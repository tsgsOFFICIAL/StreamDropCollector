using MessageBox = System.Windows.MessageBox;
using System.Diagnostics;
using System.Windows;
using Core.Managers;
using System.IO;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private static readonly Lazy<SettingsView> _instance = new(() => new SettingsView());
        public static SettingsView Instance => _instance.Value;

        private SettingsView()
        {
            InitializeComponent();
            DataContext = UISettingsManager.Instance;
        }

        private void OnRemoveAllAccountsButtonClick(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("NUKE ALL ACCOUNTS AND RESTART?", "DANGER", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            string mainExe = Process.GetCurrentProcess().MainModule!.FileName;
            string folderToNuke = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Stream Drop Collector.exe.WebView2", "EBWebView", "Default", "Network");

            if (!Directory.Exists(folderToNuke))
            {
                MessageBox.Show("Nothing to nuke.");
                return;
            }

            // PURE CODE — NO EXTERNAL EXE, NO DLL, NO BULLSHIT
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C timeout /t 5 && rmdir /s /q \"{folderToNuke}\" && start \"\" \"{mainExe}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        }
    }
}