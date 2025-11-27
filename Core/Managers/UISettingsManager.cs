using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Text.Json;
using Core.Models;
using System.IO;

namespace Core.Managers
{
    public sealed class UISettingsManager : INotifyPropertyChanged
    {
        private static readonly Lazy<UISettingsManager> _instance = new(() => new UISettingsManager());
        public static UISettingsManager Instance => _instance.Value;
        public event PropertyChangedEventHandler? PropertyChanged;
        private static readonly string _settingsFilePath = Path.Combine(Environment.ExpandEnvironmentVariables("%APPDATA%"), "Stream Drop Collector", "Settings.json");
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        // === SETTINGS PROPERTIES ===
        private bool _startWithWindows;
        private bool _minimizeToTrayOnStartup;
        private string _theme = "System";
        private bool _autoClaimRewards = true;
        private bool _notifyOnDropUnlocked;
        private bool _notifyOnReadyToClaim;
        private bool _notifyOnAutoClaimed = true;

        public bool StartWithWindows
        {
            get => _startWithWindows;
            set
            {
                if (SetField(ref _startWithWindows, value))
                {
                    if (!value && MinimizeToTrayOnStartup)
                        MinimizeToTrayOnStartup = false;
                }
            }
        }

        public bool MinimizeToTrayOnStartup
        {
            get => _minimizeToTrayOnStartup;
            set
            {
                // Prevent enabling if StartWithWindows is false
                if (!StartWithWindows)
                {
                    if (_minimizeToTrayOnStartup != false)
                        SetField(ref _minimizeToTrayOnStartup, false);

                    return;
                }
                else
                    SetField(ref _minimizeToTrayOnStartup, value);
            }
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
            if (!File.Exists(_settingsFilePath))
                return; // First run — use defaults

            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                SettingsModel? settings = JsonSerializer.Deserialize<SettingsModel>(json, _jsonOptions);

                if (settings != null)
                {
                    StartWithWindows = settings.StartWithWindows;
                    MinimizeToTrayOnStartup = settings.MinimizeToTrayOnStartup;
                    Theme = settings.Theme ?? "System";
                    AutoClaimRewards = settings.AutoClaimRewards;
                    NotifyOnDropUnlocked = settings.NotifyOnDropUnlocked;
                    NotifyOnReadyToClaim = settings.NotifyOnReadyToClaim;
                    NotifyOnAutoClaimed = settings.NotifyOnAutoClaimed;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {

            }

            UpdateStartupRegistry();
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);

                SettingsModel settings = new SettingsModel
                {
                    StartWithWindows = _startWithWindows,
                    MinimizeToTrayOnStartup = _minimizeToTrayOnStartup,
                    Theme = _theme,
                    AutoClaimRewards = _autoClaimRewards,
                    NotifyOnDropUnlocked = _notifyOnDropUnlocked,
                    NotifyOnReadyToClaim = _notifyOnReadyToClaim,
                    NotifyOnAutoClaimed = _notifyOnAutoClaimed
                };

                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {

            }
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

            // Handle special cases
            if (propertyName is nameof(StartWithWindows) or nameof(MinimizeToTrayOnStartup))
                UpdateStartupRegistry();

            // Auto-save whenever a setting changes (lightweight & convenient)
            Task.Run(SaveSettings); // Fire-and-forget on background thread

            return true;
        }

        private void UpdateStartupRegistry()
        {
            string keyName = "StreamDropCollector";
            string exePath = Utility.GetExePath();

            try
            {
                if (!StartWithWindows)
                {
                    // Just remove it — clean and simple
                    Utility.RemoveFromRegistry(keyName);
                    return;
                }

                // StartWithWindows = true → we MUST have a registry entry
                if (MinimizeToTrayOnStartup)
                {
                    // Launch minimized
                    Utility.WriteToRegistry(keyName, exePath, ["--minimize"]);
                }
                else
                {
                    // Launch normally
                    Utility.WriteToRegistry(keyName, exePath);
                }
            }
            catch (Exception ex)
            {
                // Never let registry errors crash settings flow
                System.Diagnostics.Debug.WriteLine($"[Startup Registry] Failed: {ex.Message}");
                // Optional: show non-blocking toast later if you want
            }
        }
    }
}