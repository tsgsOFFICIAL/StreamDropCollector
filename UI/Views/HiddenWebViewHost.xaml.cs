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
        /// Asynchronously retrieves all cookies associated with the specified URL from the underlying WebView2 control.
        /// </summary>
        /// <remarks>The returned list reflects the state of cookies at the time of the call. Subsequent
        /// changes to cookies will not be reflected in the returned collection. This method does not modify the cookie
        /// store.</remarks>
        /// <param name="url">The URL for which to retrieve cookies. Must be a valid absolute URI; cookies are returned for this specific
        /// address.</param>
        /// <returns>A read-only list of <see cref="CoreWebView2Cookie"/> objects representing the cookies for the specified URL.
        /// Returns an empty list if the WebView2 control is not initialized or if no cookies are found.</returns>
        public async Task<IReadOnlyList<CoreWebView2Cookie>> GetCookiesAsync(string url)
        {
            if (WebView?.CoreWebView2 == null)
                return [];

            List<CoreWebView2Cookie> cookies = await WebView.CoreWebView2.CookieManager.GetCookiesAsync(url);
            return cookies.ToList().AsReadOnly();
        }
        /// <summary>
        /// Asynchronously retrieves the value of the specified cookie for the given URL.
        /// </summary>
        /// <param name="url">The URL for which to retrieve the cookie. Must be a valid absolute URI.</param>
        /// <param name="name">The name of the cookie whose value is to be retrieved. The comparison is case-sensitive.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the value of the specified
        /// cookie if found; otherwise, null.</returns>
        public async Task<string?> GetCookieValueAsync(string url, string name)
        {
            IReadOnlyList<CoreWebView2Cookie> cookies = await GetCookiesAsync(url);
            return cookies.FirstOrDefault(c => c.Name == name)?.Value;
        }
        /// <summary>
        /// Asynchronously captures the value of a specified HTTP request header from the first network request whose URL
        /// contains the given substring.
        /// </summary>
        /// <remarks>This method listens for network requests initiated by the WebView and captures the
        /// specified header from the first request whose URL matches the provided substring. If no such request is
        /// observed within the timeout period, the fallback value is returned. The header name comparison is
        /// case-insensitive. The method automatically enables network tracking via the DevTools protocol if it is not
        /// already enabled.</remarks>
        /// <param name="headerName">The name of the HTTP request header to capture. The comparison is case-insensitive.</param>
        /// <param name="urlContains">A substring to match against the request URL. The header is captured from the first request whose URL
        /// contains this value, using a case-insensitive comparison.</param>
        /// <param name="fallbackValue">The value to return if no matching request is captured within the specified timeout period.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching request before returning the fallback value. The
        /// default is 8000 milliseconds.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the value of the specified
        /// request header if captured; otherwise, the fallback value if the timeout elapses or the operation is
        /// canceled.</returns>
        public async Task<string> CaptureRequestHeaderAsync(string headerName, string urlContains, int timeoutMs = 8000, CancellationToken ct = default)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            // Get the event receiver for the "Network.requestWillBeSent" event
            CoreWebView2DevToolsProtocolEventReceiver eventReceiver = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");

            void Handler(object? s, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                try
                {
                    JsonElement json = JsonDocument.Parse(e.ParameterObjectAsJson).RootElement;
                    JsonElement request = json.GetProperty("request");
                    string url = request.GetProperty("url").GetString() ?? "";
                    JsonElement headersObj = request.GetProperty("headers");

                    if (!url.Contains(urlContains, StringComparison.OrdinalIgnoreCase))
                        return;

                    if (headersObj.TryGetProperty(headerName, out JsonElement valueElem))
                    {
                        string? value = valueElem.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            eventReceiver.DevToolsProtocolEventReceived -= Handler;
                            tcs.TrySetResult(value);
                        }
                    }
                    else
                    {
                        foreach (JsonProperty prop in headersObj.EnumerateObject())
                        {
                            if (string.Equals(prop.Name, headerName, StringComparison.OrdinalIgnoreCase))
                            {
                                string? value = prop.Value.GetString();
                                if (!string.IsNullOrEmpty(value))
                                {
                                    eventReceiver.DevToolsProtocolEventReceived -= Handler;
                                    tcs.TrySetResult(value);
                                }
                            }
                        }
                    }
                }
                catch { /* firehose - ignore */ }
            }

            eventReceiver.DevToolsProtocolEventReceived += Handler;
            await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            Task<string> captureTask = tcs.Task;
            Task<string> timeoutTask = Task.Delay(timeoutMs, ct).ContinueWith(_ => string.Empty, TaskScheduler.Default);

            Task<string> result = await Task.WhenAny(captureTask, timeoutTask).ConfigureAwait(false);
            Dispatcher.Invoke(() =>
            {
                eventReceiver.DevToolsProtocolEventReceived -= Handler; // cleanup just in case
            });

            return await result; // unwrap
        }
        /// <summary>
        /// Asynchronously captures the body of the first outgoing GraphQL POST request whose payload contains the
        /// specified trigger text, or throws if no such request is observed within the timeout period.
        /// </summary>
        /// <remarks>This method listens for outgoing GraphQL POST requests made by the underlying WebView
        /// and inspects their payloads in real time. Only the first matching request body is returned. If the operation
        /// is cancelled via the provided cancellation token, the returned task is cancelled. This method enables
        /// network monitoring scenarios where it is necessary to capture specific GraphQL requests as they
        /// occur.</remarks>
        /// <param name="triggerText">The text to search for within the body of outgoing GraphQL POST requests. The method returns the first
        /// request body that contains this text, using a case-insensitive search.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching GraphQL request before timing out.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation before the timeout elapses. The default value
        /// is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the body of the first GraphQL
        /// POST request whose payload includes the specified trigger text.</returns>
        /// <exception cref="TimeoutException">Thrown if no GraphQL POST request containing the specified trigger text is observed before the timeout
        /// period elapses.</exception>
        public async Task<string> CaptureGqlRequestBodyContainingAsync(string triggerText, int timeoutMs, CancellationToken ct = default)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            HashSet<string> gqlRequestIds = new HashSet<string>();
            int checkedCount = 0;

            // 1. Collect all GQL POST requestIds
            CoreWebView2DevToolsProtocolEventReceiver requestWillBeSent = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            void OnRequestWillBeSent(object? s, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                try
                {
                    JsonElement root = JsonDocument.Parse(e.ParameterObjectAsJson).RootElement;
                    JsonElement request = root.GetProperty("request");
                    string url = request.GetProperty("url").GetString() ?? "";
                    string method = request.GetProperty("method").GetString() ?? "";

                    if (url.Contains("gql.twitch.tv/gql") && method == "POST")
                    {
                        string? requestId = root.GetProperty("requestId").GetString();

                        if (requestId != null)
                            gqlRequestIds.Add(requestId);
                    }
                }
                catch { }
            }
            requestWillBeSent.DevToolsProtocolEventReceived += OnRequestWillBeSent;

            // 2. When loading finishes → pull the real postData
            CoreWebView2DevToolsProtocolEventReceiver loadingFinished = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
            async void OnLoadingFinished(object? s, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                try
                {
                    string? requestId = JsonDocument.Parse(e.ParameterObjectAsJson).RootElement.GetProperty("requestId").GetString();
                    if (requestId == null || !gqlRequestIds.Contains(requestId))
                        return;

                    checkedCount++;

                    string result = await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.getRequestPostData", JsonSerializer.Serialize(new { requestId }));

                    string postData = JsonDocument.Parse(result).RootElement.GetProperty("postData").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(postData))
                        return;

                    if (postData.Contains(triggerText, StringComparison.OrdinalIgnoreCase))
                    {
                        Cleanup();
                        tcs.TrySetResult(postData);
                    }
                }
                catch (Exception)
                { }
            }

            loadingFinished.DevToolsProtocolEventReceived += OnLoadingFinished;

            void Cleanup()
            {
                Dispatcher.Invoke(() =>
                {
                    requestWillBeSent.DevToolsProtocolEventReceived -= OnRequestWillBeSent;
                    loadingFinished.DevToolsProtocolEventReceived -= OnLoadingFinished;
                });
            }

            // Timeout fallback
            Task timeoutTask = Task.Delay(timeoutMs, ct).ContinueWith(_ =>
            {
                // Check if tcs already completed
                if (tcs.Task.IsCompleted)
                    return;

                Cleanup();
                tcs.TrySetResult(string.Empty);
            }, TaskScheduler.Default);

            // Start everything
            await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            requestWillBeSent.DevToolsProtocolEventReceived += OnRequestWillBeSent;
            loadingFinished.DevToolsProtocolEventReceived += OnLoadingFinished;

            Task completed = await Task.WhenAny(tcs.Task, timeoutTask);
            Cleanup();

            string body = await tcs.Task;
            if (string.IsNullOrEmpty(body))
                throw new TimeoutException($"No GQL payload containing \"{triggerText}\" found");

            return body;
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
        public async Task<string> CaptureViewerDropsDashboardResponseAsync(int timeoutMs = 15000, CancellationToken ct = default)
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