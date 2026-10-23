using Core.Interfaces;
using Core.Logging;
using Core.Enums;
using Core.Helpers;

namespace Core.Services
{
    /// <summary>
    /// Validates Kick login state by inspecting the kick.com homepage for a logged-in session.
    /// </summary>
    public class KickLoginService : LoginServiceBase
    {
        public override async Task ValidateCredentialsAsync(IWebViewHost host)
        {
            AppLogger.Debug("KickLogin", "Validating credentials...");
            ConnectionStatus previous = Status == ConnectionStatus.Connected ? ConnectionStatus.Connected : ConnectionStatus.NotConnected;
            UpdateStatus(ConnectionStatus.Connecting);

            if (host == null)
            {
                AppLogger.Warn("KickLogin", "Validation aborted: host is null.");
                UpdateStatus(ConnectionStatus.NotConnected);
                return;
            }

            try
            {
                await host.EnsureInitializedAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("KickLogin", $"EnsureInitializedAsync failed but continuing: {ex.Message}");
            }

            UpdateStatus(ConnectionStatus.Validating);

            LoginState state = await NavigateAndGetLoginStateAsync(host, Platform.Kick, "https://kick.com/");

            if (state == LoginState.Unknown)
            {
                AppLogger.Warn("KickLogin", $"Could not determine login state; keeping previous status ({previous}).");
                UpdateStatus(previous);
                return;
            }

            bool isLoggedIn = state == LoginState.LoggedIn;
            AppLogger.Info("KickLogin", isLoggedIn ? "Kick connected." : "Kick not logged in.");

            UpdateStatus(isLoggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }
    }
}