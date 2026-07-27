using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Core.Logging;
using Core.Models;

namespace Core.Services.Twitch.Helix
{
    /// <summary>
    /// Signals that Helix authentication could not be completed because of a transient network failure
    /// (DNS resolution, connection reset, timeout) rather than an actual invalid/expired token.
    /// </summary>
    internal sealed class TwitchHelixTransientAuthException(string message, Exception innerException)
        : Exception(message, innerException);

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
                catch (Exception ex) when (IsTransientNetworkFailure(ex))
                {
                    // Network hiccup (DNS, timeout, connection reset) - the saved token is likely still
                    // valid, so keep it instead of forcing the user through a fresh device-code login.
                    AppLogger.Warn(
                        "TwitchHelix",
                        $"Saved Helix login refresh hit a transient network error ({ex.Message}); keeping saved login for retry.");
                    throw new TwitchHelixTransientAuthException(
                        "Transient network failure refreshing saved Helix login.", ex);
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

        /// <summary>
        /// Distinguishes connectivity failures (DNS, timeout, connection reset) - which say nothing about
        /// whether the saved token is still valid - from an actual rejection by Twitch's auth server.
        /// </summary>
        private static bool IsTransientNetworkFailure(Exception ex) => ex switch
        {
            HttpRequestException => true,
            SocketException => true,
            TaskCanceledException => true,
            TimeoutException => true,
            _ => false
        };
    }
}