using System.Text.Json;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Reads the logged-in account's Kick viewer level via an authenticated in-page fetch.
    /// </summary>
    /// <remarks>
    /// The endpoint is Kick's internal <c>gamification/user/level</c> route (not in the public API). The exact host is not
    /// documented, so candidates are tried in order and the first working one is remembered. Raw responses are logged
    /// at debug level to make adjustments easy if Kick changes the shape.
    /// </remarks>
    public static class KickLevelClient
    {
        private static readonly string[] CandidateUrls =
        [
            "https://web.kick.com/api/v1/gamification/user/level",
            "https://kick.com/api/v1/gamification/user/level"
        ];

        private static string? _workingUrl;

        /// <summary>
        /// Fetches the current level progress, or <see langword="null"/> when unavailable.
        /// </summary>
        /// <param name="host">Kick WebView host currently on a kick.com origin.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task<KickLevelProgress?> FetchAsync(IWebViewHost host, CancellationToken ct = default)
        {
            try
            {
                string? encodedToken = await host.GetCookieValueAsync("https://kick.com", "session_token");
                if (string.IsNullOrEmpty(encodedToken))
                    return null;

                string token = JsonSerializer.Serialize(Uri.UnescapeDataString(encodedToken));

                foreach (string url in _workingUrl is null ? CandidateUrls : [_workingUrl])
                {
                    string script = $@"
                        const r = await fetch({JsonSerializer.Serialize(url)}, {{
                            method: 'GET',
                            credentials: 'include',
                            headers: {{ 'Accept': 'application/json', 'Authorization': 'Bearer ' + {token} }}
                        }});
                        if (!r.ok) return JSON.stringify({{ __fetchError: true, status: r.status }});
                        return JSON.stringify({{ status: r.status, body: await r.text() }});";

                    string? envelope = await host.ExecuteAsyncScriptAsync(script, 15000, ct);
                    if (string.IsNullOrWhiteSpace(envelope))
                        continue;

                    using JsonDocument env = JsonDocument.Parse(envelope);
                    if (env.RootElement.TryGetProperty("__fetchError", out _))
                    {
                        AppLogger.Debug("KickLevel",
                            $"{url} -> HTTP {env.RootElement.GetProperty("status").GetInt32()}");
                        continue;
                    }

                    string body = env.RootElement.GetProperty("body").GetString() ?? string.Empty;
                    AppLogger.Debug("KickLevel", $"{url} OK body={body}");

                    KickLevelProgress? parsed = Parse(body);
                    if (parsed != null)
                    {
                        _workingUrl = url;
                        return parsed;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warn("KickLevel", $"Level fetch failed: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Parses a level payload, accepting fields at the root or nested under <c>data</c>.
        /// </summary>
        public static KickLevelProgress? Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement data) &&
                    data.ValueKind == JsonValueKind.Object)
                    root = data;

                if (root.ValueKind != JsonValueKind.Object
                    || !TryGetLong(root, "level", out long level)
                    || !TryGetLong(root, "progress_xp", out long progress)
                    || !TryGetLong(root, "xp_to_next_level", out long toNext))
                {
                    return null;
                }

                long? total = TryGetLong(root, "total_xp", out long t) ? t : null;
                string? badgeUrl = root.TryGetProperty("badge", out JsonElement badge)
                    && badge.ValueKind == JsonValueKind.Object
                    && badge.TryGetProperty("image_url", out JsonElement img)
                    && img.ValueKind == JsonValueKind.String
                        ? img.GetString()
                        : null;

                return new KickLevelProgress((int)level, progress, toNext, total, badgeUrl);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool TryGetLong(JsonElement obj, string name, out long value)
        {
            value = 0;
            return obj.TryGetProperty(name, out JsonElement el)
                   && el.ValueKind == JsonValueKind.Number
                   && el.TryGetInt64(out value);
        }
    }
}