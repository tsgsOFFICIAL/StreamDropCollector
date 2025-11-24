using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;
using System.Text.Json;
using Core.Interfaces;
using System.Windows;

namespace UI.Views
{
    public partial class HiddenWebViewHost : Window, IWebViewHost, IDisposable
    {
        public WebView2 WebView => WebViewControl;
        private WebView2 WebViewControl => WebViewElement;
        /// <summary>
        /// Initializes a new instance of the HiddenWebViewHost class with a fully hidden and non-interactive window.
        /// </summary>
        /// <remarks>This constructor configures the window to be completely invisible and non-activating,
        /// making it suitable for scenarios where a background WebView is required without any user interface or
        /// taskbar presence.</remarks>
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
        /// Captures the response body of the Twitch Viewer Drops Dashboard network request from the embedded web view
        /// asynchronously.
        /// </summary>
        /// <remarks>This method listens for the Twitch Viewer Drops Dashboard network response and
        /// returns its body as a JSON string. If the response is not received within the specified timeout, a
        /// TimeoutException is thrown. The operation can also be cancelled using the provided cancellation
        /// token.</remarks>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for the Viewer Drops Dashboard response before timing out. The
        /// default is 15,000 milliseconds.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A JSON string containing the response body of the Viewer Drops Dashboard network request.</returns>
        /// <exception cref="TimeoutException">Thrown if the Viewer Drops Dashboard response is not captured within the specified timeout period.</exception>
        public async Task<string> CaptureViewerDropsDashboardResponseAsync(int timeoutMs = 10000, CancellationToken ct = default)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            CoreWebView2DevToolsProtocolEventReceiver responseReceived = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived");

