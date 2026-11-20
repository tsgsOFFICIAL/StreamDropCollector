using System.Text.Json.Nodes;
using System.Text.Json;
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
        /// The server-reported rewards per campaign id, and whether Twitch actually answered: an empty map from a
        /// failed or skipped query must not be read as "Twitch tracks none of these campaigns".
        /// </returns>
        public static async Task<TwitchServerProgress> FetchTwitchRewardProgressAsync(
            IGqlService gql,
            IReadOnlyCollection<DropsCampaign> campaigns,
            CancellationToken ct = default)
        {
            if (campaigns.Count == 0)
                return TwitchServerProgress.Unavailable;

            try
            {
                JsonObject? inventoryResponse = await gql.QueryInventoryProgressAsync(ct);
                if (inventoryResponse == null)
                {
                    AppLogger.Warn("ProgressSync", "Twitch inventory query returned null response.");
                    return TwitchServerProgress.Unavailable;
                }

                // A campaign missing from this list only means "Twitch tracks no progress for it" when the list itself
                // was returned; a null field (partial GraphQL error) says nothing about any campaign.
                if (inventoryResponse["data"]?["currentUser"]?["inventory"]?["dropCampaignsInProgress"] is not JsonArray dropCampaignsInProgress)
                {
                    AppLogger.Warn("ProgressSync", "Twitch inventory response carried no dropCampaignsInProgress list; treating progress as unavailable.");
                    return TwitchServerProgress.Unavailable;
                }

                // "gameEventDropsConnection" permanently errors on Twitch's backend - see the matching
                // comment in TwitchDropsProvider.cs. Already-claimed rewards with no in-progress record
                // come from "earnedDropRewards" instead.
                JsonArray earnedDropRewardEdges = inventoryResponse["data"]?["currentUser"]?["inventory"]?["earnedDropRewards"]?["edges"]?.AsArray() ?? new JsonArray();

                Dictionary<string, DropsCampaign> localById = campaigns.ToDictionary(c => c.Id, StringComparer.Ordinal);
                Dictionary<string, IReadOnlyList<DropsReward>> result = new(StringComparer.Ordinal);
                Dictionary<string, string?> dropInstanceIds = new(StringComparer.Ordinal);

                foreach (JsonObject? matchingProgress in dropCampaignsInProgress.OfType<JsonObject>())
                {
                    string? campaignId = matchingProgress["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(campaignId) || !localById.TryGetValue(campaignId, out DropsCampaign? localCampaign))
                        continue;

                    JsonArray? timeBasedDropsProgress = matchingProgress["timeBasedDrops"]?.AsArray();
                    if (timeBasedDropsProgress == null)
                    {
                        // Listed but without drop records: the campaign is tracked, so it must not look absent.
                        result[campaignId] = localCampaign.Rewards;
                        continue;
                    }

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

                            if (matchingDropProgress["self"] is JsonObject)
                                dropInstanceIds[TwitchServerProgress.DropKey(campaignId, localReward.Id)] = matchingDropProgress["self"]?["dropInstanceID"]?.GetValue<string>();
                        }

                        // Completed earned drops override in-progress minutes for claim state.
                        JsonObject? matchingEarnedDrop = earnedDropRewardEdges.OfType<JsonObject>()
                            .Select(edge => edge["node"]?.AsObject())
                            .FirstOrDefault(node =>
                                node?["item"]?["id"]?.GetValue<string>() == localReward.DropInstanceId &&
                                node?["status"]?.GetValue<string>() == "CLAIMED");

                        if (matchingEarnedDrop != null &&
                            EarnedRewardMatcher.IsClaimedByEarnedEntries(localReward, campaignId, localCampaign.Rewards, earnedDropRewardEdges, matchingDropProgress != null))
                        {
                            // Diagnostics only (no behaviour change): flag earned entries from another campaign.
                            string? earnedCampaignId = matchingEarnedDrop["campaign"]?["id"]?.GetValue<string>();
                            if (!string.Equals(earnedCampaignId, campaignId, StringComparison.Ordinal))
                            {
                                AppLogger.Warn(
                                    "ProgressSync",
                                    $"Earned entry matched by benefit id but belongs to a different campaign. " +
                                    $"currentCampaignId={campaignId} dropId={localReward.Id} benefitId={localReward.DropInstanceId} " +
                                    $"earnedCampaignId={earnedCampaignId ?? "null"} earnedAt={matchingEarnedDrop["earnedAt"]?.ToString() ?? "null"} " +
                                    $"inProgressClaimedBefore={isClaimed} progressBefore={progressMinutes}/{localReward.RequiredMinutes}");
                            }

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

                // Twitch grants some badge drops the moment the last minute is credited, and a finished campaign then drops
                // out of "in progress". Its earned entries are the only trace, so a campaign missing from the list but
                // present there is finished, not untracked.
                foreach (DropsCampaign localCampaign in campaigns.Where(c => !result.ContainsKey(c.Id)))
                {
                    List<DropsReward> rewards = [.. localCampaign.Rewards.Select(reward =>
                        EarnedRewardMatcher.IsClaimedByEarnedEntries(reward, localCampaign.Id, localCampaign.Rewards, earnedDropRewardEdges, false)
                            ? reward with { ProgressMinutes = reward.RequiredMinutes, IsClaimed = true }
                            : reward)];

                    if (!rewards.Any(r => r.IsClaimed && !localCampaign.Rewards.First(l => l.Id == r.Id).IsClaimed))
                        continue;

                    AppLogger.Info("ProgressSync", $"Twitch campaign {localCampaign.Id} is not in progress but its rewards are in the earned list; treating them as granted.");
                    result[localCampaign.Id] = rewards.AsReadOnly();
                    foreach (DropsReward reward in rewards.Where(r => r.IsClaimed))
                        dropInstanceIds[TwitchServerProgress.DropKey(localCampaign.Id, reward.Id)] = null;
                }

                return new TwitchServerProgress(true, result, dropInstanceIds);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ProgressSync", $"Twitch progress fetch failed: {ex.Message}");
                return TwitchServerProgress.Unavailable;
            }
        }

        /// <summary>
        /// Retrieves progress for any mix of Twitch drops campaigns and open reward campaigns, each from its own inventory query.
        /// </summary>
        /// <returns>Both answers merged; <see cref="TwitchServerProgress.Succeeded"/> only when every query that was needed answered.</returns>
        public static async Task<TwitchServerProgress> FetchTwitchProgressAsync(
            IGqlService gql,
            IReadOnlyCollection<DropsCampaign> campaigns,
            CancellationToken ct = default)
        {
            List<DropsCampaign> rewardCampaigns = [.. campaigns.Where(c => c.IsRewardCampaign)];
            if (rewardCampaigns.Count == 0)
                return await FetchTwitchRewardProgressAsync(gql, campaigns, ct);

            TwitchServerProgress rewardProgress = await FetchTwitchRewardCampaignProgressAsync(gql, rewardCampaigns, ct);
            if (rewardCampaigns.Count == campaigns.Count)
                return rewardProgress;

            TwitchServerProgress dropProgress = await FetchTwitchRewardProgressAsync(gql, [.. campaigns.Where(c => !c.IsRewardCampaign)], ct);
            if (!rewardProgress.Succeeded || !dropProgress.Succeeded)
                return TwitchServerProgress.Unavailable;

            return new TwitchServerProgress(
                true,
                dropProgress.Campaigns.Concat(rewardProgress.Campaigns).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
                dropProgress.DropInstanceIds);
        }

        /// <summary>
        /// Retrieves progress for Twitch open reward campaigns from <c>ViewerRewardDropInventory</c>, without navigating the WebView.
        /// </summary>
        /// <param name="gql">Twitch GQL service with headers and the query hash already captured by a prior dashboard load.</param>
        /// <param name="campaigns">Locally tracked reward campaigns to match against the viewer's in-progress ones.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// Server-reported rewards per campaign id. A campaign Twitch has not started tracking is absent from the map, and
        /// <see cref="TwitchServerProgress.Succeeded"/> is only <see langword="true"/> when Twitch actually returned its list.
        /// </returns>
        public static async Task<TwitchServerProgress> FetchTwitchRewardCampaignProgressAsync(
            IGqlService gql,
            IReadOnlyCollection<DropsCampaign> campaigns,
            CancellationToken ct = default)
        {
            if (campaigns.Count == 0)
                return TwitchServerProgress.Unavailable;

            try
            {
                JsonObject? response = await gql.QueryRewardCampaignProgressAsync(ct, allowHashRefresh: false);
                if (response?["data"]?["currentUser"]?["inventory"]?["viewerRewardDropCampaignsInProgress"] is not JsonArray inProgress)
                {
                    AppLogger.Debug("ProgressSync", "Twitch reward campaign inventory unavailable; treating progress as unknown.");
                    return TwitchServerProgress.Unavailable;
                }

                Dictionary<string, DropsCampaign> localById = campaigns.ToDictionary(c => c.Id, StringComparer.Ordinal);
                Dictionary<string, IReadOnlyList<DropsReward>> result = new(StringComparer.Ordinal);

                foreach (JsonObject serverCampaign in inProgress.OfType<JsonObject>())
                {
                    string? campaignId = serverCampaign["id"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(campaignId) || !localById.TryGetValue(campaignId, out DropsCampaign? local))
                        continue;

                    Dictionary<string, JsonObject> groups = (serverCampaign["rewardGroups"]?.AsArray() ?? [])
                        .OfType<JsonObject>()
                        .Where(g => g["id"] != null)
                        .GroupBy(g => g["id"]!.GetValue<string>(), StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                    LogRewardGroupStates(local, groups);

                    List<DropsReward> rewards = new(local.Rewards.Count);
                    foreach (DropsReward localReward in local.Rewards)
                    {
                        // A chest's rewards are tracked as "groupId:itemId" (see ParseRewardCampaign); Twitch reports the group.
                        string groupId = localReward.Id.Split(':')[0];
                        if (!groups.TryGetValue(groupId, out JsonObject? group) || group["self"] is not JsonObject self)
                        {
                            rewards.Add(localReward);
                            continue;
                        }

                        bool claimed = self["status"]?.GetValue<string>() == "CLAIMED";
                        int watched = self["currentMinutesWatched"]?.GetValue<int>() ?? localReward.ProgressMinutes;

                        // A chest grants one of its pooled rewards per completed window.
                        if (localReward.Id.Contains(':'))
                        {
                            int index = local.Rewards.Where(r => r.Id.StartsWith(groupId + ":", StringComparison.Ordinal))
                                .ToList().FindIndex(r => r.Id == localReward.Id);
                            (bool poolClaimed, int poolProgress, DateTimeOffset? poolAvailableAt) = RewardCampaignPoolGrants.Resolve(
                                Math.Max(0, index), self["grantCount"]?.GetValue<int>() ?? 0, watched, localReward.RequiredMinutes, claimed,
                                RewardCampaignPoolGrants.ReadWindowExpiry(self));

                            rewards.Add(localReward with { ProgressMinutes = poolProgress, IsClaimed = poolClaimed, AvailableAt = poolAvailableAt });
                            continue;
                        }

                        rewards.Add(localReward with
                        {
                            ProgressMinutes = claimed ? localReward.RequiredMinutes : Math.Min(watched, localReward.RequiredMinutes),
                            IsClaimed = claimed
                        });
                    }

                    result[campaignId] = rewards.AsReadOnly();
                }

                LogRewardCampaignPresence(campaigns, result.Keys.ToHashSet(StringComparer.Ordinal));

                return new TwitchServerProgress(true, result, new Dictionary<string, string?>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warn("ProgressSync", $"Twitch reward campaign progress fetch failed: {ex.Message}");
                return TwitchServerProgress.Unavailable;
            }
        }

        private sealed record GroupSeen(int Minutes, int GrantCount, string? WindowExpiresAt, bool Completed);

        private static readonly Dictionary<string, GroupSeen> SeenRewardGroups = new(StringComparer.Ordinal);
        private static readonly HashSet<string> AbsentRewardCampaigns = new(StringComparer.Ordinal);

        /// <summary>
        /// Names each event a reward group goes through, one Info line each, so the log reads as a story: first seen, minutes
        /// reset, window completed, reward granted (and which badges that makes granted), window rolled over.
        /// </summary>
        private static void LogRewardGroupTransitions(DropsCampaign local, string groupId, JsonObject self, int required)
        {
            int minutes = self["currentMinutesWatched"]?.GetValue<int>() ?? 0;
            int grantCount = self["grantCount"]?.GetValue<int>() ?? 0;
            string? expires = self["currentWindow"]?["expiresAt"]?.GetValue<string>();
            bool completed = required > 0 && minutes >= required;
            string who = $"'{local.Name}' group={groupId}";

            GroupSeen? before;
            string key = $"{local.Id}#{groupId}";
            lock (SeenRewardGroups)
            {
                SeenRewardGroups.TryGetValue(key, out before);
                SeenRewardGroups[key] = new GroupSeen(minutes, grantCount, expires, completed);
            }

            if (before == null)
            {
                AppLogger.Info("RewardGroup", $"FIRST SEEN {who}: minutes={minutes}/{required} grantCount={grantCount} windowExpires={expires ?? "none"}");
                return;
            }

            if (minutes < before.Minutes)
                AppLogger.Info("RewardGroup", $"MINUTES RESET {who}: {before.Minutes} -> {minutes} (window {before.WindowExpiresAt ?? "none"} -> {expires ?? "none"})");

            if (expires != before.WindowExpiresAt)
                AppLogger.Info("RewardGroup", $"WINDOW CHANGED {who}: {before.WindowExpiresAt ?? "none"} -> {expires ?? "none"}");

            if (completed && !before.Completed)
                AppLogger.Info("RewardGroup", $"WINDOW COMPLETE {who}: {minutes}/{required} minutes, grantCount={grantCount}; waiting for Twitch to grant.");

            if (grantCount > before.GrantCount)
            {
                // Twitch does not say which pooled reward it granted; the first grantCount ones in pool order are assumed.
                string assumed = string.Join(", ", local.Rewards
                    .Where(r => r.Id.StartsWith(groupId + ":", StringComparison.Ordinal))
                    .Take(grantCount)
                    .Select(r => r.Name));

                AppLogger.Info("RewardGroup", $"GRANT DETECTED {who}: grantCount {before.GrantCount} -> {grantCount} at minutes={minutes}/{required}. " +
                    $"Granted badges (assumed pool order): [{assumed}]. Check Twitch's notification for the real one.");
            }
            else if (grantCount < before.GrantCount)
            {
                AppLogger.Warn("RewardGroup", $"GRANT COUNT DROPPED {who}: {before.GrantCount} -> {grantCount}; unexpected.");
            }
        }

        /// <summary>
        /// Notes campaigns that vanish from, or return to, Twitch's in-progress reward list.
        /// </summary>
        private static void LogRewardCampaignPresence(IReadOnlyCollection<DropsCampaign> campaigns, ISet<string> presentIds)
        {
            foreach (DropsCampaign campaign in campaigns)
            {
                bool present = presentIds.Contains(campaign.Id);
                bool wasAbsent;
                lock (AbsentRewardCampaigns)
                    wasAbsent = present ? AbsentRewardCampaigns.Remove(campaign.Id) : !AbsentRewardCampaigns.Add(campaign.Id);

                if (present && wasAbsent)
                    AppLogger.Info("RewardGroup", $"'{campaign.Name}' ({campaign.Id}) is back in Twitch's in-progress reward list.");
                else if (!present && !wasAbsent)
                    AppLogger.Info("RewardGroup", $"'{campaign.Name}' ({campaign.Id}) is not in Twitch's in-progress reward list (not started, or Twitch does not count the current stream).");
            }
        }

        /// <summary>The last state logged per reward group, so only changes are written at Info level.</summary>
        private static readonly Dictionary<string, string> LoggedRewardGroupStates = new(StringComparer.Ordinal);

        /// <summary>
        /// Logs what Twitch reports for each of a reward campaign's groups: one Debug line per sync, and an Info line with the
        /// raw <c>self</c> block whenever anything in it changes (status, minutes, grants, window, earned reward). This is how a
        /// grant shows up, so the raw block is kept to catch any field that names the granted reward.
        /// </summary>
        private static void LogRewardGroupStates(DropsCampaign local, IReadOnlyDictionary<string, JsonObject> groups)
        {
            foreach ((string groupId, JsonObject group) in groups)
            {
                if (group["self"] is not JsonObject self)
                {
                    AppLogger.Debug("RewardGroup", $"'{local.Name}' group={groupId} has no self block.");
                    continue;
                }

                int required = group["progressCriteria"]?["requirements"]?["minutesWatched"]?.GetValue<int>() ?? 0;
                string summary =
                    $"status={self["status"]} minutes={self["currentMinutesWatched"]}/{required} grantCount={self["grantCount"]} " +
                    $"nextEligibleAt={self["nextEligibleAt"]?.ToJsonString() ?? "null"} " +
                    $"window={self["currentWindow"]?["startedAt"]}..{self["currentWindow"]?["expiresAt"]} " +
                    $"earnedReward={self["earnedReward"]?.ToJsonString() ?? "null"}";

                AppLogger.Debug("RewardGroup", $"'{local.Name}' group={groupId} {summary}");

                // Minutes change every sync; everything else is a state change worth keeping.
                JsonObject stable = (JsonObject)self.DeepClone();
                stable.Remove("currentMinutesWatched");
                string key = $"{local.Id}#{groupId}";
                string state = stable.ToJsonString();

                bool changed;
                lock (LoggedRewardGroupStates)
                {
                    changed = !LoggedRewardGroupStates.TryGetValue(key, out string? previous) || previous != state;
                    LoggedRewardGroupStates[key] = state;
                }

                if (changed)
                    AppLogger.Info("RewardGroup", $"STATE CHANGE '{local.Name}' group={groupId}: {summary} raw={self.ToJsonString()}");

                LogRewardGroupTransitions(local, groupId, self, required);
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

                int serverItemCount = progressArray.GetArrayLength();
                List<string> serverCampaignIds = progressArray.EnumerateArray()
                    .Select(item => item.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? "(null)" : "(missing id)")
                    .ToList();

                AppLogger.Debug(
                    "ProgressSync",
                    $"Kick progress payload parsed. serverItemCount={serverItemCount} " +
                    $"serverCampaignIds=[{string.Join(", ", serverCampaignIds)}] " +
                    $"localCampaignIds=[{string.Join(", ", localById.Keys)}]");

                foreach (JsonElement item in progressArray.EnumerateArray())
                {
                    string campaignId = item.GetProperty("id").GetString()!;
                    if (!localById.TryGetValue(campaignId, out DropsCampaign? localCampaign))
                    {
                        AppLogger.Debug("ProgressSync", $"Kick progress item campaignId={campaignId} has no local match - skipped.");
                        continue;
                    }

                    // Kick reports campaign-level progress_units; apply to each reward cap.
                    int campaignProgressUnits = item.GetProperty("progress_units").GetInt32();
                    Dictionary<string, JsonElement> serverRewards = item.GetProperty("rewards")
                        .EnumerateArray()
                        .ToDictionary(r => r.GetProperty("id").GetString()!, StringComparer.Ordinal);

                    AppLogger.Debug(
                        "ProgressSync",
                        $"Kick progress item MATCHED campaignId={campaignId} progressUnits={campaignProgressUnits} " +
                        $"serverRewardIds=[{string.Join(", ", serverRewards.Keys)}]");

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
            try
            {
                IReadOnlyList<Microsoft.Web.WebView2.Core.CoreWebView2Cookie> allCookies = await host.GetCookiesAsync("https://kick.com");
                AppLogger.Debug(
                    "ProgressSync",
                    $"Kick cookie jar for https://kick.com: [{string.Join(", ", allCookies.Select(c => $"{c.Name}(len={c.Value.Length},domain={c.Domain})"))}]");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ProgressSync", $"Failed to enumerate Kick cookie jar: {ex.Message}");
            }

            string? encodedToken = await host.GetCookieValueAsync("https://kick.com", "session_token");
            if (string.IsNullOrEmpty(encodedToken))
            {
                AppLogger.Warn("ProgressSync", "Kick progress fetch skipped - session_token cookie not found.");
                return null;
            }

            string bearerToken = Uri.UnescapeDataString(encodedToken);

            // session_token is normally shaped "<id>|<hash>" (Laravel-style token). Log the id prefix and
            // hash length only - never the hash itself - so a malformed/stale cookie is visible without
            // leaking the credential.
            int pipeIndex = bearerToken.IndexOf('|');
            string tokenShapeDescription = pipeIndex > 0
                ? $"idPrefix={bearerToken[..pipeIndex]} hashLength={bearerToken.Length - pipeIndex - 1}"
                : "UNEXPECTED SHAPE (no '|' separator found)";
            AppLogger.Debug(
                "ProgressSync",
                $"Kick session_token cookie found. encodedLength={encodedToken.Length} decodedLength={bearerToken.Length} {tokenShapeDescription}");

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

                const headersObj = {{}};
                response.headers.forEach((value, key) => {{ headersObj[key] = value; }});

                if (!response.ok) {{
                    return JSON.stringify({{ __fetchError: true, status: response.status, headers: headersObj }});
                }}

                const bodyText = await response.text();
                return JSON.stringify({{ status: response.status, headers: headersObj, body: bodyText }});
            ";

            AppLogger.Debug(
                "ProgressSync",
                $"Kick progress in-page fetch START url={KickProgressUrl} authHeaderLength={("Bearer " + bearerToken).Length}");
            DateTime fetchStartedAtUtc = DateTime.UtcNow;
            string? rawEnvelope = await host.ExecuteAsyncScriptAsync(asyncBody, 20000, ct);
            TimeSpan fetchDuration = DateTime.UtcNow - fetchStartedAtUtc;
            if (string.IsNullOrWhiteSpace(rawEnvelope))
            {
                AppLogger.Warn(
                    "ProgressSync",
                    $"Kick progress in-page script returned null/empty after {fetchDuration.TotalMilliseconds:F0}ms " +
                    "(see preceding WebViewAsync warning for the underlying script failure, if any).");
                return null;
            }

            using JsonDocument envelopeDoc = JsonDocument.Parse(rawEnvelope);
            JsonElement envelopeRoot = envelopeDoc.RootElement;
            int status = envelopeRoot.TryGetProperty("status", out JsonElement statusEl) ? statusEl.GetInt32() : -1;
            string headersSummary = envelopeRoot.TryGetProperty("headers", out JsonElement headersEl)
                ? string.Join(", ", headersEl.EnumerateObject().Select(p => $"{p.Name}={p.Value.GetString()}"))
                : "(none)";

            if (envelopeRoot.TryGetProperty("__fetchError", out _))
            {
                AppLogger.Warn(
                    "ProgressSync",
                    $"Kick progress in-page fetch failed after {fetchDuration.TotalMilliseconds:F0}ms status={status} headers=[{headersSummary}]");
                return null;
            }

            string payload = envelopeRoot.TryGetProperty("body", out JsonElement bodyEl) ? bodyEl.GetString() ?? "" : "";

            AppLogger.Debug(
                "ProgressSync",
                $"Kick progress in-page fetch OK durationMs={fetchDuration.TotalMilliseconds:F0} status={status} " +
                $"headers=[{headersSummary}] bodyLength={payload.Length} rawBody={payload}");

            return payload;
        }
    }
}