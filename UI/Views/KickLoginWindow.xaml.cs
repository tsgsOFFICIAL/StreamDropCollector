using Core.Enums;
using Core.Helpers;
using Core.Logging;
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

            AppLogger.Debug("KickLoginWindow", "OnLoaded - navigating to https://kick.com");
            await Web.EnsureCoreWebView2Async();
            Web.NavigationCompleted += OnNavigationCompleted;
            Web.Source = new Uri("https://kick.com");
        }

        private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            AppLogger.Debug("KickLoginWindow", $"OnNavigationCompleted isSuccess={e.IsSuccess} webErrorStatus={e.WebErrorStatus} url={Web.Source}");

            if (!e.IsSuccess)
                return;

            Web.NavigationCompleted -= OnNavigationCompleted;

            try
            {
                bool clicked = await PlatformLoginDetector.ClickKickLoginWhenEarlyAsync(
                    script => Web.ExecuteScriptAsync(script),
                    cancellationToken: _cts.Token);
                AppLogger.Debug("KickLoginWindow", $"ClickKickLoginWhenEarlyAsync result={clicked}");

                _ = PollAndCloseWhenLoggedInAsync();
            }
            catch (OperationCanceledException)
            {
                AppLogger.Debug("KickLoginWindow", "OnNavigationCompleted canceled.");
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

                AppLogger.Debug("KickLoginWindow", $"PollAndCloseWhenLoggedInAsync loggedIn={loggedIn} cancelled={_cts.IsCancellationRequested}");

                if (loggedIn && !_cts.IsCancellationRequested)
                {
                    AppLogger.Info("KickLoginWindow", "Kick login detected - closing login window.");
                    Dispatcher.Invoke(Close);
                }
            }
            catch (OperationCanceledException)
            {
                AppLogger.Debug("KickLoginWindow", "PollAndCloseWhenLoggedInAsync canceled.");
            }
        }
    }
}