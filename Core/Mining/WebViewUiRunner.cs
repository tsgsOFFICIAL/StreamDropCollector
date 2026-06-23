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
    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        return await await Application.Current.Dispatcher.InvokeAsync(action);
    }

    /// <summary>
    /// Runs an asynchronous operation on the UI dispatcher.
    /// </summary>
    public async Task RunAsync(Func<Task> action)
    {
        await await Application.Current.Dispatcher.InvokeAsync(action);
    }
}