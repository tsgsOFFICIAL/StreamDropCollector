using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Text.Json;
using Core.Models;
using Core.Enums;
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
        private UpdateFrequency _updateFrequency = UpdateFrequency.Daily;
        private bool _autoClaimRewards = true;
        private bool _notifyOnDropUnlocked;
        private bool _notifyOnReadyToClaim;
        private bool _notifyOnAutoClaimed = true;
        private bool _updateAvailable = true;

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
                // Prevent enabling if StartWithWindows is off
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

        public UpdateFrequency UpdateFrequency
        {
            get => _updateFrequency;
            set => SetField(ref _updateFrequency, value);
        }

        public bool AutoClaimRewards
        {
            get => _autoClaimRewards;
            set
            {
                if (SetField(ref _autoClaimRewards, value))
                {
                    if (value && NotifyOnReadyToClaim)
                        NotifyOnReadyToClaim = false;

                    if (!value && NotifyOnAutoClaimed)
                        NotifyOnAutoClaimed = false;
                }
            }
        }

        public bool NotifyOnDropUnlocked
        {
            get => _notifyOnDropUnlocked;
            set => SetField(ref _notifyOnDropUnlocked, value);
        }

        public bool NotifyOnReadyToClaim
        {
            get => _notifyOnReadyToClaim;
            set
            {
                // Prevent enabling if AutoClaimRewards is on
                if (AutoClaimRewards)
                {
                    if (_notifyOnReadyToClaim != false)
                        SetField(ref _notifyOnReadyToClaim, false);

                    return;
                }
                else
                    SetField(ref _notifyOnReadyToClaim, value);
            }
        }

        public bool NotifyOnAutoClaimed
        {
            get => _notifyOnAutoClaimed;
            set
            {
                // Prevent enabling if AutoClaimRewards is off
                if (!AutoClaimRewards)
                {
                    if (_notifyOnAutoClaimed != false)
                        SetField(ref _notifyOnAutoClaimed, false);

                    return;
                }
                else
                    SetField(ref _notifyOnAutoClaimed, value);
            }
        }

        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set => SetField(ref _updateAvailable, value);
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
                    UpdateFrequency = settings.UpdateFrequency;
                    AutoClaimRewards = settings.AutoClaimRewards;
                    NotifyOnDropUnlocked = settings.NotifyOnDropUnlocked;
                    NotifyOnReadyToClaim = settings.NotifyOnReadyToClaim;
                    NotifyOnAutoClaimed = settings.NotifyOnAutoClaimed;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            { }

            UpdateStartupRegistry();
        }

        public void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);

                SettingsModel settings = new SettingsModel
                {
                    StartWithWindows = StartWithWindows,
                    MinimizeToTrayOnStartup = MinimizeToTrayOnStartup,
                    Theme = Theme,
                    UpdateFrequency = UpdateFrequency,
                    AutoClaimRewards = AutoClaimRewards,
                    NotifyOnDropUnlocked = NotifyOnDropUnlocked,
                    NotifyOnReadyToClaim = NotifyOnReadyToClaim,
                    NotifyOnAutoClaimed = NotifyOnAutoClaimed
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

                // StartWithWindows = true -> we MUST have a registry entry
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