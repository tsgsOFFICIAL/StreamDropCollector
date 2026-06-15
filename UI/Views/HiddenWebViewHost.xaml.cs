using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using Core.Logging;
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

            if (!App.IsDebugMode)
            {
                WindowStyle = WindowStyle.None;
                ShowInTaskbar = false;
                ShowActivated = false;
                Top = -20000;
                Left = -20000;
            }
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

            Owner = System.Windows.Application.Current.MainWindow;

            // Ensure CoreWebView2 environment is ready
            await WebView.EnsureCoreWebView2Async();

            WebView.CoreWebView2.IsMuted = true;
            WebView.CoreWebView2.Settings.AreHostObjectsAllowed = true;
            WebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
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

                            AppLogger.Debug("WebViewCapture", $"Body captured for ViewerDropsDashboard response: {body}\n\nlength={body.Length}");

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
                catch (Exception ex)
                {
                    AppLogger.Warn("WebViewCapture", $"ViewerDropsDashboard response handler parse failure: {ex.Message}");
                }
            }

            responseReceived.DevToolsProtocolEventReceived += Handler;

            // Trigger the real request
            await NavigateAsync("https://www.twitch.tv/drops/campaigns?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            Task result = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
            responseReceived.DevToolsProtocolEventReceived -= Handler;

            if (result != tcs.Task)
            {
                AppLogger.Warn("WebViewCapture", $"Timeout capturing ViewerDropsDashboard response after {timeoutMs}ms.");
                throw new TimeoutException("Failed to capture ViewerDropsDashboard response");
            }

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

                            AppLogger.Debug("WebViewCapture", $"Body captured for dropCampaignsInProgress response: {body}\n\nlength={body.Length}");

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
                catch (Exception ex)
                {
                    AppLogger.Warn("WebViewCapture", $"ViewerDropsProgress response handler parse failure: {ex.Message}");
                }
            }

            responseReceived.DevToolsProtocolEventReceived += Handler;

            // Trigger the real request
            await NavigateAsync("https://www.twitch.tv/drops/inventory?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            Task result = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
            responseReceived.DevToolsProtocolEventReceived -= Handler;

            if (result != tcs.Task)
            {
                AppLogger.Warn("WebViewCapture", $"Timeout capturing dropCampaignsInProgress response after {timeoutMs}ms.");
                throw new TimeoutException("Failed to capture dropCampaignsInProgress response");
            }

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

                    if (!url.EndsWith("/api/v1/drops/progress"))
                        return;

                    string? requestId = payload.GetProperty("requestId").GetString();
                    if (requestId == null)
                        return;

                    // Stale requestIds from previous navigations are hex strings (e.g. "A63D247D...")
                    // Fresh ones from the current page are numeric (e.g. "42304.356")
                    // Skip stale ones as their bodies are no longer available in the CDP buffer
                    if (!requestId.Contains('.'))
                    {
                        AppLogger.Debug("WebViewCapture", $"[Kick Progress] Skipping stale requestId={requestId}");
                        return;
                    }

                    // Log how long between event fire and dispatch execution
                    DateTime eventFiredAt = DateTime.UtcNow;
                    AppLogger.Debug("WebViewCapture", $"[Kick Progress] responseReceived fired. requestId={requestId}, url={url}");

                    Dispatcher.InvokeAsync(async () =>
                    {
                        TimeSpan dispatchDelay = DateTime.UtcNow - eventFiredAt;
                        AppLogger.Debug("WebViewCapture", $"[Kick Progress] Dispatcher executing. dispatchDelay={dispatchDelay.TotalMilliseconds}ms, requestId={requestId}");

                        try
                        {
                            string result = await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                                "Network.getResponseBody",
                                JsonSerializer.Serialize(new { requestId }));

                            JsonElement bodyJson = JsonDocument.Parse(result).RootElement;
                            string body = bodyJson.GetProperty("body").GetString() ?? "";

                            AppLogger.Debug("WebViewCapture", $"[Kick Progress] getResponseBody succeeded. bodyLength={body.Length}");

                            // Don't assume a successful response contains reward data.
                            // {"data":[],"message":"Success"} is a valid "no progress" state, not an error.
                            // If we reject it, the capture never completes and gets retried indefinitely.
                            bool hasRewardFields = body.Contains("claimed") || body.Contains("progress_units") || body.Contains("rewards");
                            bool isValidProgressPayload = hasRewardFields;

                            if (!isValidProgressPayload)
                            {
                                try
                                {
                                    JsonElement rootElement = JsonDocument.Parse(body).RootElement;
                                    isValidProgressPayload =
                                        rootElement.ValueKind == JsonValueKind.Object
                                            &&
                                        rootElement.TryGetProperty("data", out JsonElement dataEl)
                                            &&
                                        dataEl.ValueKind == JsonValueKind.Array;
                                }
                                catch
                                {
                                    // Ignore failures on this level, this shouldn't crash / stagnate the app
                                }
                            }

                            if (isValidProgressPayload)
                            {
                                AppLogger.Debug("WebViewCapture", $"[Kick Progress] SUCCESS - RESPONSE CAPTURED ({body.Length} chars)");
                                responseReceived.DevToolsProtocolEventReceived -= Handler;
                                tcs.TrySetResult(body);
                            }
                            else
                            {
                                AppLogger.Warn("WebViewCapture", $"[Kick Progress] Body captured but no expected fields. body={body}");
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("WebViewCapture", $"[Kick Progress] getResponseBody failed: {ex.Message}, requestId={requestId}, dispatchDelay={dispatchDelay.TotalMilliseconds}ms");
                        }
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("WebViewCapture", $"Kick progress response handler parse failure: {ex.Message}");
                }
            }

            responseReceived.DevToolsProtocolEventReceived += Handler;

            // Trigger the real request
            await NavigateAsync("https://kick.com/drops/inventory?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
            responseReceived.DevToolsProtocolEventReceived -= Handler;

            if (completed != tcs.Task)
            {
                AppLogger.Warn("WebViewCapture", $"Timeout capturing Kick progress response after {timeoutMs}ms.");
                throw new TimeoutException("Failed to capture Kick progress response");
            }

            return await tcs.Task;
        }
        /// <summary>
        /// Asynchronously captures the value of a specified HTTP request header from the next network request whose URL
        /// contains the given substring.
        /// </summary>
        /// <remarks>This method listens for network requests using the DevTools protocol and returns the
        /// value of the specified header from the first matching request. If no matching request is observed within the
        /// timeout period, the method returns an empty string. The operation may complete sooner if canceled via the
        /// provided cancellation token.</remarks>
        /// <param name="headerName">The name of the HTTP request header to capture. The search is case-insensitive.</param>
        /// <param name="urlContains">A substring to match against the request URL. Only requests whose URLs contain this substring
        /// (case-insensitive) are considered.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching request before timing out. The default is 8000
        /// milliseconds.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the value of the specified
        /// request header if found; otherwise, an empty string if the timeout elapses or the operation is canceled.</returns>
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
                catch (Exception ex)
                {
                    AppLogger.Warn("WebViewCapture", $"CaptureRequestHeaderAsync handler failed: {ex.Message}");
                }
            }

            eventReceiver.DevToolsProtocolEventReceived += Handler;
            await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            Task<string> captureTask = tcs.Task;
            Task<string> timeoutTask = Task.Delay(timeoutMs, ct).ContinueWith(_ => string.Empty, TaskScheduler.Default);

            Task<string> result = await Task.WhenAny(captureTask, timeoutTask).ConfigureAwait(false);
            Dispatcher.Invoke(() =>
            {
                eventReceiver.DevToolsProtocolEventReceived -= Handler; // cleanup - just in case
            });

            return await result; // unwrap
        }
        /// <summary>
        /// Asynchronously captures the body of a GraphQL network request whose operation name matches the specified
        /// trigger text.
        /// </summary>
        /// <remarks>This method listens for network requests in the associated WebView2 instance and
        /// inspects their bodies for a GraphQL operation with the specified name. Only requests containing a persisted
        /// query hash ("sha256Hash") are considered. If no matching request is captured within the specified timeout,
        /// the returned task is canceled.</remarks>
        /// <param name="triggerText">The operation name to match within the GraphQL request body. The method returns the first request body
        /// containing this operation name.</param>
        /// <param name="timeoutMs">The maximum duration, in milliseconds, to wait for a matching request before timing out.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the request body as a JSON
        /// string if a matching request is found before the timeout or cancellation; otherwise, the task is canceled or
        /// faulted.</returns>
        public async Task<string> CaptureGqlRequestBodyContainingAsync(string triggerText, int timeoutMs, CancellationToken ct = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);

            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConcurrentDictionary<string, byte> candidateRequestIds = new ConcurrentDictionary<string, byte>();
            int cleanedUp = 0;

            CoreWebView2DevToolsProtocolEventReceiver? requestWillBeSentReceiver = null;
            CoreWebView2DevToolsProtocolEventReceiver? loadingFinishedReceiver = null;
            EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? onRequestWillBeSent = null;
            EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>? onLoadingFinished = null;

            bool IsMatchingOperationPayload(string postData)
            {
                if (string.IsNullOrWhiteSpace(postData) || !postData.Contains("sha256Hash", StringComparison.Ordinal))
                    return false;

                using JsonDocument doc = JsonDocument.Parse(postData);
                IEnumerable<JsonElement> operations = doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.EnumerateArray()
                    : Enumerable.Repeat(doc.RootElement, 1);

                return operations.Any(op =>
                    op.TryGetProperty("operationName", out JsonElement name) &&
                    string.Equals(name.GetString(), triggerText, StringComparison.Ordinal));
            }

            static bool IsTargetGqlPostRequest(JsonElement requestElement)
            {
                string method = requestElement.TryGetProperty("method", out JsonElement methodElement)
                    ? (methodElement.GetString() ?? string.Empty)
                    : string.Empty;

                if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                    return false;

                string url = requestElement.TryGetProperty("url", out JsonElement urlElement)
                    ? (urlElement.GetString() ?? string.Empty)
                    : string.Empty;

                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                    return false;

                return string.Equals(uri.Host, "gql.twitch.tv", StringComparison.OrdinalIgnoreCase) &&
                       uri.AbsolutePath.StartsWith("/gql", StringComparison.OrdinalIgnoreCase);
            }

            void Cleanup()
            {
                if (Interlocked.Exchange(ref cleanedUp, 1) != 0)
                    return;

                if (!WebView.Dispatcher.HasShutdownStarted)
                {
                    _ = WebView.Dispatcher.InvokeAsync(() =>
                    {
                        if (requestWillBeSentReceiver != null && onRequestWillBeSent != null)
                            requestWillBeSentReceiver.DevToolsProtocolEventReceived -= onRequestWillBeSent;

                        if (loadingFinishedReceiver != null && onLoadingFinished != null)
                            loadingFinishedReceiver.DevToolsProtocolEventReceived -= onLoadingFinished;
                    });
                }
            }

            async Task TryResolveRequestPostDataAsync(string requestId)
            {
                try
                {
                    string result = await (await WebView.Dispatcher.InvokeAsync(async () =>
                        await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync(
                            "Network.getRequestPostData",
                            JsonSerializer.Serialize(new { requestId })))).ConfigureAwait(false);

                    using JsonDocument resultDoc = JsonDocument.Parse(result);
                    if (!resultDoc.RootElement.TryGetProperty("postData", out JsonElement postDataElement))
                        return;

                    string? postData = postDataElement.GetString();
                    if (string.IsNullOrWhiteSpace(postData))
                        return;

                    if (IsMatchingOperationPayload(postData))
                    {
                        Cleanup();
                        tcs.TrySetResult(postData);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("WebViewCapture", $"CaptureGqlRequestBodyContainingAsync loading handler failed: {ex.Message}");
                }
            }

            // Ensure everything up to subscription happens on UI thread
            await (await WebView.Dispatcher.InvokeAsync(async () =>
            {
                await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

                requestWillBeSentReceiver = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
                loadingFinishedReceiver = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFinished");

                onRequestWillBeSent = (s, e) =>
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                        JsonElement root = doc.RootElement;

                        if (!root.TryGetProperty("requestId", out JsonElement requestIdElement))
                            return;

                        string? requestId = requestIdElement.GetString();
                        if (string.IsNullOrWhiteSpace(requestId))
                            return;

                        if (!root.TryGetProperty("request", out JsonElement requestElement))
                            return;

                        if (!IsTargetGqlPostRequest(requestElement))
                            return;

                        if (requestElement.TryGetProperty("hasPostData", out JsonElement hasPostDataElement) && hasPostDataElement.ValueKind != JsonValueKind.True)
                            return;

                        if (requestElement.TryGetProperty("postData", out JsonElement postDataElement))
                        {
                            string? inlinePostData = postDataElement.GetString();
                            if (!string.IsNullOrWhiteSpace(inlinePostData) && IsMatchingOperationPayload(inlinePostData))
                            {
                                Cleanup();
                                tcs.TrySetResult(inlinePostData);
                                return;
                            }
                        }

                        candidateRequestIds.TryAdd(requestId, 0);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("WebViewCapture", $"CaptureGqlRequestBodyContainingAsync request handler failed: {ex.Message}");
                    }
                };

                onLoadingFinished = (s, e) =>
                {
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                        JsonElement root = doc.RootElement;

                        if (!root.TryGetProperty("requestId", out JsonElement requestIdElement))
                            return;

                        string? requestId = requestIdElement.GetString();
                        if (string.IsNullOrWhiteSpace(requestId))
                            return;

                        if (!candidateRequestIds.TryRemove(requestId, out _))
                            return;

                        _ = TryResolveRequestPostDataAsync(requestId);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("WebViewCapture", $"CaptureGqlRequestBodyContainingAsync loading handler failed: {ex.Message}");
                    }
                };

                requestWillBeSentReceiver.DevToolsProtocolEventReceived += onRequestWillBeSent;
                loadingFinishedReceiver.DevToolsProtocolEventReceived += onLoadingFinished;
            })).ConfigureAwait(false);

            // External cancellation
            using CancellationTokenRegistration externalReg = ct.Register(() =>
            {
                Cleanup();
                tcs.TrySetCanceled(ct);
            });

            // Timeout
            using CancellationTokenSource timeoutCts = new CancellationTokenSource(timeoutMs);
            using CancellationTokenRegistration timeoutReg = timeoutCts.Token.Register(() =>
            {
                Cleanup();
                tcs.TrySetException(new TimeoutException($"No matching GraphQL request captured within {timeoutMs}ms"));
            });

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                Cleanup();
            }
        }
        /// <summary>
        /// Attempts to capture the body of a GraphQL request containing the specified trigger text, retrying the
        /// operation if it times out or fails.
        /// </summary>
        /// <remarks>Each retry forces a fresh navigation to ensure a new request is made. If the
        /// operation is canceled via the cancellation token, retries are not attempted. The delay between retries
        /// increases with each attempt (exponential backoff).</remarks>
        /// <param name="triggerText">The text to search for within the GraphQL request body. The method captures the first request body that
        /// contains this text.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for each attempt before timing out.</param>
        /// <param name="maxRetries">The maximum number of times to retry the operation if it fails or times out. Must be greater than zero. The
        /// default is 3.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A string containing the body of the first GraphQL request that includes the specified trigger text.</returns>
        /// <exception cref="TimeoutException">Thrown if no matching GraphQL request body is captured within the allowed number of retries.</exception>
        public async Task<string> CaptureGqlRequestBodyContainingAsyncWithRetry(string triggerText, int timeoutMs, int maxRetries = 3, string? preCaptureJs = null, CancellationToken ct = default)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Task<string> captureTask = CaptureGqlRequestBodyContainingAsync(triggerText, timeoutMs, ct);

                    await WebView.Dispatcher.InvokeAsync(() => { });

                    // Fresh navigation each retry to force new request
                    await ForceRefreshAsync();

                    // Run custom JS if provided
                    if (!string.IsNullOrEmpty(preCaptureJs))
                    {
                        await ExecuteScriptAsync(preCaptureJs);
                    }

                    return await captureTask;
                }
                catch (TimeoutException ex)
                {
                    lastException = ex;
                    AppLogger.Warn("WebViewCapture", $"[GQL Capture] Attempt {attempt}/{maxRetries} timed out for '{triggerText}'");
                }
                catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
                {
                    AppLogger.Info("WebViewCapture", $"[GQL Capture] Capture canceled for '{triggerText}'. {ex.Message}");
                    throw; // User cancel - don't retry
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    AppLogger.Warn("WebViewCapture", $"[GQL Capture] Attempt {attempt}/{maxRetries} failed: {ex.Message}");
                }

                if (attempt < maxRetries)
                    await Task.Delay(1000 * attempt, ct); // Exponential backoff: 1s, 2s, 3s
            }

            throw new TimeoutException($"Failed to capture GQL payload containing '{triggerText}' after {maxRetries} attempts", lastException);
        }
        /// <summary>
        /// Attempts to claim a reward drop for a specified campaign on Kick.com using the current session.
        /// </summary>
        /// <remarks>This method requires a valid Kick.com session token to be present in the browser's
        /// cookies. If the session token is missing or invalid, the claim will not be attempted and the method returns
        /// <see langword="false"/>. The method communicates with the Kick.com API via a web view and may take up to 15
        /// seconds to complete due to network or API response times.</remarks>
        /// <param name="campaignId">The unique identifier of the campaign for which the drop is being claimed.</param>
        /// <param name="rewardId">The unique identifier of the reward drop to claim within the specified campaign.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the drop was
        /// successfully claimed; otherwise, <see langword="false"/>.</returns>
        public async Task<bool> ClaimKickDropAsync(string campaignId, string rewardId)
        {
            string? encodedToken = await GetCookieValueAsync("https://kick.com", "session_token");
            if (string.IsNullOrEmpty(encodedToken))
            {
                AppLogger.Warn("KickClaim", "session_token cookie not found");
                return false;
            }

            string bearerToken = Uri.UnescapeDataString(encodedToken); // Decode %7C -> |

            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            // One-time handler for the result
            void MessageReceivedHandler(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
            {
                string message = e.TryGetWebMessageAsString() ?? "";
                tcs.TrySetResult(message);
                WebView.CoreWebView2.WebMessageReceived -= MessageReceivedHandler;
            }

            WebView.CoreWebView2.WebMessageReceived += MessageReceivedHandler;

            try
            {
                string script = $@"
                    (async () => {{
                        try {{
                            const response = await fetch('https://web.kick.com/api/v1/drops/claim', {{
                                method: 'POST',
                                credentials: 'include',
                                headers: {{
                                    'Content-Type': 'application/json',
                                    'Accept': 'application/json',
                                    'Authorization': 'Bearer {bearerToken}'
                                }},
                                body: JSON.stringify({{
                                    campaign_id: '{campaignId}',
                                    reward_id: '{rewardId}'
                                }})
                            }});

                            const data = await response.json();

                            const result = {{
                                success: response.ok && (data.message === 'Success' || data.success === true),
                                data: data,
                                error: response.ok ? null : (data.message || data.error || response.statusText)
                            }};

                            window.chrome.webview.postMessage(JSON.stringify(result));
                        }} catch (err) {{
                            window.chrome.webview.postMessage(JSON.stringify({{
                                success: false,
                                error: err.message || 'Unknown JS error'
                            }}));
                        }}
                    }})();
                ";

                await WebView.CoreWebView2.ExecuteScriptAsync(script);

                // Wait for the postMessage (with timeout)
                string rawResult = await Task.WhenAny(tcs.Task, Task.Delay(15000)) == tcs.Task
                    ? await tcs.Task
                    : "{}";

                if (rawResult == "{}" || string.IsNullOrWhiteSpace(rawResult))
                {
                    AppLogger.Warn("KickClaim", "Claim timed out or no response");
                    return false;
                }

                using JsonDocument doc = JsonDocument.Parse(rawResult);
                bool success = doc.RootElement.GetProperty("success").GetBoolean();

                if (success)
                {
                    AppLogger.Debug("KickClaim", $"Successfully claimed Kick drop: {rewardId} (Campaign: {campaignId})");
                }
                else
                {
                    string? error =
                        doc.RootElement.TryGetProperty("error", out JsonElement errElem) &&
                        !string.IsNullOrWhiteSpace(errElem.GetString())
                            ? errElem.GetString()
                            : (
                                doc.RootElement.TryGetProperty("data", out JsonElement data1) &&
                                data1.TryGetProperty("data", out JsonElement data2) &&
                                data2.TryGetProperty("details", out JsonElement details)
                                    ? details.GetString()
                                    : "Unknown error"
                              );
                    AppLogger.Warn("KickClaim", $"Kick claim failed: {error}");
                }

                return success;
            }
            catch (Exception ex)
            {
                AppLogger.Error("KickClaim", "Exception in ClaimKickDropAsync.", ex);
                return false;
            }
            finally
            {
                // Clean up handler in case of exception/timeout
                WebView.CoreWebView2.WebMessageReceived -= MessageReceivedHandler;
            }
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
            await WaitForDomReadyAsync();
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
        /// <summary>
        /// Asynchronously waits until the web view's DOM is fully loaded and ready for interaction.
        /// </summary>
        /// <remarks>Use this method to ensure that subsequent operations on the web view are performed
        /// only after the DOM has finished loading. This is particularly useful when interacting with dynamic content
        /// or scripts that depend on the DOM being fully constructed.</remarks>
        /// <returns>A task that completes when the DOM is in a ready state.</returns>
        public async Task WaitForDomReadyAsync()
        {
            while (true)
            {
                string result = await WebView.ExecuteScriptAsync(
                    "document.readyState"
                );

                // result comes back as a quoted string: "\"complete\""
                if (result.Contains("complete", StringComparison.OrdinalIgnoreCase))
                    break;

                await Task.Delay(50);
            }
        }
        /// <summary>
        /// Asynchronously waits until the page is network-idle for a continuous period.
        /// </summary>
        /// <remarks>This method is site-agnostic and does not depend on specific selectors. It tracks
        /// network requests via DevTools protocol and returns when no tracked requests are in flight for the
        /// configured idle window. Long-lived noisy request types such as WebSocket and EventSource are ignored.
        /// If idle is not reached before the timeout, the method returns <see langword="false"/>.</remarks>
        /// <param name="timeoutMs">Maximum time to wait before giving up. Must be greater than zero. Default is 15000.</param>
        /// <param name="idleWindowMs">Continuous idle period required to consider the page settled. Must be greater than zero. Default is 900.</param>
        /// <param name="ct">Cancellation token used to cancel the wait.</param>
        /// <returns><see langword="true"/> if idle was reached; otherwise <see langword="false"/> on timeout.</returns>
        public async Task<bool> WaitForNetworkIdleAsync(int timeoutMs = 15000, int idleWindowMs = 900, CancellationToken ct = default)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(timeoutMs);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(idleWindowMs);

            await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");

            ConcurrentDictionary<string, byte> inFlightRequests = new ConcurrentDictionary<string, byte>();
            long lastActivityTick = Environment.TickCount64;

            CoreWebView2DevToolsProtocolEventReceiver requestWillBeSent = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent");
            CoreWebView2DevToolsProtocolEventReceiver loadingFinished = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFinished");
            CoreWebView2DevToolsProtocolEventReceiver loadingFailed = WebView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.loadingFailed");

            static bool IsIgnorableType(string? requestType)
                => string.Equals(requestType, "WebSocket", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestType, "EventSource", StringComparison.OrdinalIgnoreCase)
                || string.Equals(requestType, "Manifest", StringComparison.OrdinalIgnoreCase);

            static bool TryGetRequestMetadata(string json, out string? requestId, out string? requestType, out string? requestUrl)
            {
                requestId = null;
                requestType = null;
                requestUrl = null;

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;

                    if (root.TryGetProperty("requestId", out JsonElement requestIdElement))
                        requestId = requestIdElement.GetString();

                    if (root.TryGetProperty("type", out JsonElement typeElement))
                        requestType = typeElement.GetString();

                    if (root.TryGetProperty("request", out JsonElement requestElement) &&
                        requestElement.TryGetProperty("url", out JsonElement urlElement))
                    {
                        requestUrl = urlElement.GetString();
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
            }

            static bool TryGetRequestId(string json, out string? requestId)
            {
                requestId = null;

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;

                    if (!root.TryGetProperty("requestId", out JsonElement requestIdElement))
                        return false;

                    requestId = requestIdElement.GetString();
                    return !string.IsNullOrWhiteSpace(requestId);
                }
                catch
                {
                    return false;
                }
            }

            void OnRequestWillBeSent(object? _, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                if (!TryGetRequestMetadata(e.ParameterObjectAsJson, out string? requestId, out string? requestType, out string? requestUrl))
                    return;

                if (string.IsNullOrWhiteSpace(requestId))
                    return;

                if (IsIgnorableType(requestType))
                    return;

                if (!string.IsNullOrWhiteSpace(requestUrl) &&
                    (requestUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                     requestUrl.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                if (inFlightRequests.TryAdd(requestId, 0))
                    Interlocked.Exchange(ref lastActivityTick, Environment.TickCount64);
            }

            void OnRequestDone(object? _, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
            {
                if (!TryGetRequestId(e.ParameterObjectAsJson, out string? requestId) || string.IsNullOrWhiteSpace(requestId))
                    return;

                if (inFlightRequests.TryRemove(requestId, out byte _))
                    Interlocked.Exchange(ref lastActivityTick, Environment.TickCount64);
            }

            requestWillBeSent.DevToolsProtocolEventReceived += OnRequestWillBeSent;
            loadingFinished.DevToolsProtocolEventReceived += OnRequestDone;
            loadingFailed.DevToolsProtocolEventReceived += OnRequestDone;

            Stopwatch timeout = Stopwatch.StartNew();

            try
            {
                while (timeout.ElapsedMilliseconds < timeoutMs)
                {
                    ct.ThrowIfCancellationRequested();

                    if (inFlightRequests.IsEmpty)
                    {
                        long idleForMs = Environment.TickCount64 - Interlocked.Read(ref lastActivityTick);
                        if (idleForMs >= idleWindowMs)
                            return true;
                    }

                    await Task.Delay(100, ct);
                }

                return false;
            }
            finally
            {
                requestWillBeSent.DevToolsProtocolEventReceived -= OnRequestWillBeSent;
                loadingFinished.DevToolsProtocolEventReceived -= OnRequestDone;
                loadingFailed.DevToolsProtocolEventReceived -= OnRequestDone;
            }
        }
        /// <summary>
        /// Asynchronously retrieves the raw byte data of the first image found at the specified URL.
        /// </summary>
        /// <remarks>This method searches for the first <img> element on the page and extracts its pixel
        /// data as a PNG image. If no image is found or the image cannot be processed, the method returns <c>null</c>.
        /// The operation may fail if the page does not load within the specified timeout or if the image is not
        /// accessible.</remarks>
        /// <param name="imageUrl">The URL of the web page containing the image to fetch. Must be a valid, accessible URL.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for the operation to complete. The default is 15,000
        /// milliseconds.</param>
        /// <returns>A byte array containing the PNG-encoded image data if an image is found and successfully processed;
        /// otherwise, <c>null</c>.</returns>
        public async Task<byte[]?> FetchImageBytesAsync(string imageUrl, int timeoutMs = 15000)
        {
            await EnsureInitializedAsync();

            await NavigateAsync(imageUrl);

            // Grab the Base64 data from the img element
            string script = @"
            const img = document.querySelector('img');
            if (!img) 
                ''
            else (function() {
                const canvas = document.createElement('canvas');
                canvas.width = img.naturalWidth;
                canvas.height = img.naturalHeight;
                const ctx = canvas.getContext('2d');
                ctx.drawImage(img, 0, 0);
                return canvas.toDataURL('image/png').split(',')[1];
            })()";

            string result = await ExecuteScriptAsync(script);

            if (string.IsNullOrWhiteSpace(result) || result == "null" || result == "\"\"")
                return null;

            try
            {
                string base64 = result.Trim('"');
                return Convert.FromBase64String(base64);
            }
            catch
            {
                return null;
            }
        }
        /// <summary>
        /// Releases all resources used by the current instance and performs necessary cleanup.
        /// </summary>
        /// <remarks>Call this method when you are finished using the object to free unmanaged resources
        /// and perform additional shutdown operations. After calling <see cref="Dispose"/>, the object should not be
        /// used.</remarks>
        public void Dispose()
        {
            WebView.Dispose();
            GC.SuppressFinalize(this);
            Close();
        }
    }
}