using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Logging;

namespace Core.Services.Twitch.Helix
{
    internal sealed class TwitchHelixClient
    {
        private const int MaxBatchSize = 100;

        private readonly HttpClient _http;
        private readonly TwitchHelixTokenManager _tokenManager;

        public TwitchHelixClient(HttpClient http, TwitchHelixTokenManager tokenManager)
        {
            _http = http;
            _tokenManager = tokenManager;
        }

        public async Task<Dictionary<string, JsonElement>> GetUsersByLoginAsync(
            IReadOnlyList<string> logins,
            CancellationToken ct = default)
        {
            Dictionary<string, JsonElement> users = new(StringComparer.OrdinalIgnoreCase);
            if (logins.Count == 0)
                return users;

            for (int offset = 0; offset < logins.Count; offset += MaxBatchSize)
            {
                List<string> batch = logins.Skip(offset).Take(MaxBatchSize).ToList();
                string query = string.Join("&", batch.Select(l => $"login={Uri.EscapeDataString(l)}"));
                using JsonDocument doc = await GetJsonAsync($"https://api.twitch.tv/helix/users?{query}", ct);

                foreach (JsonElement user in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    string? login = user.GetProperty("login").GetString();
                    if (!string.IsNullOrWhiteSpace(login))
                        users[login] = user.Clone();
                }
            }

            return users;
        }

        public async Task<Dictionary<string, JsonElement>> GetStreamsByLoginAsync(
            IReadOnlyList<string> logins,
            CancellationToken ct = default)
        {
            Dictionary<string, JsonElement> streams = new(StringComparer.OrdinalIgnoreCase);
            if (logins.Count == 0)
                return streams;

            for (int offset = 0; offset < logins.Count; offset += MaxBatchSize)
            {
                List<string> batch = logins.Skip(offset).Take(MaxBatchSize).ToList();
                string query = string.Join("&", batch.Select(l => $"user_login={Uri.EscapeDataString(l)}"));
                using JsonDocument doc = await GetJsonAsync($"https://api.twitch.tv/helix/streams?{query}", ct);

                foreach (JsonElement stream in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    string? login = stream.GetProperty("user_login").GetString();
                    if (!string.IsNullOrWhiteSpace(login))
                        streams[login] = stream.Clone();
                }
            }

            return streams;
        }

        public async Task<Dictionary<string, JsonElement>> GetChannelsByBroadcasterIdAsync(
            IReadOnlyList<string> broadcasterIds,
            CancellationToken ct = default)
        {
            Dictionary<string, JsonElement> channels = new(StringComparer.OrdinalIgnoreCase);
            if (broadcasterIds.Count == 0)
                return channels;

            for (int offset = 0; offset < broadcasterIds.Count; offset += MaxBatchSize)
            {
                List<string> batch = broadcasterIds.Skip(offset).Take(MaxBatchSize).ToList();
                string query = string.Join("&", batch.Select(id => $"broadcaster_id={Uri.EscapeDataString(id)}"));
                using JsonDocument doc = await GetJsonAsync($"https://api.twitch.tv/helix/channels?{query}", ct);

                foreach (JsonElement channel in doc.RootElement.GetProperty("data").EnumerateArray())
                {
                    string? broadcasterId = channel.GetProperty("broadcaster_id").GetString();
                    if (!string.IsNullOrWhiteSpace(broadcasterId))
                        channels[broadcasterId] = channel.Clone();
                }
            }

            return channels;
        }

        public async Task<string?> GetUserIdByLoginAsync(string login, CancellationToken ct = default)
        {
            Dictionary<string, JsonElement> users = await GetUsersByLoginAsync([login], ct);
            if (!users.TryGetValue(login, out JsonElement user))
                return null;

            return user.GetProperty("id").GetString();
        }

        public async Task<string?> CreateEventSubSubscriptionAsync(
            string type,
            string version,
            string broadcasterUserId,
            string sessionId,
            CancellationToken ct = default)
        {
            object body = new
            {
                type,
                version,
                condition = new { broadcaster_user_id = broadcasterUserId },
                transport = new { method = "websocket", session_id = sessionId }
            };

            using HttpResponseMessage response = await PostJsonWithRefreshAsync(
                "https://api.twitch.tv/helix/eventsub/subscriptions",
                body,
                ct);

            string bodyText = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                using JsonDocument doc = JsonDocument.Parse(bodyText);
                JsonElement data = doc.RootElement.GetProperty("data");
                if (data.GetArrayLength() == 0)
                    return null;

                return data[0].GetProperty("id").GetString();
            }

            AppLogger.Warn("TwitchEventSub", $"Subscribe {type} failed for {broadcasterUserId}: {(int)response.StatusCode} {bodyText}");
            return null;
        }

        public async Task DeleteEventSubSubscriptionAsync(string subscriptionId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId))
                return;

            using HttpResponseMessage response = await SendWithRefreshAsync(
                () => new HttpRequestMessage(
                    HttpMethod.Delete,
                    $"https://api.twitch.tv/helix/eventsub/subscriptions?id={Uri.EscapeDataString(subscriptionId)}"),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync(ct);
                AppLogger.Warn("TwitchEventSub", $"Unsubscribe {subscriptionId} failed: {(int)response.StatusCode} {error}");
            }
        }

        private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
        {
            using HttpResponseMessage response = await SendWithRefreshAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }

        private Task<HttpResponseMessage> PostJsonWithRefreshAsync(string url, object body, CancellationToken ct) =>
            SendWithRefreshAsync(() =>
            {
                HttpRequestMessage request = new(HttpMethod.Post, url)
                {
                    Content = JsonContent.Create(body)
                };
                return request;
            }, ct);

        private async Task<HttpResponseMessage> SendWithRefreshAsync(
            Func<HttpRequestMessage> createRequest,
            CancellationToken ct)
        {
            using HttpRequestMessage request = createRequest();
            HttpResponseMessage response = await _http.SendAsync(request, ct);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;

            response.Dispose();
            await _tokenManager.RefreshAsync(ct);
            using HttpRequestMessage retry = createRequest();
            return await _http.SendAsync(retry, ct);
        }
    }
}