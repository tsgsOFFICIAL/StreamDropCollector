using System.Windows;

namespace Core.Mining;

/// <summary>
/// Marshals WebView operations onto the WPF UI thread.
/// </summary>
public sealed class WebViewUiRunner
{
    /// <summary>
    /// Runs an asynchronous operation on the UI dispatcher and returns its result.
    /// </summary>
    /// <typeparam name="T">Result type produced by <paramref name="action"/>.</typeparam>
    /// <param name="action">Async work that must execute on the WPF UI thread (for example WebView calls).</param>
    /// <returns>The value returned by <paramref name="action"/>.</returns>
    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        return await await Application.Current.Dispatcher.InvokeAsync(action);
    }

    /// <summary>
    /// Runs an asynchronous operation on the UI dispatcher.
    /// </summary>
    /// <param name="action">Async work that must execute on the WPF UI thread (for example WebView calls).</param>
    public async Task RunAsync(Func<Task> action)
    {
        await await Application.Current.Dispatcher.InvokeAsync(action);
    }
}