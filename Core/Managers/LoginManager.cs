using System.Text.Json;
using Core.Interfaces;
using System.Text;
using System.IO;

namespace Core.Managers
{
    public static class LoginManager
    {
        public static event Action<ConnectionStatus>? KickStatusChanged;
        public static event Action<ConnectionStatus>? TwitchStatusChanged;

        private static void UpdateKickStatus(ConnectionStatus status) => KickStatusChanged?.Invoke(status);
        private static void UpdateTwitchStatus(ConnectionStatus status) => TwitchStatusChanged?.Invoke(status);

        public enum ConnectionStatus
        {
            NotConnected,
            Validating,
            Connected
        }

        public static async Task ValidateTwitchTokensAsync(IWebViewHost host)
        {
            UpdateTwitchStatus(ConnectionStatus.Validating);

            if (host == null)
            {
                UpdateTwitchStatus(ConnectionStatus.NotConnected);
                return;
            }

            await host.EnsureInitializedAsync();
            await host.NavigateAsync("https://twitch.tv/");
            await host.WaitForNavigationAsync();
            
            string htmlRaw = await host.ExecuteScriptAsync("document.documentElement.outerHTML;");
            string html = JsonSerializer.Deserialize<string>(htmlRaw) ?? "";
            bool loggedIn = IsTwitchLoggedInFromHtml(html);
            
            UpdateTwitchStatus(loggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }

        public static async Task ValidateKickTokenAsync(IWebViewHost host)
        {
            UpdateKickStatus(ConnectionStatus.Validating);

            if (host == null)
            {
                UpdateKickStatus(ConnectionStatus.NotConnected);
                return;
            }

            await host.EnsureInitializedAsync();

            await host.NavigateAsync("https://kick.com/");
            await host.WaitForNavigationAsync();

            string htmlRaw = await host.ExecuteScriptAsync("document.documentElement.outerHTML;");
            string html = JsonSerializer.Deserialize<string>(htmlRaw) ?? "";

            bool loggedIn = IsKickLoggedInFromHtml(html);

            UpdateKickStatus(loggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }

        private static bool IsTwitchLoggedInFromHtml(string html)
        {
            return !html.Contains("data-a-target=\"login-button\"");
        }

        private static bool IsKickLoggedInFromHtml(string html)
        {
            return !html.Contains("data-testid=\"login\"");
        }
    }
}