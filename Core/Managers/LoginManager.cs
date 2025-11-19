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

        private readonly static string FilePath = "kick_session.dat";
        public enum ConnectionStatus
        {
            NotConnected,
            Validating,
            Connected
        }

        public static async Task ValidateTwitchTokensAsync(IWebViewHost host)
        {
            string[] tokens = LoadTwitchTokens();
            UpdateTwitchStatus(ConnectionStatus.Validating);

            if (host == null)
            {
                UpdateTwitchStatus(ConnectionStatus.NotConnected);
                return;
            }

            if (tokens.Length < 2)
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

            bool loggedIn = IsKickLoggedInFromHtml(html);

            UpdateKickStatus(loggedIn ? ConnectionStatus.Connected : ConnectionStatus.NotConnected);
        }

        private static bool IsTwitchLoggedInFromHtml(string html)
        {
            return !html.Contains("data-a-target=\"login-button\"");
        }

        private static bool IsKickLoggedInFromHtml(string html)
        {
            return html.Contains("data-test=\"user-menu\"")
                || html.Contains("alt=\"profile-avatar\"")
                || html.Contains("profile-avatar")
                || html.Contains("avatar")
                || html.Contains("Logout")
                || html.Contains("Profile Settings");
        }
        /// <summary>
        /// Loads the Kick token from the file specified by the FilePath field, if it exists.
        /// </summary>
        /// <returns>A string containing the Kick token if the file exists and can be read; otherwise, null.</returns>
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
        /// <summary>
        /// Saves the specified Kick token to persistent storage, overwriting any existing token.
        /// </summary>
        /// <param name="token">The Kick token to be saved. Cannot be null.</param>
        public static void SaveKickToken(string token)
        {
            File.WriteAllBytes(FilePath, Encoding.UTF8.GetBytes(token));
        }
        private static string[] LoadTwitchTokens()
        {
            if (!File.Exists("twitch_session.dat"))
                return [];
            try
            {
                byte[] bytes = File.ReadAllBytes("twitch_session.dat");
                string combined = Encoding.UTF8.GetString(bytes);

                return combined.Split(';', StringSplitOptions.RemoveEmptyEntries);
            }
            catch
            {
                return [];
            }
        }
        /// <summary>
        /// Saves the specified Twitch authentication tokens to a local file for later use.
        /// </summary>
        /// <remarks>The tokens are stored in a file named "twitch_session.dat" in the application's
        /// working directory. Existing file contents will be overwritten. Ensure that sensitive token data is handled
        /// securely.</remarks>
        /// <param name="tokens">An array of Twitch authentication tokens to be saved. Each token should be a non-null, non-empty string.</param>
        public static void SaveTwitchTokens(string[] tokens)
        {
            string combined = string.Join(";", tokens);
            File.WriteAllBytes("twitch_session.dat", Encoding.UTF8.GetBytes(combined));
        }
    }
}