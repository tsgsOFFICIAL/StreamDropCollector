using System.Text.Json;
using Core.Interfaces;
using System.Text;
using System.IO;

namespace Core.Managers
{
    public static class LoginManager
    {
        public static event Action<ConnectionStatus>? KickStatusChanged;

        private static void UpdateKickStatus(ConnectionStatus status) => KickStatusChanged?.Invoke(status);

        private readonly static string FilePath = "session.dat";
        public enum ConnectionStatus
        {
            NotConnected,
            Validating,
            Connected
        }

        public static async Task ValidateKickTokenAsync(IWebViewHost host)
        {
            string? token = LoadKickToken();
            UpdateKickStatus(ConnectionStatus.Validating);

            if (host == null)
            {
                UpdateKickStatus(ConnectionStatus.NotConnected);
                return;
            }

            if (string.IsNullOrEmpty(token))
            {
                UpdateKickStatus(ConnectionStatus.NotConnected);
                return;
            }

            await host.EnsureInitializedAsync();
            await host.AddOrUpdateCookieAsync("session_token", token, "kick.com", "/");

            await host.NavigateAsync("https://kick.com/");
            await host.WaitForNavigationAsync();

            string htmlRaw = await host.ExecuteScriptAsync("document.documentElement.outerHTML;");
            string html = JsonSerializer.Deserialize<string>(htmlRaw) ?? "";

            bool loggedIn = IsLoggedInFromHtml(html);

            UpdateKickStatus(loggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }

        private static bool IsLoggedInFromHtml(string html)
        {
            return html.Contains("data-test=\"user-menu\"")
                || html.Contains("alt=\"profile-avatar\"")
                || html.Contains("profile-avatar")
                || html.Contains("avatar")
                || html.Contains("Logout")
                || html.Contains("Profile Settings");
        }

        private static string? LoadKickToken()
        {
            if (!File.Exists(FilePath))
                return null;

            try
            {
                byte[] bytes = File.ReadAllBytes(FilePath);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        public static void SaveKickToken(string token)
        {
            File.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes(token));
        }
    }
}