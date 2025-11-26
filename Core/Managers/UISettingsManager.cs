using System.Runtime.CompilerServices;
using System.ComponentModel;

namespace Core.Managers
{
    public sealed class UISettingsManager
    {
        private static readonly Lazy<UISettingsManager> _instance = new(() => new UISettingsManager());
        public static UISettingsManager Instance => _instance.Value;
        public event PropertyChangedEventHandler? PropertyChanged;

        // === SETTINGS PROPERTIES ===
        private bool _startWithWindows;
        private bool _minimizeToTrayOnStartup;
        private string _theme = "Dark";
        private bool _autoClaimRewards = true;
        private bool _notifyOnDropUnlocked;
        private bool _notifyOnReadyToClaim;
        private bool _notifyOnAutoClaimed = true;

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set => SetField(ref _startWithWindows, value);
        }

        public bool MinimizeToTrayOnStartup
        {
            get => _minimizeToTrayOnStartup;
            set => SetField(ref _minimizeToTrayOnStartup, value);
        }

        public string Theme
        {
            get => _theme;
            set => SetField(ref _theme, value);
        }

        public bool AutoClaimRewards
        {
            get => _autoClaimRewards;
            set => SetField(ref _autoClaimRewards, value);
        }

        public bool NotifyOnDropUnlocked
        {
            get => _notifyOnDropUnlocked;
            set => SetField(ref _notifyOnDropUnlocked, value);
        }

        public bool NotifyOnReadyToClaim
        {
            get => _notifyOnReadyToClaim;
            set => SetField(ref _notifyOnReadyToClaim, value);
        }

        public bool NotifyOnAutoClaimed
        {
            get => _notifyOnAutoClaimed;
            set => SetField(ref _notifyOnAutoClaimed, value);
        }

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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}