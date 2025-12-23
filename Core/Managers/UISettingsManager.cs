using System.Runtime.CompilerServices;
using Timer = System.Timers.Timer;
using System.Net.Http.Headers;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
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
        private bool _notifyOnReadyToClaim;
        private bool _notifyOnAutoClaimed = true;
        private bool _updateAvailable = false;
        private bool _notifyOnNewUpdateAvailable = true;
        private DateTime? _lastUpdateCheck = null;

        /// <summary>
        /// Gets or sets a value indicating whether the application starts automatically when Windows starts.
        /// </summary>
        /// <remarks>Disabling this option may also disable related startup behaviors, such as minimizing
        /// to the system tray on startup.</remarks>
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
        /// <summary>
        /// Gets or sets a value indicating whether the application should start minimized to the system tray on
        /// startup.
        /// </summary>
        /// <remarks>This property can only be enabled if the application is configured to start with
        /// Windows. If StartWithWindows is disabled, setting this property to true has no effect and the value will
        /// remain false.</remarks>
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
        /// <summary>
        /// Gets or sets the name of the current application theme.
        /// </summary>
        public string Theme
        {
            get => _theme;
            set => SetField(ref _theme, value);
        }
        /// <summary>
        /// Gets or sets the frequency at which updates are performed.
        /// </summary>
        public UpdateFrequency UpdateFrequency
        {
            get => _updateFrequency;
            set
            {
                SetField(ref _updateFrequency, value);

                if (value == UpdateFrequency.Never)
                {
                    NotifyOnNewUpdateAvailable = false;
                }

                OnPropertyChanged(nameof(IsUpdateNotificationEnabled));
            }
        }
        /// <summary>
        /// Gets or sets a value indicating whether rewards are automatically claimed when they become available.
        /// </summary>
        /// <remarks>Enabling this property may automatically disable certain notification options, such
        /// as notifications for rewards ready to claim or for rewards that have been auto-claimed. Changing this
        /// property can affect related notification settings.</remarks>
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
        /// <summary>
        /// Gets or sets a value indicating whether a notification should be sent when rewards are ready to be claimed.
        /// </summary>
        /// <remarks>This property cannot be enabled if automatic reward claiming is active. If <see
        /// cref="AutoClaimRewards"/> is <see langword="true"/>, setting this property to <see langword="true"/> has no
        /// effect and the value remains <see langword="false"/>.</remarks>
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
        /// <summary>
        /// Gets or sets a value indicating whether a notification is sent when rewards are automatically claimed.
        /// </summary>
        /// <remarks>This property can only be enabled if automatic reward claiming is active. If
        /// automatic reward claiming is disabled, setting this property to true has no effect and the value remains
        /// false.</remarks>
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
        /// <summary>
        /// Gets or sets a value indicating whether a software update is available.
        /// </summary>
        public bool UpdateAvailable
        {
            get => _updateAvailable;
            set
            {
                SetField(ref _updateAvailable, value);

                if (value && NotifyOnNewUpdateAvailable)
                    NotificationManager.ShowNotification("Update Available", "A new version is available.");
            }
        }
        /// <summary>
        /// Gets or sets a value indicating whether the application should notify the user when a new update is
        /// available.
        /// </summary>
        public bool NotifyOnNewUpdateAvailable
        {
            get => _notifyOnNewUpdateAvailable;
            set => SetField(ref _notifyOnNewUpdateAvailable, value);
        }
        /// <summary>
        /// Gets a value indicating whether update notifications are enabled.
        /// </summary>
        public bool IsUpdateNotificationEnabled => UpdateFrequency != UpdateFrequency.Never;

        private UISettingsManager()
        {
            LoadSettings();
            _ = CheckForUpdatesAsync(); // Fire and forget
        }

        private async Task CheckForUpdatesAsync()
        {
            if (UpdateFrequency != UpdateFrequency.Never)
            {
                LoadSettings(); // Ensure we have the latest settings, this includes last time we checked for an update

                if (_lastUpdateCheck.HasValue)
                {
                    TimeSpan timeSinceLastCheck = DateTime.Now - _lastUpdateCheck.Value;

                    switch (UpdateFrequency)
                    {
                        case UpdateFrequency.OnLaunch:
                            // Always check on launch
                            break;
                        case UpdateFrequency.Daily:
                            if (timeSinceLastCheck < TimeSpan.FromDays(1))
                            {
                                TimeSpan timeLeft = TimeSpan.FromDays(1) - timeSinceLastCheck;

                                // Create a timer, to check again in "timeLeft" and skip for now
                                Timer timer = new Timer(timeLeft.TotalMilliseconds);

                                timer.Elapsed += async (sender, e) =>
                                {
                                    timer.Stop();
                                    timer.Dispose();
                                    await CheckForUpdatesAsync();
                                };

                                timer.Start();

                                Debug.WriteLine($"[Update Check] Skipping check. Next check in {timeLeft.TotalHours:F1} hours.");

                                return; // Skip check
                            }
                            break;
                        case UpdateFrequency.Weekly:
                            if (timeSinceLastCheck < TimeSpan.FromDays(7))
                            {
                                TimeSpan timeLeft = TimeSpan.FromDays(7) - timeSinceLastCheck;

                                // Create a timer, to check again in "timeLeft" and skip for now
                                Timer timer = new Timer(timeLeft.TotalMilliseconds);

                                timer.Elapsed += async (sender, e) =>
                                {
                                    timer.Stop();
                                    timer.Dispose();
                                    await CheckForUpdatesAsync();
                                };

                                timer.Start();

                                Debug.WriteLine($"[Update Check] Skipping check. Next check in {timeLeft.TotalHours:F1} hours.");

                                return; // Skip check
                            }
                            break;
                    }
                }

                FileVersionInfo localVersionInfo = FileVersionInfo.GetVersionInfo(Utility.GetExePath());
                UpdateInfo? serverUpdateInfo;

                try
                {
                    using HttpClient client = new HttpClient();
                    client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
                    {
                        NoCache = true
                    };

                    client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

                    serverUpdateInfo = JsonSerializer.Deserialize<UpdateInfo>(await client.GetStringAsync("https://raw.githubusercontent.com/tsgsOFFICIAL/StreamDropCollector/master/updateInfo.sdc")) ?? new UpdateInfo();
                }
                catch (Exception)
                {
                    UpdateAvailable = false;
                    return;
                }

                if (Version.TryParse(serverUpdateInfo.Version, out Version? serverVersion) && Version.TryParse(localVersionInfo.FileVersion, out Version? localVersion))
                    UpdateAvailable = serverVersion > localVersion;
                else
                    UpdateAvailable = false;

                _lastUpdateCheck = DateTime.Now;
                SaveSettings();
            }
        }
        /// <summary>
        /// Loads application settings from the configuration file, if it exists, and applies them to the current
        /// instance.
        /// </summary>
        /// <remarks>If the configuration file does not exist or cannot be read, default settings are
        /// used. Invalid or inaccessible files are ignored without throwing an exception.</remarks>
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
                    NotifyOnReadyToClaim = settings.NotifyOnReadyToClaim;
                    NotifyOnAutoClaimed = settings.NotifyOnAutoClaimed;
                    NotifyOnNewUpdateAvailable = settings.NotifyOnNewUpdateAvailable;
                    _lastUpdateCheck = settings.LastUpdateCheck;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            { }

            UpdateStartupRegistry();
        }
        /// <summary>
        /// Saves the current application settings to the settings file in JSON format.
        /// </summary>
        /// <remarks>If the settings file or its directory does not exist, they are created automatically.
        /// Any I/O or access errors encountered during the save operation are silently ignored; the method does not
        /// throw exceptions in these cases.</remarks>
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
                    NotifyOnReadyToClaim = NotifyOnReadyToClaim,
                    NotifyOnAutoClaimed = NotifyOnAutoClaimed,
                    NotifyOnNewUpdateAvailable = NotifyOnNewUpdateAvailable,
                    LastUpdateCheck = _lastUpdateCheck
                };

                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {

            }
        }
        /// <summary>
        /// Raises the PropertyChanged event to notify listeners that a property value has changed.
        /// </summary>
        /// <remarks>Call this method in a property's setter to notify subscribers that the property's
        /// value has changed. This is commonly used to support data binding in applications that implement the
        /// INotifyPropertyChanged interface.</remarks>
        /// <param name="propertyName">The name of the property that changed. This value is optional and is automatically provided when called from
        /// a property setter.</param>
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        /// <summary>
        /// Sets the specified field to a new value and raises a property changed notification if the value has changed.
        /// </summary>
        /// <remarks>This method is typically used in property setters to implement the
        /// INotifyPropertyChanged pattern. It also performs additional actions such as updating startup settings and
        /// saving configuration when certain properties change.</remarks>
        /// <typeparam name="T">The type of the field and value being set.</typeparam>
        /// <param name="field">A reference to the field to update. The field is set to the new value if it differs from the current value.</param>
        /// <param name="value">The new value to assign to the field.</param>
        /// <param name="propertyName">The name of the property associated with the field. This is used for property change notification. If not
        /// specified, the caller member name is used.</param>
        /// <returns>true if the field value was changed and a property change notification was raised; otherwise, false.</returns>
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
        /// <summary>
        /// Updates the Windows startup registry entry to configure whether the application launches automatically when
        /// the user logs in.
        /// </summary>
        /// <remarks>This method adds or removes the application's registry entry based on the current
        /// startup and minimize settings. It does not throw exceptions if registry access fails; errors are logged for
        /// diagnostic purposes. This method should be called whenever the startup-related settings change to ensure the
        /// registry reflects the desired behavior.</remarks>
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