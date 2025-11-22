using Core.Interfaces;
using Core.Enums;

namespace Core.Services
{
    public class TwitchLoginService : LoginServiceBase
    {
        public override async Task ValidateCredentialsAsync(IWebViewHost host)
        {
            System.Diagnostics.Debug.WriteLine("[TwitchLogin] Validating credentials...");
            UpdateStatus(ConnectionStatus.Connecting);

            if (host == null)
            {
                UpdateStatus(ConnectionStatus.NotConnected);
                return;
            }

            try
            {
                await host.EnsureInitializedAsync();
            }
            catch (Exception)
            { }

            await host.NavigateAsync("https://twitch.tv/");

            UpdateStatus(ConnectionStatus.Validating);

            string html = await GetPageHtmlAsync(host);
            bool isLoggedIn = !html.Contains("data-a-target=\"login-button\"", StringComparison.OrdinalIgnoreCase);

            UpdateStatus(isLoggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }
    }
}