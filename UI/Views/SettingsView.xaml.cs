using MessageBox = System.Windows.MessageBox;
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