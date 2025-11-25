using MessageBox = System.Windows.MessageBox;
using System.Windows.Controls;
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

        private readonly UISettingsManager _settingsManager = UISettingsManager.Instance;
        private SettingsView()
        {
            InitializeComponent();
            Loaded += OnSettingsViewLoaded;
        }

        private void OnSettingsViewLoaded(object sender, RoutedEventArgs e)
        {
            // === BIND ALL THE THINGS ===
            StartWithWindowsCheckBox.IsChecked = _settingsManager.StartWithWindows;
            MinimizeToTrayOnStartupCheckBox.IsChecked = _settingsManager.MinimizeToTrayOnStartup;

            // ComboBox — manually set selected item
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Content?.ToString() == _settingsManager.Theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }

            AutoClaimRewardsCheckBox.IsChecked = _settingsManager.AutoClaimRewards;
            NotifyOnDropUnlockedCheckBox.IsChecked = _settingsManager.NotifyOnDropUnlocked;
            NotifyOnReadyToClaimCheckBox.IsChecked = _settingsManager.NotifyOnReadyToClaim;
            NotifyOnAutoClaimedCheckBox.IsChecked = _settingsManager.NotifyOnAutoClaimed;

            // === SUBSCRIBE TO CHANGES ===
            StartWithWindowsCheckBox.Checked += OnSettingChanged;
            StartWithWindowsCheckBox.Unchecked += OnSettingChanged;
            MinimizeToTrayOnStartupCheckBox.Checked += OnSettingChanged;
            MinimizeToTrayOnStartupCheckBox.Unchecked += OnSettingChanged;
            ThemeComboBox.SelectionChanged += ThemeComboBox_SelectionChanged;
            AutoClaimRewardsCheckBox.Checked += OnSettingChanged;
            AutoClaimRewardsCheckBox.Unchecked += OnSettingChanged;
            NotifyOnDropUnlockedCheckBox.Checked += OnSettingChanged;
            NotifyOnDropUnlockedCheckBox.Unchecked += OnSettingChanged;
            NotifyOnReadyToClaimCheckBox.Checked += OnSettingChanged;
            NotifyOnReadyToClaimCheckBox.Unchecked += OnSettingChanged;
            NotifyOnAutoClaimedCheckBox.Checked += OnSettingChanged;
            NotifyOnAutoClaimedCheckBox.Unchecked += OnSettingChanged;

            // === DANGER BUTTON ===
            RemoveAllAccountsButton.Click += RemoveAllAccountsButton_Click;
        }

        private void OnSettingChanged(object? sender, RoutedEventArgs e)
        {
            // Update singleton
            _settingsManager.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
            _settingsManager.MinimizeToTrayOnStartup = MinimizeToTrayOnStartupCheckBox.IsChecked == true;
            _settingsManager.AutoClaimRewards = AutoClaimRewardsCheckBox.IsChecked == true;
            _settingsManager.NotifyOnDropUnlocked = NotifyOnDropUnlockedCheckBox.IsChecked == true;
            _settingsManager.NotifyOnReadyToClaim = NotifyOnReadyToClaimCheckBox.IsChecked == true;
            _settingsManager.NotifyOnAutoClaimed = NotifyOnAutoClaimedCheckBox.IsChecked == true;

            _settingsManager.SaveSettings();
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Content is string theme)
            {
                _settingsManager.Theme = theme;
                //_settingsManager.ApplyTheme();
                _settingsManager.SaveSettings();
            }
        }

        private void RemoveAllAccountsButton_Click(object sender, RoutedEventArgs e)
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