using Core.Enums;
using Core.Helpers;
using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for KickLoginWindow.xaml
    /// </summary>
    public partial class KickLoginWindow : Window
    {
        private readonly CancellationTokenSource _cts = new();

        /// <summary>
        /// Initializes the Kick login window and navigates to the Kick site when loaded.
        /// </summary>
        public KickLoginWindow()
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
            Web.Source = new Uri("https://kick.com");
        }

        private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            Web.NavigationCompleted -= OnNavigationCompleted;

            try
            {
                await PlatformLoginDetector.ClickKickLoginWhenEarlyAsync(
                    script => Web.ExecuteScriptAsync(script),
                    cancellationToken: _cts.Token);

                _ = PollAndCloseWhenLoggedInAsync();
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task PollAndCloseWhenLoggedInAsync()
        {
            try
            {
                bool loggedIn = await PlatformLoginDetector.PollUntilLoggedInAsync(
                    script => Web.ExecuteScriptAsync(script),
                    Platform.Kick,
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