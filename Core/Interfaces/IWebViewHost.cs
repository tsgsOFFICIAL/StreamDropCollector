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