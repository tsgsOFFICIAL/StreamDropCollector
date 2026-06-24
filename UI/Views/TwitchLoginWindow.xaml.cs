using Core.Helpers;
using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for TwitchLoginWindow.xaml
    /// </summary>
    public partial class TwitchLoginWindow : Window
    {
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Initializes the Twitch login window and navigates to the Twitch login page when loaded.
        /// </summary>
        public TwitchLoginWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += (_, _) => _cts.Cancel();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await Web.EnsureCoreWebView2Async();
            Web.NavigationCompleted += OnNavigationCompleted;
            Web.Source = new Uri("https://twitch.tv/login");
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            Web.NavigationCompleted -= OnNavigationCompleted;
            _ = PollAndCloseWhenLoggedInAsync();
        }

        private async Task PollAndCloseWhenLoggedInAsync()
        {
            try
            {
                bool loggedIn = await PlatformLoginDetector.PollUntilTwitchLoginCompleteAsync(
                    script => Web.ExecuteScriptAsync(script),
                    cancellationToken: _cts.Token);

                if (loggedIn && !_cts.IsCancellationRequested)
                    Dispatcher.Invoke(Close);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}