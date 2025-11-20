using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Core.Interfaces;
using System.Windows;

namespace UI.Views
{

    public partial class HiddenWebViewHost : Window, IWebViewHost
    {
        public WebView2 WebView => WebViewControl;
        private WebView2 WebViewControl => WebViewElement;

        public HiddenWebViewHost()
        {
            InitializeComponent();

            // Fully invisible, no taskbar, no activation
            Width = Height = 0;
            WindowStyle = WindowStyle.None;
            ShowInTaskbar = false;
            Topmost = false;
            AllowsTransparency = true;
            Opacity = 0;
            Visibility = Visibility.Hidden;
            ShowActivated = false;
        }

        /// <summary>
        /// Ensures that the window and its underlying WebView2 environment are initialized and ready for use.
        /// </summary>
        /// <remarks>This method must be called on the UI thread. If the window is not visible, it will be
        /// shown (but remain hidden to the user) to complete initialization. Call this method before performing
        /// operations that require the WebView2 environment to be ready.</remarks>
        /// <returns>A task that represents the asynchronous initialization operation.</returns>
        public async Task EnsureInitializedAsync()
        {
            // Must be called on UI Dispatcher thread. Ensure window is shown (invisible) so HwndHost can be created.
            if (!IsVisible)
                Show();

            // Ensure CoreWebView2 environment is ready
            await WebView.EnsureCoreWebView2Async();
        }
        /// <summary>
        /// Adds a new cookie or updates an existing cookie for the specified domain and path asynchronously.
        /// </summary>
        /// <remarks>This method requires that the underlying WebView2 control has been initialized. If a
        /// cookie with the specified name, domain, and path already exists, its value is updated; otherwise, a new
        /// cookie is created.</remarks>
        /// <param name="name">The name of the cookie to add or update. Cannot be null or empty.</param>
        /// <param name="value">The value to assign to the cookie. Cannot be null.</param>
        /// <param name="domain">The domain to associate with the cookie. Must be a valid domain name.</param>
        /// <param name="path">The path to associate with the cookie. Must begin with a forward slash ('/').</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public Task AddOrUpdateCookieAsync(string name, string value, string domain, string path)
        {
            // CoreWebView2 API must be called after EnsureCoreWebView2Async
            CoreWebView2CookieManager cookieManager = WebView.CoreWebView2.CookieManager;
            CoreWebView2Cookie cookie = cookieManager.CreateCookie(name, value, domain, path);
            cookieManager.AddOrUpdateCookie(cookie);

            return Task.CompletedTask;
        }
        /// <summary>
        /// Navigates the web view to the specified URL, forcing a fresh reload each time.
        /// </summary>
        /// <remarks>This method appends a unique query parameter to the URL to ensure that the web view
        /// performs a full reload, bypassing any cached content. The navigation does not complete until the underlying
        /// web view signals that navigation has finished.</remarks>
        /// <param name="url">The destination URL to navigate to. Cannot be null or empty.</param>
        /// <returns>A task that represents the asynchronous navigation operation.</returns>
        public async Task NavigateAsync(string url)
        {
            // Force fresh navigation every time
            string forcedUrl = $"{url}{(url.Contains('?') ? "&" : "?")}forceReload={DateTimeOffset.Now.ToUnixTimeMilliseconds()}";

            WebView.Source = new Uri(forcedUrl);

            // Still wait for it (in case it's a real nav)
            await WaitForNavigationAsync();
        }
        /// <summary>
        /// Executes the specified JavaScript code asynchronously in the context of the current web page.
        /// </summary>
        /// <remarks>The returned string is always a JSON-encoded value, even for primitive types. For
        /// example, a script returning a string will result in a JSON string literal (e.g., "\"result\""). Callers may
        /// need to parse or deserialize the result to obtain the actual value.</remarks>
        /// <param name="script">The JavaScript code to execute. Cannot be null.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a JSON-encoded string
        /// representing the return value of the executed script.</returns>
        public async Task<string> ExecuteScriptAsync(string script)
        {
            // ExecuteScriptAsync returns a JSON string literal: e.g. "\"{...}\""
            return await WebView.CoreWebView2.ExecuteScriptAsync(script);
        }
        /// <summary>
        /// Asynchronously waits for the next navigation to complete in the associated WebView control.
        /// </summary>
        /// <remarks>If a navigation is already in progress, the task completes when that navigation
        /// finishes. If no navigation is in progress, the task waits for the next navigation event. This method does
        /// not initiate navigation; it only observes completion.</remarks>
        /// <returns>A task that completes when the next navigation has finished. The task completes successfully when navigation
        /// is complete.</returns>
        public Task WaitForNavigationAsync()
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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