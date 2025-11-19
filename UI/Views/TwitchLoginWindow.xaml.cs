using Microsoft.Web.WebView2.Core;
using System.Windows;
using Core.Managers;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for TwitchLoginWindow.xaml
    /// </summary>
    public partial class TwitchLoginWindow : Window
    {
        public string? AuthToken { get; private set; }
        public string? UniqueId { get; private set; }
        public TwitchLoginWindow()
        {
            InitializeComponent();
            Initialize();
        }

        private async void Initialize()
        {
            await Web.EnsureCoreWebView2Async();

            Web.Source = new Uri("https://twitch.tv");

            // Start cookie watcher after WebView2 is ready
            WatchForLoginCookie();
        }

        private async void WatchForLoginCookie()
        {
            while (true)
            {
                // Try reading the session cookie
                List<CoreWebView2Cookie> cookies = await Web.CoreWebView2.CookieManager.GetCookiesAsync("https://twitch.tv");
                CoreWebView2Cookie? authToken = cookies.SingleOrDefault(c => c.Name == "auth-token");
                CoreWebView2Cookie? uniqueId = cookies.SingleOrDefault(c => c.Name == "unique_id");

                if (authToken != null && uniqueId != null)
                {
                    AuthToken = authToken.Value;
                    UniqueId = uniqueId.Value;

                    LoginManager.SaveTwitchTokens([AuthToken, UniqueId]);

                    DialogResult = true;
                    Close();
                    return;
                }

                // Avoid CPU burn (250–500ms is perfect)
                await Task.Delay(500);
            }
        }
    }
}