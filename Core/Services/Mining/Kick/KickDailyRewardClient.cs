using System.Text.Json;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Reads and claims the logged-in account's Kick daily reward via authenticated in-page fetches.
    /// </summary>
    /// <remarks>
    /// Progress comes from Kick's internal <c>gamification/challenges</c> route. The claim request has not been
    /// captured yet, so <see cref="ClaimUrlTemplate"/> is unset and <see cref="ClaimAsync"/> only logs until it is filled in.
    /// </remarks>
    public static class KickDailyRewardClient
    {
        private const string ChallengesUrl = "https://web.kick.com/api/v1/gamification/challenges";
        private const string StreakUrlFormat = "https://web.kick.com/api/v1/gamification/users/{0}/streak";

        // Kick's own site calls this route for the signed-in user; the response is logged at debug level in case its shape differs.
        private const string UserUrl = "https://kick.com/api/v1/user";

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> UserIds = new();

        /// <summary>
        /// Claim URL with a <c>{id}</c> placeholder for the challenge id. <see langword="null"/> until the real request is captured.
        /// </summary>
        private const string? ClaimUrlTemplate = null;

        /// <summary>
        /// Fetches the current daily challenge, or <see langword="null"/> when unavailable or none is active.
        /// </summary>
        /// <param name="host">Kick WebView host currently on a kick.com origin.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task<KickChallenge?> FetchDailyAsync(IWebViewHost host, CancellationToken ct = default)
        {
            string? body = await SendAsync(host, "GET", ChallengesUrl, requestBody: null, ct);
            if (body is null)
                return null;

            AppLogger.Debug("KickDaily", $"challenges body={body}");
            return ParseDaily(body);
        }

        /// <summary>
        /// Fetches the current daily streak length in days, or <see langword="null"/> when unavailable.
        /// The streak route is keyed by the numeric user id, which is looked up once per account and cached.
        /// </summary>
        public static async Task<int?> FetchStreakDaysAsync(IWebViewHost host, CancellationToken ct = default)
        {
            if (!UserIds.TryGetValue(host.AccountId, out long userId))
            {
                string? userBody = await SendAsync(host, "GET", UserUrl, requestBody: null, ct);
                if (userBody is null)
                    return null;

                AppLogger.Debug("KickDaily", $"user body={userBody}");
                if (ParseUserId(userBody) is not { } parsedId)
                    return null;

                userId = parsedId;
                UserIds[host.AccountId] = userId;
            }

            string? body = await SendAsync(host, "GET", string.Format(StreakUrlFormat, userId), requestBody: null, ct);
            if (body is null)
                return null;

            AppLogger.Debug("KickDaily", $"streak body={body}");
            return ParseStreakDays(body);
        }

        /// <summary>
        /// Parses a streak payload (<c>{"data":{"length_days":N}}</c>) into the streak length.
        /// </summary>
        public static int? ParseStreakDays(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("data", out JsonElement data)
                    && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("length_days", out JsonElement days)
                    && days.TryGetInt32(out int value)
                        ? value
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Parses the numeric user id from a user payload, accepting it at the root or under <c>data</c>.
        /// </summary>
        public static long? ParseUserId(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data)
                    && data.ValueKind == JsonValueKind.Object)
                    root = data;

                return root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("id", out JsonElement id)
                    && id.ValueKind == JsonValueKind.Number
                    && id.TryGetInt64(out long value)
                        ? value
                        : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Claims the given challenge. Returns <see langword="false"/> while the claim endpoint is not configured.
        /// </summary>
        public static async Task<bool> ClaimAsync(IWebViewHost host, KickChallenge challenge, CancellationToken ct = default)
        {
            if (ClaimUrlTemplate is null)
            {
                AppLogger.Warn("KickDaily", $"Daily reward '{challenge.Id}' is claimable but the claim endpoint is not configured yet.");
                return false;
            }

            string url = ClaimUrlTemplate.Replace("{id}", Uri.EscapeDataString(challenge.Id));
            string? body = await SendAsync(host, "POST", url, requestBody: "{}", ct);
            AppLogger.Debug("KickDaily", $"claim id={challenge.Id} body={body ?? "(failed)"}");
            return body is not null;
        }

        /// <summary>
        /// Parses a challenges payload and returns the daily challenge, or <see langword="null"/> when there is none.
        /// </summary>
        public static KickChallenge? ParseDaily(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
                    return null;

                foreach (JsonElement item in data.EnumerateArray())
                {
                    KickChallenge? challenge = ParseChallenge(item);
                    if (challenge is { IsDaily: true })
                        return challenge;
                }
            }
            catch (JsonException)
            {
            }

            return null;
        }

        private static KickChallenge? ParseChallenge(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object || GetString(item, "id") is not { Length: > 0 } id)
                return null;

            long progress = 0, threshold = 0;
            string conditionType = string.Empty;
            if (item.TryGetProperty("condition", out JsonElement condition) && condition.ValueKind == JsonValueKind.Object)
            {
                conditionType = GetString(condition, "type") ?? string.Empty;
                if (condition.TryGetProperty("progress", out JsonElement p) && p.TryGetInt64(out long pv))
                    progress = pv;
                if (condition.TryGetProperty("threshold", out JsonElement t) && t.TryGetInt64(out long tv))
                    threshold = tv;
            }

            DateTimeOffset? endsAt = null;
            if (item.TryGetProperty("window", out JsonElement window) && window.ValueKind == JsonValueKind.Object
                && DateTimeOffset.TryParse(GetString(window, "ends_at"), out DateTimeOffset parsedEnd))
                endsAt = parsedEnd;

            return new KickChallenge(
                id,
                GetString(item, "name") ?? string.Empty,
                GetString(item, "recurrence") ?? string.Empty,
                GetString(item, "status") ?? string.Empty,
                conditionType,
                progress,
                threshold,
                endsAt);
        }

        private static string? GetString(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        private static async Task<string?> SendAsync(IWebViewHost host, string method, string url, string? requestBody, CancellationToken ct)
        {
            try
            {
                string? encodedToken = await host.GetCookieValueAsync("https://kick.com", "session_token");
                if (string.IsNullOrEmpty(encodedToken))
                    return null;

                string token = JsonSerializer.Serialize(Uri.UnescapeDataString(encodedToken));
                string bodyOption = requestBody is null ? string.Empty : $", body: {JsonSerializer.Serialize(requestBody)}";

                string script = $@"
                    const r = await fetch({JsonSerializer.Serialize(url)}, {{
                        method: {JsonSerializer.Serialize(method)},
                        credentials: 'include',
                        headers: {{ 'Accept': 'application/json', 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + {token} }}{bodyOption}
                    }});
                    if (!r.ok) return JSON.stringify({{ __fetchError: true, status: r.status }});
                    return JSON.stringify({{ status: r.status, body: await r.text() }});";

                string? envelope = await host.ExecuteAsyncScriptAsync(script, 15000, ct);
                if (string.IsNullOrWhiteSpace(envelope))
                    return null;

                using JsonDocument env = JsonDocument.Parse(envelope);
                if (env.RootElement.TryGetProperty("__fetchError", out _))
                {
                    AppLogger.Debug("KickDaily", $"{method} {url} -> HTTP {env.RootElement.GetProperty("status").GetInt32()}");
                    return null;
                }

                return env.RootElement.GetProperty("body").GetString();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warn("KickDaily", $"{method} {url} failed: {ex.Message}");
                return null;
            }
        }
    }
}
