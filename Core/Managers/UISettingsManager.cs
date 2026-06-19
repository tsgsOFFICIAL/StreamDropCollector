using System.Runtime.CompilerServices;
using Timer = System.Timers.Timer;
using System.Net.Http.Headers;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
using Core.Logging;
using Core.Models;
using Core.Enums;
using System.IO;

namespace Core.Managers
{
    /// <summary>
    /// Manages user-facing application settings, game filter whitelists, update checks, and startup registry configuration.
    /// </summary>
    /// <remarks>This class follows the singleton pattern and implements <see cref="INotifyPropertyChanged"/> for data binding.
    /// Settings are persisted to disk and auto-saved when changed.</remarks>
    public sealed class UISettingsManager : INotifyPropertyChanged
    {
        private static readonly Lazy<UISettingsManager> _instance = new(() => new UISettingsManager());

        /// <summary>
        /// Gets the singleton instance of the UI settings manager.
        /// </summary>
        public static UISettingsManager Instance => _instance.Value;

        /// <summary>
        /// Occurs when a bindable setting property value changes.
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Occurs when <see cref="MiningPriorityMode"/> changes after settings have finished loading.
        /// </summary>
        public event Action<MiningPriorityMode>? MiningPriorityModeChanged;

        /// <summary>
        /// Occurs when the game whitelist or blacklist mode changes for a platform.
        /// </summary>
        public event Action<Platform>? GameWhitelistChanged;
        private static readonly string _settingsFilePath = Path.Combine(Environment.ExpandEnvironmentVariables("%APPDATA%"), "Stream Drop Collector", "Settings.json");
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };

        // === SETTINGS PROPERTIES ===
        private bool _startWithWindows;
        private bool _minimizeToTrayOnStartup;
        private bool _runInBackground;
        private string _theme = "System";
        private UpdateFrequency _updateFrequency = UpdateFrequency.Daily;
        private bool _autoClaimRewards = true;
        private bool _notifyOnReadyToClaim;
        private bool _notifyOnAutoClaimed = true;
        private bool _verboseDebugLogging;
        private bool _updateAvailable = false;
        private bool _notifyOnNewUpdateAvailable = true;
        private DateTime? _lastUpdateCheck = null;
        private MiningPriorityMode _miningPriorityMode = MiningPriorityMode.AvailabilityThenProgress;
        private List<string> _twitchGameWhitelistSlugs = new List<string>();
        private List<string> _kickGameWhitelistSlugs = new List<string>();
        private bool _twitchGameFilterBlacklistMode;
        private bool _kickGameFilterBlacklistMode;
        private bool _isUpdatingGameFilterOptions;
        private bool _isLoadingSettings;

        /// <summary>
        /// Gets the selectable Twitch game filter options shown in settings.
        /// </summary>
        public ObservableCollection<GameFilterOption> TwitchGameFilterOptions { get; } = new ObservableCollection<GameFilterOption>();

