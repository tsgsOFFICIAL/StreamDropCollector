using System.Text.Json;
using Core.Interfaces;
using Core.Logging;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Reads the channels the logged-in Kick account follows via an authenticated in-page fetch.
    /// </summary>
    /// <remarks>
    /// Uses Kick's internal <c>GET /api/v2/channels/followed</c> route (not in the public API), which answers with
    /// <c>{ "channels": [ { "channel_slug", "is_live", "viewer_count", ... } ] }</c>. Raw responses are logged at debug
    /// level to make adjustments easy if Kick changes the shape.
    /// </remarks>
    public static class KickFollowedChannelsClient
    {
        private const string KickOrigin = "https://kick.com/";
        private const string FollowedUrl = "https://kick.com/api/v2/channels/followed";
        private const int MaxPages = 20;

        /// <summary>
        /// Fetches the followed channel logins in the order Kick returns them, or <see langword="null"/> when unavailable.
        /// </summary>
        /// <param name="host">Kick WebView host of the account.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task<IReadOnlyList<string>?> FetchAsync(IWebViewHost host, CancellationToken ct = default)
        {
            try
            {
                await host.EnsureInitializedAsync();

                // The in-page fetch needs a kick.com page; only navigate when not on one so a watched stream is left alone.
                if (!await WebViewJsonFetch.IsOnOriginAsync(host, KickOrigin, ct))
                    await host.NavigateAsync(KickOrigin);

                string? encodedToken = await host.GetCookieValueAsync("https://kick.com", "session_token");
                if (string.IsNullOrEmpty(encodedToken))
                {
                    AppLogger.Warn("KickFollows", "session_token cookie not found; the account is not logged in.");
                    return null;
                }

                string token = JsonSerializer.Serialize(Uri.UnescapeDataString(encodedToken));
                List<string> logins = [];
                string? cursor = null;

                for (int page = 0; page < MaxPages; page++)
                {
                    ct.ThrowIfCancellationRequested();

                    string url = cursor is null ? FollowedUrl : $"{FollowedUrl}?cursor={Uri.EscapeDataString(cursor)}";
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
                        return page == 0 ? null : logins;

                    using JsonDocument env = JsonDocument.Parse(envelope);
                    if (env.RootElement.TryGetProperty("__fetchError", out _))
                    {
                        AppLogger.Warn("KickFollows", $"{url} -> HTTP {env.RootElement.GetProperty("status").GetInt32()}");
                        return page == 0 ? null : logins;
                    }

                    string body = env.RootElement.GetProperty("body").GetString() ?? string.Empty;
                    AppLogger.Debug("KickFollows", $"{url} OK body={body}");

                    (IReadOnlyList<string> pageLogins, string? nextCursor) = Parse(body);
                    logins.AddRange(pageLogins);

                    if (string.IsNullOrEmpty(nextCursor) || pageLogins.Count == 0 || nextCursor == cursor)
                        break;

                    cursor = nextCursor;
                }

                return logins;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warn("KickFollows", $"Followed channels fetch failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses a followed-channels payload into lowercase logins and the cursor of the next page, if any.
        /// </summary>
        public static (IReadOnlyList<string> Logins, string? NextCursor) Parse(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("channels", out JsonElement channels)
                    || channels.ValueKind != JsonValueKind.Array)
                {
                    return ([], null);
                }

                List<string> logins = [];
                foreach (JsonElement channel in channels.EnumerateArray())
                {
                    if (channel.ValueKind == JsonValueKind.Object
                        && channel.TryGetProperty("channel_slug", out JsonElement slug)
                        && slug.ValueKind == JsonValueKind.String
                        && slug.GetString() is { Length: > 0 } login)
                    {
                        logins.Add(login.ToLowerInvariant());
                    }
                }

                string? next = root.TryGetProperty("nextCursor", out JsonElement cursor) && cursor.ValueKind is JsonValueKind.String or JsonValueKind.Number
                    ? cursor.ToString()
                    : null;

                return (logins, next);
            }
            catch (JsonException)
            {
                return ([], null);
            }
        }
    }
}
