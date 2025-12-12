using System.Text.Json.Nodes;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.Http;
using Core.Interfaces;
using System.Net;

namespace Core.Services
{
    public sealed class TwitchGqlService : IGqlService
    {
        private readonly IWebViewHost _host;
        private readonly HttpClient _httpClient;

        private string? _clientId;
        private string? _integrityToken;
        private string? _deviceId;
        private string? _accessToken;

        public TwitchGqlService(IWebViewHost host, HttpClient? httpClient = null)
        {
            _host = host;
            _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36"
            );
        }

        private async Task RefreshHeadersAsync(CancellationToken ct = default)
        {
            await _host.NavigateAsync($"https://twitch.tv/drops/campaigns?t={DateTimeOffset.Now.ToUnixTimeMilliseconds()}");

            Task<string> clientIdTask = _host.CaptureRequestHeaderAsync("Client-ID", "gql.twitch.tv", 8000, ct);
            Task<string> integrityTask = _host.CaptureRequestHeaderAsync("Client-Integrity", "gql.twitch.tv", 8000, ct);
            Task<string> deviceIdTask = _host.CaptureRequestHeaderAsync("X-Device-Id", "gql.twitch.tv", 8000, ct);
            Task<string> authTokenTask = GetAuthTokenFromCookieAsync(ct);

            string[] results = await Task.WhenAll(clientIdTask, integrityTask, deviceIdTask, authTokenTask);

            _clientId = results[0];
            _integrityToken = results[1];
            _deviceId = results[2];
            _accessToken = results[3];

            if (string.IsNullOrEmpty(_integrityToken))
                throw new InvalidOperationException("Failed to capture Client-Integrity token");
        }

        private async Task<string> GetAuthTokenFromCookieAsync(CancellationToken ct = default)
        {
            string? token = await _host.GetCookieValueAsync("https://twitch.tv", "auth-token");
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("auth-token cookie not found");

            return "OAuth " + token.ToLower();
        }

        private async Task<string> GetPersistedQueryHashAsync(string operationName, CancellationToken ct = default)
        {
            await _host.NavigateAsync($"https://www.twitch.tv/drops/campaigns?t={DateTimeOffset.Now.ToUnixTimeMilliseconds()}");
            string payload = await _host.CaptureGqlRequestBodyContainingAsync(operationName, 8000, ct);

            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            IEnumerable<JsonElement> operations = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : Enumerable.Repeat(root, 1);

            foreach (JsonElement operation in operations)
            {
                if (!operation.TryGetProperty("operationName", out JsonElement opNameElement) ||
                    opNameElement.GetString() != operationName)
                    continue;

                if (operation.TryGetProperty("extensions", out JsonElement extensions) &&
                    extensions.TryGetProperty("persistedQuery", out JsonElement persistedQuery) &&
                    persistedQuery.TryGetProperty("sha256Hash", out JsonElement hashElement))
                {
                    return hashElement.GetString()!;
                }
            }

            throw new InvalidOperationException($"Persisted query hash not found for operation: {operationName}");
        }

        public async Task<JsonObject> QueryAsync(string operationName, object? variables = null, CancellationToken ct = default)
        {
            if (_clientId == null || _integrityToken == null)
                await RefreshHeadersAsync(ct);

            string hash = await GetPersistedQueryHashAsync(operationName, ct);

            JsonObject payload = new JsonObject
            {
                ["operationName"] = operationName,
                ["variables"] = JsonSerializer.SerializeToNode(variables ?? new { })
            };

            payload["extensions"] = new JsonObject
            {
                ["persistedQuery"] = new JsonObject
                {
                    ["version"] = 1,
                    ["sha256Hash"] = hash
                }
            };

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.TryAddWithoutValidation("Client-ID", _clientId);
            request.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
            request.Headers.TryAddWithoutValidation("Authorization", _accessToken);
            if (!string.IsNullOrEmpty(_deviceId))
                request.Headers.TryAddWithoutValidation("X-Device-Id", _deviceId);

            HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
            string jsonText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode || jsonText.Contains("\"errors\""))
            {
                await RefreshHeadersAsync(ct);
                request.Headers.Remove("Client-Integrity");
                request.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
                response = await _httpClient.SendAsync(request, ct);
                jsonText = await response.Content.ReadAsStringAsync(ct);
            }

            response.EnsureSuccessStatusCode();
            JsonNode? node = JsonNode.Parse(jsonText);
            return node!.AsObject();
        }

        public void Dispose() => _httpClient.Dispose();
    }
}