        /// <summary>
        /// Gets the selectable Kick game filter options shown in settings.
        /// </summary>
        public ObservableCollection<GameFilterOption> KickGameFilterOptions { get; } = new ObservableCollection<GameFilterOption>();

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
        /// Gets or sets a value indicating whether the application continues running in the background when the main window is closed.
        /// </summary>
        public bool RunInBackground
        {
            get => _runInBackground;
            set => SetField(ref _runInBackground, value);
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
                    NotifyOnNewUpdateAvailable = false;
                else if (UpdateFrequency == UpdateFrequency.OnLaunch && !_isLoadingSettings)
                    _ = CheckForUpdatesAsync(true);

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
        /// Gets or sets the strategy used to prioritize which campaigns are mined when multiple are eligible.
        /// </summary>
        public MiningPriorityMode MiningPriorityMode
        {
            get => _miningPriorityMode;
            set
            {
                if (SetField(ref _miningPriorityMode, value) && !_isLoadingSettings)
                    MiningPriorityModeChanged?.Invoke(value);
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
        /// Gets or sets a value indicating whether verbose diagnostic logging is enabled.
        /// </summary>
        public bool VerboseDebugLogging
        {
            get => _verboseDebugLogging;
            set => SetField(ref _verboseDebugLogging, value);
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

        /// <summary>
        /// Gets a human-readable summary of the current Twitch game filter selection.
        /// </summary>
        public string TwitchWhitelistSummary => _twitchGameWhitelistSlugs.Count == 0
            ? "All active Twitch games are allowed"
            : _twitchGameFilterBlacklistMode
                ? $"Excluding {_twitchGameWhitelistSlugs.Count} Twitch game(s)"
                : $"{_twitchGameWhitelistSlugs.Count} Twitch game(s) selected";

        /// <summary>
        /// Gets a human-readable summary of the current Kick game filter selection.
        /// </summary>
        public string KickWhitelistSummary => _kickGameWhitelistSlugs.Count == 0
            ? "All active Kick games are allowed"
            : _kickGameFilterBlacklistMode
                ? $"Excluding {_kickGameWhitelistSlugs.Count} Kick game(s)"
                : $"{_kickGameWhitelistSlugs.Count} Kick game(s) selected";

        /// <summary>
        /// Gets the read-only list of Twitch game slugs currently selected in the game filter.
        /// </summary>
        public IReadOnlyList<string> TwitchGameWhitelistSlugs => _twitchGameWhitelistSlugs.AsReadOnly();

        /// <summary>
        /// Gets the read-only list of Kick game slugs currently selected in the game filter.
        /// </summary>
        public IReadOnlyList<string> KickGameWhitelistSlugs => _kickGameWhitelistSlugs.AsReadOnly();

        /// <summary>
        /// When true, the selected Twitch games are excluded (mine everything else) instead of being an allow-list.
        /// </summary>
        public bool TwitchGameFilterBlacklistMode
        {
            get => _twitchGameFilterBlacklistMode;
            set
            {
                if (SetField(ref _twitchGameFilterBlacklistMode, value))
                {
                    OnPropertyChanged(nameof(TwitchWhitelistSummary));
                    GameWhitelistChanged?.Invoke(Platform.Twitch);
                }
            }
        }

        /// <summary>
        /// When true, the selected Kick games are excluded (mine everything else) instead of being an allow-list.
        /// </summary>
        public bool KickGameFilterBlacklistMode
        {
            get => _kickGameFilterBlacklistMode;
            set
            {
                if (SetField(ref _kickGameFilterBlacklistMode, value))
                {
                    OnPropertyChanged(nameof(KickWhitelistSummary));
                    GameWhitelistChanged?.Invoke(Platform.Kick);
                }
            }
        }

        private UISettingsManager()
        {
            LoadSettings();
            _ = CheckForUpdatesAsync(); // Fire and forget
        }

        private async Task CheckForUpdatesAsync(bool skipLoad = false)
        {
            if (UpdateFrequency != UpdateFrequency.Never)
            {
                if (!skipLoad)
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

                                AppLogger.Debug("UISettings", $"[Update Check] Skipping check. Next check in {timeLeft.TotalHours:F1} hours.");

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

                                AppLogger.Debug("UISettings", $"[Update Check] Skipping check. Next check in {timeLeft.TotalHours:F1} hours.");

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
                catch (Exception ex)
                {
                    AppLogger.Warn("UISettings", $"Update check request failed: {ex.Message}");
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
                return; // First run - use defaults

            _isLoadingSettings = true;
            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                SettingsModel? settings = JsonSerializer.Deserialize<SettingsModel>(json, _jsonOptions);

                if (settings != null)
                {
                    StartWithWindows = settings.StartWithWindows;
                    MinimizeToTrayOnStartup = settings.MinimizeToTrayOnStartup;
                    RunInBackground = settings.RunInBackground;
                    Theme = settings.Theme ?? "System";
                    UpdateFrequency = settings.UpdateFrequency;
                    AutoClaimRewards = settings.AutoClaimRewards;
                    MiningPriorityMode = settings.MiningPriorityMode;
                    NotifyOnReadyToClaim = settings.NotifyOnReadyToClaim;
                    NotifyOnAutoClaimed = settings.NotifyOnAutoClaimed;
                    VerboseDebugLogging = settings.VerboseDebugLogging;
                    NotifyOnNewUpdateAvailable = settings.NotifyOnNewUpdateAvailable;
                    _lastUpdateCheck = settings.LastUpdateCheck;
                    _twitchGameWhitelistSlugs = NormalizeWhitelist(settings.TwitchGameWhitelistSlugs);
                    _kickGameWhitelistSlugs = NormalizeWhitelist(settings.KickGameWhitelistSlugs);
                    _twitchGameFilterBlacklistMode = settings.TwitchGameFilterBlacklistMode;
                    _kickGameFilterBlacklistMode = settings.KickGameFilterBlacklistMode;
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLogger.Warn("UISettings", $"LoadSettings failed and defaults are used. {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                _isLoadingSettings = false;
            }

            OnPropertyChanged(nameof(TwitchWhitelistSummary));
            OnPropertyChanged(nameof(KickWhitelistSummary));

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
                    RunInBackground = RunInBackground,
                    Theme = Theme,
                    UpdateFrequency = UpdateFrequency,
                    AutoClaimRewards = AutoClaimRewards,
                    MiningPriorityMode = MiningPriorityMode,
                    NotifyOnReadyToClaim = NotifyOnReadyToClaim,
                    NotifyOnAutoClaimed = NotifyOnAutoClaimed,
                    VerboseDebugLogging = VerboseDebugLogging,
                    NotifyOnNewUpdateAvailable = NotifyOnNewUpdateAvailable,
                    LastUpdateCheck = _lastUpdateCheck,
                    TwitchGameWhitelistSlugs = [.. _twitchGameWhitelistSlugs],
                    KickGameWhitelistSlugs = [.. _kickGameWhitelistSlugs],
                    TwitchGameFilterBlacklistMode = _twitchGameFilterBlacklistMode,
                    KickGameFilterBlacklistMode = _kickGameFilterBlacklistMode
                };

                string json = JsonSerializer.Serialize(settings, _jsonOptions);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                AppLogger.Warn("UISettings", $"SaveSettings failed. {ex.GetType().Name}: {ex.Message}");
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
            if (!_isLoadingSettings)
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
                    // Just remove it - clean and simple
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
                AppLogger.Error("UISettings", "[Startup Registry] Failed", ex);
                // Optional: show non-blocking toast later if you want
            }
        }

        /// <summary>
        /// Rebuilds the Twitch and Kick game filter option lists from the supplied active campaigns.
        /// </summary>
        /// <param name="campaigns">The campaigns used to derive selectable game slugs and display names.</param>
        public void UpdateAvailableGameFilterOptions(IEnumerable<DropsCampaign> campaigns)
        {
            _isUpdatingGameFilterOptions = true;
            try
            {
                List<(Platform platform, string slug, string displayName)> options = campaigns
                    .Where(c => !string.IsNullOrWhiteSpace(c.Slug))
                    .Select(c => (c.Platform, c.Slug.Trim().ToLowerInvariant(), c.GameName))
                    .GroupBy(x => $"{x.Platform}:{x.Item2}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(x => x.Platform)
                    .ThenBy(x => x.Item3, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                RebuildOptionsCollection(
                    TwitchGameFilterOptions,
                    Platform.Twitch,
                    options.Where(x => x.platform == Platform.Twitch),
                    _twitchGameWhitelistSlugs);

                RebuildOptionsCollection(
                    KickGameFilterOptions,
                    Platform.Kick,
                    options.Where(x => x.platform == Platform.Kick),
                    _kickGameWhitelistSlugs);
            }
            finally
            {
                _isUpdatingGameFilterOptions = false;
            }

            OnPropertyChanged(nameof(TwitchWhitelistSummary));
            OnPropertyChanged(nameof(KickWhitelistSummary));
        }

        /// <summary>
        /// Clears the game filter selection for the specified platform and removes inactive filter entries.
        /// </summary>
        /// <param name="platform">The platform whose game filter should be reset.</param>
        public void ClearGameWhitelist(Platform platform)
        {
            if (platform == Platform.Twitch)
                _twitchGameWhitelistSlugs = new List<string>();
            else
                _kickGameWhitelistSlugs = new List<string>();

            ObservableCollection<GameFilterOption> options = platform == Platform.Twitch
                ? TwitchGameFilterOptions
                : KickGameFilterOptions;

            _isUpdatingGameFilterOptions = true;
            try
            {
                // Materialize collection to avoid "collection modified" if PropertyChanged events fire
                foreach (GameFilterOption option in options.ToList())
                    option.IsSelected = false;

                List<GameFilterOption> inactiveOptions = options
                    .Where(x => x.DisplayName.EndsWith(" (inactive)", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (GameFilterOption option in inactiveOptions)
                {
                    option.PropertyChanged -= OnGameFilterOptionPropertyChanged;
                    options.Remove(option);
                }
            }
            finally
            {
                _isUpdatingGameFilterOptions = false;
            }

            OnPropertyChanged(platform == Platform.Twitch ? nameof(TwitchWhitelistSummary) : nameof(KickWhitelistSummary));
            Task.Run(SaveSettings);
            GameWhitelistChanged?.Invoke(platform);
        }

        /// <summary>
        /// Determines whether the specified campaign is allowed by the current game filter for its platform.
        /// </summary>
        /// <param name="campaign">The campaign to evaluate against the allow-list or exclude-list configuration.</param>
        /// <returns><see langword="true"/> if the campaign should be included; otherwise, <see langword="false"/>.</returns>
        public bool IsCampaignAllowedByWhitelist(DropsCampaign campaign)
        {
            List<string> whitelist = campaign.Platform == Platform.Twitch
                ? _twitchGameWhitelistSlugs
                : _kickGameWhitelistSlugs;

            // No games selected => allow everything (regardless of mode).
            if (whitelist.Count == 0)
                return true;

            string slug = campaign.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
            bool inList = whitelist.Contains(slug, StringComparer.OrdinalIgnoreCase);

            bool excludeMode = campaign.Platform == Platform.Twitch
                ? _twitchGameFilterBlacklistMode
                : _kickGameFilterBlacklistMode;

            // Exclude mode: allow everything EXCEPT the selected games.
            // Allow mode: allow ONLY the selected games.
            return excludeMode ? !inList : inList;
        }

        private void RebuildOptionsCollection(
            ObservableCollection<GameFilterOption> collection,
            Platform platform,
            IEnumerable<(Platform platform, string slug, string displayName)> options,
            List<string> whitelist)
        {
            // Materialize collection to avoid "collection modified" during unsubscribe
            foreach (GameFilterOption existing in collection.ToList())
                existing.PropertyChanged -= OnGameFilterOptionPropertyChanged;

            collection.Clear();

            HashSet<string> seenSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((Platform optionPlatform, string slug, string displayName) in options)
            {
                GameFilterOption option = new GameFilterOption(
                    optionPlatform,
                    slug,
                    displayName,
                    whitelist.Contains(slug, StringComparer.OrdinalIgnoreCase));

                option.PropertyChanged += OnGameFilterOptionPropertyChanged;
                collection.Add(option);
                seenSlugs.Add(slug);
            }

            foreach (string slug in whitelist)
            {
                if (seenSlugs.Contains(slug))
                    continue;

                GameFilterOption option = new GameFilterOption(
                    platform,
                    slug,
                    $"{slug} (inactive)",
                    true);

                option.PropertyChanged += OnGameFilterOptionPropertyChanged;
                collection.Add(option);
            }
        }

        private void OnGameFilterOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_isUpdatingGameFilterOptions)
                return;

            if (e.PropertyName != nameof(GameFilterOption.IsSelected) || sender is not GameFilterOption option)
                return;

            List<string> whitelist = option.Platform == Platform.Twitch
                ? _twitchGameWhitelistSlugs
                : _kickGameWhitelistSlugs;

            if (option.IsSelected)
            {
                if (!whitelist.Contains(option.Slug, StringComparer.OrdinalIgnoreCase))
                    whitelist.Add(option.Slug);
            }
            else
            {
                whitelist.RemoveAll(x => string.Equals(x, option.Slug, StringComparison.OrdinalIgnoreCase));

                if (option.DisplayName.EndsWith(" (inactive)", StringComparison.OrdinalIgnoreCase))
                {
                    ObservableCollection<GameFilterOption> collection = option.Platform == Platform.Twitch
                        ? TwitchGameFilterOptions
                        : KickGameFilterOptions;

                    _isUpdatingGameFilterOptions = true;
                    try
                    {
                        option.PropertyChanged -= OnGameFilterOptionPropertyChanged;
                        collection.Remove(option);
                    }
                    finally
                    {
                        _isUpdatingGameFilterOptions = false;
                    }
                }
            }

            if (option.Platform == Platform.Twitch)
                _twitchGameWhitelistSlugs = NormalizeWhitelist(whitelist);
            else
                _kickGameWhitelistSlugs = NormalizeWhitelist(whitelist);

            OnPropertyChanged(option.Platform == Platform.Twitch ? nameof(TwitchWhitelistSummary) : nameof(KickWhitelistSummary));
            Task.Run(SaveSettings);
            GameWhitelistChanged?.Invoke(option.Platform);
        }

        private static List<string> NormalizeWhitelist(IEnumerable<string>? values)
        {
            if (values == null)
                return new List<string>();

            return values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}