using Microsoft.Web.WebView2.Core;

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
        /// Asynchronously captures the value of a specified HTTP request header from network traffic that matches a
        /// given URL substring.
        /// </summary>
        /// <remarks>If multiple requests match the specified URL substring, the header is captured from
        /// the first matching request. This method is typically used in scenarios where network traffic is being
        /// monitored or intercepted.</remarks>
        /// <param name="headerName">The name of the HTTP request header to capture. Cannot be null or empty.</param>
        /// <param name="urlContains">A substring that must be present in the request URL for the header to be captured. Matching is
        /// case-sensitive.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching request before returning the fallback value. Must
        /// be greater than zero.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the value of the specified
        /// header if found; otherwise, the fallback value.</returns>
        Task<string> CaptureRequestHeaderAsync(string headerName, string urlContains, int timeoutMs = 8000, CancellationToken ct = default);
        /// <summary>
        /// Asynchronously retrieves the body content of the first <script> element that contains the specified text.
        /// </summary>
        /// <param name="containsText">The text to search for within the contents of <script> elements. The method returns the body of the first
        /// script containing this text.</param>
        /// <param name="timeoutMs">The maximum time, in milliseconds, to wait for a matching <script> element to be found. Defaults to 20,000
        /// milliseconds.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the body content of the first
        /// matching <script> element, or null if no such element is found within the timeout period.</returns>
        Task<string> CaptureGqlRequestBodyContainingAsync(string triggerText, int timeoutMs, CancellationToken ct = default);
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
    }
}