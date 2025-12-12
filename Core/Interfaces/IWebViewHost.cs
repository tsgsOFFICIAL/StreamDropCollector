using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Core.Interfaces
{
    public interface IWebViewHost
    {
        /// <summary>
        /// Ensures the underlying WebView2 is initialized and ready.
        /// Must be called from the UI thread (Dispatcher) when appropriate.
        /// </summary>
        Task EnsureInitializedAsync();
        /// <summary>
        /// Adds or updates a cookie for the given domain/path.
        /// </summary>
        Task AddOrUpdateCookieAsync(string name, string value, string domain, string path);
        /// <summary>
        /// Asynchronously retrieves all cookies associated with the specified URL.
        /// </summary>
        /// <param name="url">The URL for which to retrieve cookies. Must be an absolute URI. Cannot be null or empty.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a read-only list of cookies
        /// associated with the specified URL. The list is empty if no cookies are found.</returns>
        Task<IReadOnlyList<CoreWebView2Cookie>> GetCookiesAsync(string url);
        /// <summary>
        /// Asynchronously retrieves the value of the specified cookie for the given URL.
        /// </summary>
        /// <param name="url">The URL for which to retrieve the cookie value. Must be a valid absolute URI.</param>
        /// <param name="name">The name of the cookie to retrieve. Cannot be null or empty.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the cookie value if found;
        /// otherwise, null.</returns>
        Task<string?> GetCookieValueAsync(string url, string name);
        /// <summary>
        /// Asynchronously retrieves the raw dashboard response containing viewer drops data.
        /// </summary>
        /// <param name="timeoutMs">The maximum duration, in milliseconds, to wait for the dashboard response before timing out. Must be greater
        /// than zero.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a string with the raw dashboard
        /// response data.</returns>
        Task<string> CaptureViewerDropsDashboardResponseAsync(int timeoutMs = 10 * 1000, CancellationToken ct = default);
        /// <summary>
        /// Asynchronously captures the progress response from the viewer drops endpoint.
        /// </summary>
        /// <param name="timeoutMs">The maximum duration, in milliseconds, to wait for the response before timing out. Must be greater than
        /// zero.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the response string from the
        /// viewer drops endpoint.</returns>
        Task<string> CaptureViewerDropsProgressResponseAsync(int timeoutMs = 15000, CancellationToken ct = default);
        /// <summary>
        /// Asynchronously captures the progress response from the operation, waiting up to the specified timeout.
        /// </summary>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for the progress response before timing out. Must be greater than
        /// zero.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the progress response as a
        /// string, or an empty string if no response is received within the timeout period.</returns>
        Task<string> CaptureProgressResponseAsync(int timeoutMs = 10 * 1000, CancellationToken ct = default);
        /// <summary>
        /// Asynchronously captures the value of a specified HTTP request header from the first request whose URL
        /// contains the given substring.
        /// </summary>
        /// <remarks>If no matching request is observed within the specified timeout, the returned task
        /// completes without a result. This method is typically used for monitoring or testing scenarios where
        /// capturing specific request headers is required.</remarks>
        /// <param name="headerName">The name of the HTTP request header to capture. Cannot be null or empty.</param>
        /// <param name="urlContains">A substring to match within the request URL. The method captures the header from the first request whose URL
        /// contains this value. Cannot be null or empty.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching request before timing out. Must be greater than
        /// zero. The default is 8000 milliseconds.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the value of the specified
        /// request header, or null if the header is not present in the matching request.</returns>
        Task<string> CaptureRequestHeaderAsync(string headerName, string urlContains, int timeoutMs = 8000, CancellationToken ct = default);
        /// <summary>
        /// Asynchronously captures the body of the first GraphQL request containing the specified trigger text within
        /// the given timeout period.
        /// </summary>
        /// <remarks>If no GraphQL request containing the trigger text is captured before the timeout
        /// expires or the operation is canceled, the returned task will complete with a null result. This method is
        /// thread-safe and can be called concurrently from multiple threads.</remarks>
        /// <param name="triggerText">The text to search for within the GraphQL request body. The method returns the body of the first request
        /// that contains this text.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching GraphQL request before timing out. Must be greater
        /// than zero.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation before the timeout elapses.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the body of the matching GraphQL
        /// request as a string, or null if no matching request is captured within the timeout period.</returns>
        Task<string> CaptureGqlRequestBodyContainingAsync(string triggerText, int timeoutMs, CancellationToken ct = default);
        /// <summary>
        /// Initiates an asynchronous operation to force a refresh of the underlying data or cache.
        /// </summary>
        /// <returns>A task that represents the asynchronous refresh operation.</returns>
        Task ForceRefreshAsync();
        /// <summary>
        /// Navigate the webview to a URL (used to set origin/referrer).
        /// </summary>
        Task NavigateAsync(string url);
        /// <summary>
        /// Executes JS in the webview and returns the resulting JSON/string.
        /// </summary>
        Task<string> ExecuteScriptAsync(string script);
        /// <summary>
        /// Waits asynchronously for a navigation event to complete.
        /// </summary>
        /// <returns>A task that represents the asynchronous wait operation. The task completes when the navigation has finished.</returns>
        Task WaitForNavigationAsync();
        /// <summary>
        /// Asynchronously retrieves the image data from the specified URL as a byte array.
        /// </summary>
        /// <param name="imageUrl">The URL of the image to download. Must be a valid, absolute URI pointing to an accessible image resource.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a byte array with the image
        /// data. Returns an empty array if the image cannot be retrieved.</returns>
        Task<byte[]?> FetchImageBytesAsync(string imageUrl, int timeoutMs = 10000);
    }
}