            async void Handler(object? s, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                try
                {
                    JsonElement payload = JsonDocument.Parse(e.ParameterObjectAsJson).RootElement;
                    string url = payload.GetProperty("response").GetProperty("url").GetString() ?? "";
                    string? requestId = payload.GetProperty("requestId").GetString();

                    if (url == "https://gql.twitch.tv/gql" && requestId != null)
                    {
                        JsonElement headers = payload.GetProperty("response").GetProperty("headers");
                        if (headers.TryGetProperty("content-type", out JsonElement ctHeader) &&
                            ctHeader.GetString()?.Contains("application/json") == true)
                        {
                            // Get the response body
                            string bodyResult = await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                                "Network.getResponseBody",
                                JsonSerializer.Serialize(new { requestId }));

                            JsonElement bodyJson = JsonDocument.Parse(bodyResult).RootElement;
                            string body = bodyJson.GetProperty("body").GetString() ?? "";

                            Debug.WriteLine($"Body captured for ViewerDropsDashboard response: {body}\n\nlength={body.Length}");

                            if (body.Contains("ViewerDropsDashboard") || body.Contains("rewardCampaignsAvailableToUser"))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    responseReceived.DevToolsProtocolEventReceived -= Handler;
                                });

                                tcs.TrySetResult(body);
                                return;
                            }
                        }
                    }
                }
                catch { }
            }

            responseReceived.DevToolsProtocolEventReceived += Handler;

            // Trigger the real request
            await NavigateAsync("https://www.twitch.tv/drops/campaigns?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            Task result = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
            responseReceived.DevToolsProtocolEventReceived -= Handler;

            if (result != tcs.Task)
                throw new TimeoutException("Failed to capture ViewerDropsDashboard response");

            return await tcs.Task;
        }
        /// <summary>
        /// Asynchronously captures the response body of the Twitch drops progress request from the embedded web viewer.
        /// </summary>
        /// <remarks>This method listens for the Twitch drops progress response by monitoring network
        /// events in the embedded web viewer. It is intended for scenarios where the drops progress data must be
        /// programmatically retrieved from Twitch's inventory page. The returned JSON string can be parsed to extract
        /// campaign progress information. This method should be called from a context that supports asynchronous
        /// operations.</remarks>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for the drops progress response before timing out. The default is
        /// 10,000 milliseconds.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A JSON string containing the body of the Twitch drops progress response if successfully captured.</returns>
        /// <exception cref="TimeoutException">Thrown if the drops progress response is not received within the specified timeout period.</exception>
        public async Task<string> CaptureViewerDropsProgressResponseAsync(int timeoutMs = 10000, CancellationToken ct = default)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            CoreWebView2DevToolsProtocolEventReceiver responseReceived = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived");

            async void Handler(object? s, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                try
                {
                    JsonElement payload = JsonDocument.Parse(e.ParameterObjectAsJson).RootElement;
                    string url = payload.GetProperty("response").GetProperty("url").GetString() ?? "";
                    string? requestId = payload.GetProperty("requestId").GetString();

                    if (url == "https://gql.twitch.tv/gql" && requestId != null)
                    {
                        JsonElement headers = payload.GetProperty("response").GetProperty("headers");
                        if (headers.TryGetProperty("content-type", out JsonElement ctHeader) &&
                            ctHeader.GetString()?.Contains("application/json") == true)
                        {
                            // Get the response body
                            string bodyResult = await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                                "Network.getResponseBody",
                                JsonSerializer.Serialize(new { requestId }));

                            JsonElement bodyJson = JsonDocument.Parse(bodyResult).RootElement;
                            string body = bodyJson.GetProperty("body").GetString() ?? "";

                            Debug.WriteLine($"Body captured for dropCampaignsInProgress response: {body}\n\nlength={body.Length}");

                            if (body.Contains("dropCampaignsInProgress") || body.Contains("dropCampaignsInProgress"))
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    responseReceived.DevToolsProtocolEventReceived -= Handler;
                                });

                                tcs.TrySetResult(body);
                                return;
                            }
                        }
                    }
                }
                catch { }
            }

            responseReceived.DevToolsProtocolEventReceived += Handler;

            // Trigger the real request
            await NavigateAsync("https://www.twitch.tv/drops/inventory?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            Task result = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
            responseReceived.DevToolsProtocolEventReceived -= Handler;

            if (result != tcs.Task)
                throw new TimeoutException("Failed to capture dropCampaignsInProgress response");

            return await tcs.Task;
        }
        /// <summary>
        /// Asynchronously captures the response body of a Kick progress API request from the embedded web view.
        /// </summary>
        /// <remarks>This method listens for a specific network response from the web view and returns its
        /// body when detected. It is intended for use with the Kick drops progress API and will only complete when a
        /// relevant response is received or the timeout elapses. The returned string may contain progress, claimed
        /// units, or rewards information as provided by the API.</remarks>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for the progress response before timing out. The default is
        /// 10,000 milliseconds.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the response body as a string if
        /// the progress response is successfully captured.</returns>
        /// <exception cref="TimeoutException">Thrown if the progress response is not captured within the specified timeout period.</exception>
        public async Task<string> CaptureProgressResponseAsync(int timeoutMs = 10000, CancellationToken ct = default)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            CoreWebView2DevToolsProtocolEventReceiver responseReceived = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.responseReceived");

            void Handler(object? s, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                try
                {
                    JsonElement payload = JsonDocument.Parse(e.ParameterObjectAsJson).RootElement;
                    string url = payload.GetProperty("response").GetProperty("url").GetString() ?? "";

                    if (!url.Contains("/api/v1/drops/progress"))
                        return;

                    string? requestId = payload.GetProperty("requestId").GetString();
                    if (requestId == null) return;

                    // MUST RUN ON UI THREAD — THIS IS THE FIX
                    Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            string result = await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                                "Network.getResponseBody",
                                JsonSerializer.Serialize(new { requestId }));

                            JsonElement bodyJson = JsonDocument.Parse(result).RootElement;
                            string body = bodyJson.GetProperty("body").GetString() ?? "";

                            if (body.Contains("claimed") || body.Contains("progress_units") || body.Contains("rewards"))
                            {
                                Debug.WriteLine($"[Kick Progress] SUCCESS — REAL RESPONSE CAPTURED ({body.Length} chars)");
                                responseReceived.DevToolsProtocolEventReceived -= Handler;
                                tcs.TrySetResult(body);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Kick Progress] getResponseBody failed: {ex.Message}");
                        }
                    });
                }
                catch { }
            }

            responseReceived.DevToolsProtocolEventReceived += Handler;

            // Trigger the real request
            await NavigateAsync("https://kick.com/drops/inventory?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
            responseReceived.DevToolsProtocolEventReceived -= Handler;

            if (completed != tcs.Task)
                throw new TimeoutException("Failed to capture Kick progress response");

            return await tcs.Task;
        }
        /// <summary>
        /// Forces a refresh of the current web content by reloading the source URL asynchronously.
        /// </summary>
        /// <remarks>If the source URL is not set, the method navigates to "about:blank". This method does
        /// not block the calling thread.</remarks>
        /// <returns>A task that represents the asynchronous refresh operation.</returns>
        public async Task ForceRefreshAsync()
        {
            await NavigateAsync(WebView.Source.ToString() ?? "about:blank");
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

        public void Dispose()
        {
            WebView.Dispose();
            GC.SuppressFinalize(this);
            Close();
        }
    }
}