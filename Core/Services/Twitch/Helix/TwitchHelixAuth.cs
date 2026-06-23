using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Logging;
using Core.Models;

namespace Core.Services.Twitch.Helix
{
    internal static class TwitchHelixAuth
    {
        public static async Task<TwitchHelixAuthTokens> GetUserAccessTokenAsync(
            HttpClient http,
            string clientId,
            Func<TwitchDeviceCodePrompt, Task> promptAsync,
            string scopes = "",
            CancellationToken ct = default)
        {
            using HttpResponseMessage deviceResp = await http.PostAsync(
                "https://id.twitch.tv/oauth2/device",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scopes"] = scopes
                }),
                ct);

            string deviceBody = await deviceResp.Content.ReadAsStringAsync(ct);
            if (!deviceResp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Failed to start device code flow: {deviceResp.StatusCode} {deviceBody}");

            using JsonDocument deviceJson = JsonDocument.Parse(deviceBody);
            JsonElement root = deviceJson.RootElement;
            string deviceCode = root.GetProperty("device_code").GetString()!;
            string userCode = root.GetProperty("user_code").GetString()!;
            string verificationUri = root.GetProperty("verification_uri").GetString()!;
            string activationUrl = root.TryGetProperty("verification_uri_complete", out JsonElement completeUriElement)
                && !string.IsNullOrWhiteSpace(completeUriElement.GetString())
                    ? completeUriElement.GetString()!
                    : BuildActivationUrl(verificationUri, userCode);
            int interval = root.GetProperty("interval").GetInt32();
            int expiresIn = root.GetProperty("expires_in").GetInt32();

            await promptAsync(new TwitchDeviceCodePrompt(verificationUri, userCode, activationUrl));
            AppLogger.Info("TwitchHelix", "Waiting for Twitch device-code approval...");

            DateTime deadline = DateTime.UtcNow.AddSeconds(expiresIn);

            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(interval), ct);

                using HttpResponseMessage tokenResp = await http.PostAsync(
                    "https://id.twitch.tv/oauth2/token",
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = clientId,
                        ["scopes"] = scopes,
                        ["device_code"] = deviceCode,
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
                    }),
                    ct);

                string tokenBody = await tokenResp.Content.ReadAsStringAsync(ct);

                if (tokenResp.IsSuccessStatusCode)
                {
                    using JsonDocument tokenJson = JsonDocument.Parse(tokenBody);
                    return new TwitchHelixAuthTokens(
                        tokenJson.RootElement.GetProperty("access_token").GetString()!,
                        tokenJson.RootElement.GetProperty("refresh_token").GetString()!);
                }

                if (!tokenBody.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Device code auth failed: {tokenResp.StatusCode} {tokenBody}");
            }

            throw new TimeoutException("Device code expired before the user approved the app.");
        }

        private static string BuildActivationUrl(string verificationUri, string userCode)
        {
            string separator = verificationUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{verificationUri}{separator}user_code={Uri.EscapeDataString(userCode)}";
        }

        public static async Task<TwitchHelixAuthTokens> RefreshAccessTokenAsync(
            HttpClient http,
            string clientId,
            string refreshToken,
            CancellationToken ct = default)
        {
            using HttpResponseMessage resp = await http.PostAsync(
                "https://id.twitch.tv/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken
                }),
                ct);

            string body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Refresh failed: {resp.StatusCode} {body}");

            using JsonDocument json = JsonDocument.Parse(body);
            return new TwitchHelixAuthTokens(
                json.RootElement.GetProperty("access_token").GetString()!,
                json.RootElement.GetProperty("refresh_token").GetString()!);
        }
    }
}