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

            //// Fully invisible, no taskbar, no activation
            //Width = Height = 0;
            //WindowStyle = WindowStyle.None;
            //ShowInTaskbar = false;
            //Topmost = false;
            //AllowsTransparency = true;
            //Opacity = 0;
            //Visibility = Visibility.Hidden;
            //ShowActivated = false;
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

                    // MUST RUN ON UI THREAD - THIS IS THE FIX
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
                                Debug.WriteLine($"[Kick Progress] SUCCESS - REAL RESPONSE CAPTURED ({body.Length} chars)");
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
                catch { /* firehose - ignore */ }
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
        /// Asynchronously captures the body of the first outgoing Twitch GraphQL POST request whose payload contains
        /// the specified trigger text.
        /// </summary>
        /// <remarks>This method listens for outgoing Twitch GraphQL POST requests and inspects their
        /// payloads in real time. Only the first request body containing the trigger text is returned. Subsequent
        /// requests are ignored. The method enables the network domain in the WebView2 DevTools protocol and may affect
        /// network event listeners during its execution.</remarks>
        /// <param name="triggerText">The text to search for within the GraphQL request body. The method returns the first request body that
        /// contains this text, using a case-insensitive comparison.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching request before timing out.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation before the timeout elapses.</param>
        /// <returns>A string containing the body of the first matching GraphQL POST request. If no matching request is found
        /// within the timeout period, a TimeoutException is thrown.</returns>
        /// <exception cref="TimeoutException">Thrown if no GraphQL request body containing the specified trigger text is captured before the timeout
        /// period expires.</exception>
        public async Task<string> CaptureGqlRequestBodyContainingAsync(string triggerText, int timeoutMs, CancellationToken ct = default)
        {
            TaskCompletionSource<string> tcs = new TaskCompletionSource<string>();

            HashSet<string> gqlRequestIds = new HashSet<string>();

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

            Task completed = await Task.WhenAny(tcs.Task, timeoutTask);
            Cleanup();

            string body = await tcs.Task;
            if (string.IsNullOrEmpty(body))
                throw new TimeoutException($"No GQL payload containing \"{triggerText}\" found");

            return body;
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