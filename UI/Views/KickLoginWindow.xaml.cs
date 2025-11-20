using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for KickLoginWindow.xaml
    /// </summary>
    public partial class KickLoginWindow : Window
    {
        public KickLoginWindow()
        {
            InitializeComponent();
            Initialize();
        }

        private async void Initialize()
        {
            await Web.EnsureCoreWebView2Async();

            Web.Source = new Uri("https://kick.com");
            Web.NavigationCompleted += Web_NavigationCompleted;
        }

        private async void Web_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            await Web.ExecuteScriptAsync("document.querySelector(\"[data-testid=\\\"login\\\"\").click();");
        }
    }
}