using System.Text.Json;
using System.Text.Json.Nodes;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Fetches authoritative watch progress from Twitch and Kick without disrupting active stream mining.
    /// </summary>
    /// <remarks>
    /// Twitch progress uses a cached-header HTTP GQL call. Kick progress uses an authenticated in-page
    /// <c>fetch</c> so the mining WebView never navigates away from the active stream.
    /// </remarks>
    public static class ServerProgressFetcher
    {
        private const string KickProgressUrl = "https://web.kick.com/api/v1/drops/progress";

        /// <summary>
        /// Retrieves reward progress for the specified Twitch campaigns via a cached-header Inventory GQL query.
        /// </summary>
        /// <param name="gql">Twitch GQL service with headers already captured from a prior dashboard load.</param>
        /// <param name="campaigns">Locally tracked campaigns to match against server inventory entries.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A map of campaign id to server-reported rewards. Returns an empty dictionary when the query is
        /// skipped, fails, or no matching campaigns are in progress on Twitch.
        /// </returns>
        public static async Task<IReadOnlyDictionary<string, IReadOnlyList<DropsReward>>> FetchTwitchRewardProgressAsync(
            IGqlService gql,
            IReadOnlyCollection<DropsCampaign> campaigns,
            CancellationToken ct = default)
        {
            if (campaigns.Count == 0)
                return new Dictionary<string, IReadOnlyList<DropsReward>>();

            try
            {
                JsonObject? inventoryResponse = await gql.QueryInventoryProgressAsync(ct);
                if (inventoryResponse == null)
                {
                    AppLogger.Warn("ProgressSync", "Twitch inventory query returned null response.");
                    return new Dictionary<string, IReadOnlyList<DropsReward>>();
                }

                JsonArray dropCampaignsInProgress = inventoryResponse["data"]?["currentUser"]?["inventory"]?["dropCampaignsInProgress"]?.AsArray() ?? new JsonArray();
                JsonArray gameEventDrops = inventoryResponse["data"]?["currentUser"]?["inventory"]?["gameEventDrops"]?.AsArray() ?? new JsonArray();

                Dictionary<string, DropsCampaign> localById = campaigns.ToDictionary(c => c.Id, StringComparer.Ordinal);
                Dictionary<string, IReadOnlyList<DropsReward>> result = new(StringComparer.Ordinal);

                foreach (JsonObject? matchingProgress in dropCampaignsInProgress.OfType<JsonObject>())
                {
                    string? campaignId = matchingProgress["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(campaignId) || !localById.TryGetValue(campaignId, out DropsCampaign? localCampaign))
                        continue;

                    JsonArray? timeBasedDropsProgress = matchingProgress["timeBasedDrops"]?.AsArray();
                    if (timeBasedDropsProgress == null)
                        continue;

                    List<DropsReward> rewards = new();
                    foreach (DropsReward localReward in localCampaign.Rewards)
                    {
                        JsonObject? matchingDropProgress = timeBasedDropsProgress.OfType<JsonObject>()
                            .FirstOrDefault(d => d["id"]?.GetValue<string>() == localReward.Id);

                        int progressMinutes = localReward.ProgressMinutes;
                        bool isClaimed = localReward.IsClaimed;

                        if (matchingDropProgress != null)
                        {
                            progressMinutes = matchingDropProgress["self"]?["currentMinutesWatched"]?.GetValue<int>() ?? progressMinutes;
                            isClaimed = matchingDropProgress["self"]?["isClaimed"]?.GetValue<bool>() ?? isClaimed;
                        }

                        // Completed event drops override in-progress minutes for claim state.
                        JsonObject? matchingEventDrop = gameEventDrops.OfType<JsonObject>()
                            .FirstOrDefault(e => e["id"]?.GetValue<string>() == localReward.DropInstanceId);

                        if (matchingEventDrop != null)
                        {
                            isClaimed = true;
                            progressMinutes = localReward.RequiredMinutes;
                        }

                        rewards.Add(localReward with
                        {
                            ProgressMinutes = progressMinutes,
                            IsClaimed = isClaimed
                        });
                    }

                    if (rewards.Count > 0)
                        result[campaignId] = rewards.AsReadOnly();
                }

                return result;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ProgressSync", $"Twitch progress fetch failed: {ex.Message}");
                return new Dictionary<string, IReadOnlyList<DropsReward>>();
            }
        }

        /// <summary>
        /// Retrieves reward progress for the specified Kick campaigns via an authenticated in-page fetch.
        /// </summary>
        /// <param name="host">Kick WebView host currently on a kick.com origin (typically the mined stream page).</param>
        /// <param name="campaigns">Locally tracked campaigns to match against the progress API payload.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A map of campaign id to server-reported rewards. Returns an empty dictionary when auth is missing,
        /// the request fails, or no matching campaigns appear in the response.
        /// </returns>
        public static async Task<IReadOnlyDictionary<string, IReadOnlyList<DropsReward>>> FetchKickRewardProgressAsync(
            IWebViewHost host,
            IReadOnlyCollection<DropsCampaign> campaigns,
            CancellationToken ct = default)
        {
            if (campaigns.Count == 0)
                return new Dictionary<string, IReadOnlyList<DropsReward>>();

            try
            {
                string? rawProgress = await FetchKickProgressPayloadAsync(host, ct);
                if (string.IsNullOrWhiteSpace(rawProgress))
                {
                    AppLogger.Warn("ProgressSync", "Kick progress fetch returned no payload (in-page fetch failed, timed out, or returned empty).");
                    return new Dictionary<string, IReadOnlyList<DropsReward>>();
                }

                using JsonDocument progressDoc = JsonDocument.Parse(rawProgress);
                if (!progressDoc.RootElement.TryGetProperty("data", out JsonElement progressArray))
                {
                    AppLogger.Warn("ProgressSync", $"Kick progress payload missing 'data' property. payloadLength={rawProgress.Length}");
                    return new Dictionary<string, IReadOnlyList<DropsReward>>();
                }

                Dictionary<string, DropsCampaign> localById = campaigns.ToDictionary(c => c.Id, StringComparer.Ordinal);
                Dictionary<string, IReadOnlyList<DropsReward>> result = new(StringComparer.Ordinal);

                foreach (JsonElement item in progressArray.EnumerateArray())
                {
                    string campaignId = item.GetProperty("id").GetString()!;
                    if (!localById.TryGetValue(campaignId, out DropsCampaign? localCampaign))
                        continue;

                    // Kick reports campaign-level progress_units; apply to each reward cap.
                    int campaignProgressUnits = item.GetProperty("progress_units").GetInt32();
                    Dictionary<string, JsonElement> serverRewards = item.GetProperty("rewards")
                        .EnumerateArray()
                        .ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);

                    List<DropsReward> rewards = new();
                    foreach (DropsReward localReward in localCampaign.Rewards)
                    {
                        if (!serverRewards.TryGetValue(localReward.Id, out JsonElement serverReward))
                        {
                            rewards.Add(localReward);
                            continue;
                        }

                        rewards.Add(localReward with
                        {
                            ProgressMinutes = Math.Min(campaignProgressUnits, localReward.RequiredMinutes),
                            IsClaimed = serverReward.GetProperty("claimed").GetBoolean()
                        });
                    }

                    if (rewards.Count > 0)
                        result[campaignId] = rewards.AsReadOnly();
                }

                return result;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ProgressSync", $"Kick progress fetch failed: {ex.Message}");
                return new Dictionary<string, IReadOnlyList<DropsReward>>();
            }
        }

        /// <summary>
        /// Performs an authenticated GET to Kick's progress API from the current page context.
        /// </summary>
        /// <remarks>
        /// Kick returns 403 without a Bearer token. The plain credentials-only fetch used elsewhere is not sufficient.
        /// </remarks>
        private static async Task<string?> FetchKickProgressPayloadAsync(IWebViewHost host, CancellationToken ct)
        {
            string? encodedToken = await host.GetCookieValueAsync("https://kick.com", "session_token");
            if (string.IsNullOrEmpty(encodedToken))
            {
                AppLogger.Warn("ProgressSync", "Kick progress fetch skipped - session_token cookie not found.");
                return null;
            }

            string bearerToken = Uri.UnescapeDataString(encodedToken);
            string serializedToken = JsonSerializer.Serialize(bearerToken);
            string serializedUrl = JsonSerializer.Serialize(KickProgressUrl);

            string asyncBody = $@"
                const response = await fetch({serializedUrl}, {{
                    method: 'GET',
                    credentials: 'include',
                    headers: {{
                        'Accept': 'application/json',
                        'Authorization': 'Bearer ' + {serializedToken}
                    }}
                }});

                if (!response.ok) {{
                    return JSON.stringify({{ __fetchError: true, status: response.status }});
                }}

                return await response.text();
            ";

            AppLogger.Debug("ProgressSync", $"Kick progress in-page fetch START url={KickProgressUrl}");
            string? payload = await host.ExecuteAsyncScriptAsync(asyncBody, 20000, ct);
            if (string.IsNullOrWhiteSpace(payload))
            {
                AppLogger.Warn("ProgressSync", "Kick progress in-page script returned null/empty (see preceding WebViewAsync warning for the underlying script failure, if any).");
                return null;
            }

            if (payload.Contains("__fetchError", StringComparison.Ordinal))
            {
                AppLogger.Warn("ProgressSync", $"Kick progress in-page fetch failed: {payload}");
                return null;
            }

            return payload;
        }
    }
}