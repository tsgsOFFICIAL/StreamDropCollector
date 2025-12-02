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