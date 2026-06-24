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

            await host.NavigateAsync("https://kick.com/");

            UpdateStatus(ConnectionStatus.Validating);

            string html = await GetPageHtmlAsync(host);
            bool isLoggedIn = PlatformLoginDetector.IsLoggedInFromHtml(Platform.Kick, html);

            AppLogger.Info("KickLogin", isLoggedIn ? "Kick connected." : "Kick not logged in.");

            UpdateStatus(isLoggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }
    }
}