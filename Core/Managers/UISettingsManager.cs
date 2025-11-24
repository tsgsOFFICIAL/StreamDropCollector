namespace Core.Managers
{
    public sealed class UISettingsManager
    {
        private static readonly Lazy<UISettingsManager> _instance = new(() => new UISettingsManager());
        public static UISettingsManager Instance => _instance.Value;
        public event Action? SettingsChanged;

        // === SETTINGS PROPERTIES ===
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTrayOnStartup { get; set; }
        public string Theme { get; set; } = "Dark";
        public bool AutoClaimRewards { get; set; } = true;
        public bool NotifyOnDropUnlocked { get; set; } = false;
        public bool NotifyOnReadyToClaim { get; set; } = false;
        public bool NotifyOnAutoClaimed { get; set; } = true;
        public bool PlayNotificationSound { get; set; } = true;

        private UISettingsManager()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load settings logic here
        }

        public void SaveSettings()
        {
            // Save settings logic here
        }
    }
}