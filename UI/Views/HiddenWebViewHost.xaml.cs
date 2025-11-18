using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Core.Interfaces;
using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for HiddenWebViewHost.xaml
    /// </summary>
    // Make sure this namespace and class are accessible to UI code that calls it.
    public partial class HiddenWebViewHost : Window, IWebViewHost
    {
        public WebView2 WebView => WebViewControl;

        // Note: x:Name="WebView" is the control in XAML, but if you named it WebViewControl below,
        // match the property. For this example assume x:Name="WebViewControl"
        private WebView2 WebViewControl => WebViewElement; // if x:Name="WebView" use this

        public HiddenWebViewHost()
        {
            InitializeComponent();

            // Keep window off-screen / hidden — but must be shown to host Hwnd
            Width = 0; Height = 0;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;
            Visibility = Visibility.Hidden;
        }

        public async Task EnsureInitializedAsync()
        {
            // Must be called on UI Dispatcher thread. Ensure window is shown (invisible) so HwndHost can be created.
            if (!IsVisible)
            {
                // Show the window (still hidden to user). This creates the HWND.
                Show();
            }

            // Ensure CoreWebView2 environment is ready
            await WebView.EnsureCoreWebView2Async();
        }

        public Task AddOrUpdateCookieAsync(string name, string value, string domain, string path)
        {
            // CoreWebView2 API must be called after EnsureCoreWebView2Async
            CoreWebView2CookieManager cookieManager = WebView.CoreWebView2.CookieManager;
            CoreWebView2Cookie cookie = cookieManager.CreateCookie(name, value, domain, path);
            cookieManager.AddOrUpdateCookie(cookie);
            return Task.CompletedTask;
        }

        public Task NavigateAsync(string url)
        {
            // Navigate on UI thread
            WebView.Source = new Uri(url);
            return Task.CompletedTask;
        }

        public async Task<string> ExecuteScriptAsync(string script)
        {
            // ExecuteScriptAsync returns a JSON string literal: e.g. "\"{...}\""
            string raw = await WebView.CoreWebView2.ExecuteScriptAsync(script);
            return raw;
        }

        public Task WaitForNavigationAsync()
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            void handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                WebView.CoreWebView2.NavigationCompleted -= handler;
                tcs.TrySetResult(true);
            }

            WebView.CoreWebView2.NavigationCompleted += handler;
            return tcs.Task;
        }

    }
}
