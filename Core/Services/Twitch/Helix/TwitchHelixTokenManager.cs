using System.Net.Http;
using System.Net.Http.Headers;
using Core.Logging;
using Core.Models;

namespace Core.Services.Twitch.Helix
{
    internal sealed class TwitchHelixTokenManager
    {
        private readonly HttpClient _http;
        private readonly string _clientId;
        private string _refreshToken;

        private TwitchHelixTokenManager(HttpClient http, string clientId, string accessToken, string refreshToken)
        {
            _http = http;
            _clientId = clientId;
            _refreshToken = refreshToken;
            ApplyAccessToken(accessToken);
        }

        public static async Task<TwitchHelixTokenManager> AuthenticateAsync(
            HttpClient http,
            string clientId,
            Func<TwitchDeviceCodePrompt, Task> promptAsync,
            CancellationToken ct = default)
        {
            string? savedRefreshToken = TwitchHelixTokenStore.LoadRefreshToken();

            if (savedRefreshToken is not null)
            {
                AppLogger.Debug("TwitchHelix", "Found saved Helix login, refreshing...");
                try
                {
                    TwitchHelixAuthTokens refreshed = await TwitchHelixAuth.RefreshAccessTokenAsync(
                        http, clientId, savedRefreshToken, ct);
                    TwitchHelixTokenStore.SaveRefreshToken(refreshed.RefreshToken);
                    AppLogger.Debug("TwitchHelix", "Reused saved Helix login.");
                    return new TwitchHelixTokenManager(http, clientId, refreshed.AccessToken, refreshed.RefreshToken);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TwitchHelix", $"Saved Helix login failed ({ex.Message}). Starting device-code flow.");
                    TwitchHelixTokenStore.Clear();
                }
            }

            AppLogger.Debug("TwitchHelix", "Starting Twitch device-code authentication...");
            TwitchHelixAuthTokens tokens = await TwitchHelixAuth.GetUserAccessTokenAsync(
                http, clientId, promptAsync, ct: ct);
            TwitchHelixTokenStore.SaveRefreshToken(tokens.RefreshToken);
            return new TwitchHelixTokenManager(http, clientId, tokens.AccessToken, tokens.RefreshToken);
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            TwitchHelixAuthTokens refreshed = await TwitchHelixAuth.RefreshAccessTokenAsync(
                _http, _clientId, _refreshToken, ct);
            _refreshToken = refreshed.RefreshToken;
            ApplyAccessToken(refreshed.AccessToken);
            TwitchHelixTokenStore.SaveRefreshToken(refreshed.RefreshToken);
        }

        private void ApplyAccessToken(string accessToken) =>
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }
}