using MessageBox = System.Windows.MessageBox;
using UI.Controls;
using System.Diagnostics;
using System.Windows;
using Core.Managers;
using Core.Logging;
using Core.Enums;
using System.IO;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private static readonly Lazy<SettingsView> _instance = new(() => new SettingsView());

        /// <summary>
        /// Gets the singleton instance of the settings view.
        /// </summary>
        public static SettingsView Instance => _instance.Value;

        private SettingsView()
        {
            InitializeComponent();
            DataContext = UISettingsManager.Instance;
        }

        private void OnUpdateButtonClick(object sender, RoutedEventArgs e)
        {
            if (!UISettingsManager.Instance.UpdateAvailable || UpdateManager.Instance.IsUpdating)
                return;

            // Disable the button while the update window (progress bar and log) is open.
            System.Windows.Controls.Button button = (System.Windows.Controls.Button)sender;
            button.IsEnabled = false;
            button.Content = "Updating...";

            try
            {
                new UpdateWindow { Owner = Window.GetWindow(this) }.ShowDialog();
            }
            finally
            {
                button.Content = "Update Now";
                button.IsEnabled = true;
            }
        }

        private void OnRemoveAllAccountsButtonClick(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("NUKE ALL ACCOUNTS AND RESTART?", "DANGER", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            string mainExe = Process.GetCurrentProcess().MainModule!.FileName;
            string folderToNuke = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Stream Drop Collector.exe.WebView2", "EBWebView", "Default", "Network");
            string accountsFolder = Core.Models.AccountModel.AccountsRootFolder;
            string accountsFile = Path.Combine(Path.GetDirectoryName(accountsFolder)!, "Accounts.json");

            if (!Directory.Exists(folderToNuke) && !Directory.Exists(accountsFolder) && !File.Exists(accountsFile))
            {
                MessageBox.Show("Nothing to nuke.");
                return;
            }

            // PURE CODE - NO EXTERNAL EXE, NO DLL, NO BULLSHIT
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C timeout /t 5 & rmdir /s /q \"{folderToNuke}\" & rmdir /s /q \"{accountsFolder}\" & del /q \"{accountsFile}\" & start \"\" \"{mainExe}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private void OnOpenLogsFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                AppLogger.Initialize();

                string logsDir = AppLogger.LogDirectoryPath;
                Directory.CreateDirectory(logsDir);

                Process.Start(new ProcessStartInfo
                {
                    FileName = logsDir,
                    UseShellExecute = true
                });

                AppLogger.Info("Settings", $"Opened logs folder: {logsDir}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Settings", "Failed to open logs folder.", ex);
                MessageBox.Show($"Failed to open logs folder.\n\n{ex.Message}", "Logs", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnTestWebhookClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = (System.Windows.Controls.Button)sender;
            button.IsEnabled = false;
            button.Content = "Sending...";

            try
            {
                string? error = await NotificationManager.SendTestWebhookAsync(UISettingsManager.Instance.WebhookUrl);
                if (error == null)
                    MessageBox.Show("Test message sent.", "Webhook", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"Webhook test failed.\n\n{error}", "Webhook", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                button.Content = "Send Test";
                button.IsEnabled = true;
            }
        }

        private void OnSendAllSamplesLoaded(object sender, RoutedEventArgs e)
        {
#if DEBUG
            ((System.Windows.Controls.Button)sender).Visibility = Visibility.Visible;
#endif
        }

        private async void OnSendAllSamplesClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Button button = (System.Windows.Controls.Button)sender;
            button.IsEnabled = false;

            // Mirrors every ShowNotification call site; spaced out to stay under Discord's webhook rate limit.
            (string Title, string Message, bool Webhook)[] samples =
            [
                ("Drop Claimed", "Successfully claimed drop reward: Sample Reward", true),
                ("Drop Ready to Claim", "You have 2 drops rewards ready to claim. Please claim them manually.", true),
                ("Drop Claim Failed", "Failed to claim drop reward, re-trying in a minute: Sample Reward", true),
                ("Account Not Linked", "Link your game account on Kick, then claim manually: Sample Reward", true),
                ("Update Available", "A new version is available.", true),
                ("Stream Drop Collector", "Application updated successfully!", true),
                ("Stream Drop Collector", "Minimized to tray - drops still farming!", false),
            ];

            try
            {
                foreach ((string title, string message, bool webhook) in samples)
                {
                    NotificationManager.ShowNotification(title, message, sendWebhook: webhook);
                    await Task.Delay(1500);
                }
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private void OnPriorityMove(object sender, PriorityMoveEventArgs e) =>
            UISettingsManager.Instance.MoveGamePriority(e.Option.Platform, e.Option.Slug, e.Offset);

        private void OnClearTwitchWhitelistClick(object sender, RoutedEventArgs e)
        {
            UISettingsManager.Instance.ClearGameWhitelist(Platform.Twitch);
        }

        private void OnClearKickWhitelistClick(object sender, RoutedEventArgs e)
        {
            UISettingsManager.Instance.ClearGameWhitelist(Platform.Kick);
        }
    }
}