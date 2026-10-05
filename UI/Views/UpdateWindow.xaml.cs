using System.ComponentModel;
using System.Windows;
using Core.Managers;
using Core.Services;

namespace UI.Views
{
    /// <summary>
    /// Shows the progress of an application update: a progress bar and a console-style log of each step.
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private const int MaxLogChars = 60_000;

        private CancellationTokenSource? _cts;
        private bool _running;

        /// <summary>Initializes the update window; the update starts as soon as it is shown.</summary>
        public UpdateWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await RunAsync();
            Closing += OnClosing;
        }

        private async Task RunAsync()
        {
            _running = true;
            _cts = new CancellationTokenSource();

            RetryButton.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Collapsed;
            CancelButton.Visibility = Visibility.Visible;
            CancelButton.IsEnabled = true;
            StatusText.Text = "Downloading the update. Please keep the app open.";
            UpdateProgress.Value = 0;
            ProgressText.Text = "0%";

            UpdateManager updates = UpdateManager.Instance;
            updates.LogMessage += OnLogMessage;
            updates.DownloadProgress += OnProgress;

            bool started;
            try
            {
                started = await updates.DownloadUpdate(_cts.Token);
            }
            finally
            {
                updates.LogMessage -= OnLogMessage;
                updates.DownloadProgress -= OnProgress;
                _running = false;
            }

            // On success the process has already exited to apply the update; this only runs when it did not start.
            if (!started)
            {
                bool cancelled = _cts.IsCancellationRequested;
                StatusText.Text = cancelled
                    ? "Update cancelled. Nothing was changed."
                    : "The update could not be completed. Your installed version was not modified.";
                CancelButton.Visibility = Visibility.Collapsed;
                RetryButton.Visibility = Visibility.Visible;
                CloseButton.Visibility = Visibility.Visible;
            }
        }

        private void OnLogMessage(object? sender, string message) =>
            Dispatcher.InvokeAsync(() =>
            {
                LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

                if (LogBox.Text.Length > MaxLogChars)
                    LogBox.Text = LogBox.Text[^(MaxLogChars / 2)..];

                LogBox.ScrollToEnd();
            });

        private void OnProgress(object? sender, ProgressEventArgs e) =>
            Dispatcher.InvokeAsync(() =>
            {
                UpdateProgress.Value = e.Progress;
                ProgressText.Text = e.TotalBytes > 0
                    ? $"{GitHubDirectoryDownloaderService.FormatBytes(e.DownloadedBytes)} / {GitHubDirectoryDownloaderService.FormatBytes(e.TotalBytes)}  •  {e.Progress}%"
                    : $"{e.Progress}%";
            });

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            CancelButton.IsEnabled = false;
            StatusText.Text = "Cancelling...";
            _cts?.Cancel();
        }

        private async void OnRetryClick(object sender, RoutedEventArgs e)
        {
            LogBox.AppendText(Environment.NewLine);
            await RunAsync();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        // The window cannot be closed mid-download; use Cancel so the update stops cleanly.
        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (_running)
                e.Cancel = true;
        }
    }
}