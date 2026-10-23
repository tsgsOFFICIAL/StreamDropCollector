using Core.Interfaces;
using Core.Enums;
using Core.Logging;
using Core.Helpers;

namespace Core.Services
{
    /// <summary>
    /// Validates Twitch login state by inspecting the twitch.tv homepage for a logged-in session.
    /// </summary>
    public class TwitchLoginService : LoginServiceBase
    {
        public override async Task ValidateCredentialsAsync(IWebViewHost host)
        {
            AppLogger.Debug("TwitchLogin", "Validating credentials...");
            ConnectionStatus previous = Status == ConnectionStatus.Connected ? ConnectionStatus.Connected : ConnectionStatus.NotConnected;
            UpdateStatus(ConnectionStatus.Connecting);

            if (host == null)
            {
                AppLogger.Warn("TwitchLogin", "Validation aborted: host is null.");
                UpdateStatus(ConnectionStatus.NotConnected);
                return;
            }

            try
            {
                await host.EnsureInitializedAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TwitchLogin", $"EnsureInitializedAsync failed but continuing: {ex.Message}");
            }

            UpdateStatus(ConnectionStatus.Validating);

            LoginState state = await NavigateAndGetLoginStateAsync(host, Platform.Twitch, "https://twitch.tv/");

            if (state == LoginState.Unknown)
            {
                AppLogger.Warn("TwitchLogin", $"Could not determine login state; keeping previous status ({previous}).");
                UpdateStatus(previous);
                return;
            }

            bool isLoggedIn = state == LoginState.LoggedIn;
            AppLogger.Info("TwitchLogin", isLoggedIn ? "Twitch connected." : "Twitch not logged in.");

            UpdateStatus(isLoggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }
    }
}