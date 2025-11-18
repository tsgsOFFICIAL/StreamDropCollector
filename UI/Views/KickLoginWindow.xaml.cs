using Microsoft.Web.WebView2.Core;
using System.Windows;
using Core.Managers;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for KickLoginWindow.xaml
    /// </summary>
    public partial class KickLoginWindow : Window
    {
        public string? SessionToken { get; private set; }
        public KickLoginWindow()
        {
            InitializeComponent();
            Initialize();
        }

        private async void Initialize()
        {
            await Web.EnsureCoreWebView2Async();

            Web.Source = new Uri("https://kick.com");

            // Start cookie watcher after WebView2 is ready
            WatchForLoginCookie();
        }

        private async void WatchForLoginCookie()
        {
            while (true)
            {
                // Try reading the session cookie
                List<CoreWebView2Cookie> cookies = await Web.CoreWebView2.CookieManager.GetCookiesAsync("https://kick.com");
                CoreWebView2Cookie? session = cookies.SingleOrDefault(c => c.Name == "session_token");

                if (session != null)
                {
                    SessionToken = session.Value;

                    LoginManager.SaveKickToken(SessionToken);

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