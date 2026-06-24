using Core.Helpers;
using Core.Models;
using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Device-code OAuth window for Twitch Helix API access (separate from drops WebView login).
    /// </summary>
    /// <remarks>
    /// Loads Twitch's activation URL in an embedded WebView so the user can approve Helix API access
    /// without copying a URL or confirmation code manually.
    /// </remarks>
    public partial class TwitchHelixAuthWindow : Window
    {
        private readonly TwitchDeviceCodePrompt _prompt;
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Opens the Twitch activation page so the user can approve Helix API access.
        /// </summary>
        /// <param name="prompt">Device-code details from the Helix OAuth flow.</param>
        public TwitchHelixAuthWindow(TwitchDeviceCodePrompt prompt)
        {
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += (_, _) => _cts.Cancel();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await Web.EnsureCoreWebView2Async();
            Web.NavigationCompleted += OnNavigationCompleted;
            Web.Source = new Uri(_prompt.ActivationUrl);
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            Web.NavigationCompleted -= OnNavigationCompleted;
            _ = PollAndCloseWhenAuthCompleteAsync();
        }

        private async Task PollAndCloseWhenAuthCompleteAsync()
        {
            try
            {
                bool complete = await PlatformLoginDetector.PollUntilTwitchHelixAuthCompleteAsync(
                    script => Web.ExecuteScriptAsync(script),
                    cancellationToken: _cts.Token);

                if (complete && !_cts.IsCancellationRequested)
                    Dispatcher.Invoke(Close);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}