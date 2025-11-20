using Core.Interfaces;
using Core.Enums;

namespace Core.Services
{
    public class KickLoginService : LoginServiceBase
    {
        public override async Task ValidateCredentialsAsync(IWebViewHost host)
        {
            UpdateStatus(ConnectionStatus.Connecting);

            if (host == null)
            {
                UpdateStatus(ConnectionStatus.NotConnected);
                return;
            }

            await host.EnsureInitializedAsync();
            await host.NavigateAsync("https://kick.com/");

            UpdateStatus(ConnectionStatus.Validating);

            string html = await GetPageHtmlAsync(host);
            bool isLoggedIn = !html.Contains("data-testid=\"login\"", StringComparison.OrdinalIgnoreCase);

            UpdateStatus(isLoggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }
    }
}