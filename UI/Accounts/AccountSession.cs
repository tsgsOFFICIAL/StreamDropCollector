using Application = System.Windows.Application;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using Core.Enums;
using Core.Interfaces;
using Core.Logging;
using Core.Managers;
using Core.Models;
using Core.Services;
using UI.Models;
using UI.Views;

namespace UI.Accounts
{
    /// <summary>
    /// Runtime state of one account: its isolated browser profile, login service, mining engine, and bindable UI state.
    /// </summary>
    public sealed class AccountSession : INotifyPropertyChanged, IDisposable
    {
        /// <summary>Limits how many accounts fetch their campaign lists at the same moment.</summary>
        private static readonly SemaphoreSlim LoadGate = new(3, 3);

        private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
        private readonly object _loadTriggerLock = new();
        private readonly DropsService _dropsService = new();
        private readonly ILoginService _loginService;
        private CancellationTokenSource? _currentLoadCts;
        private TwitchGqlService? _gqlService;
        private bool _loadScheduled;
        private bool _disposed;

        /// <summary>Initializes a session for the given account.</summary>
        public AccountSession(AccountModel model)
        {
            Model = model;

            bool isTwitch = model.Platform == Platform.Twitch;
            string platformName = isTwitch ? "Twitch" : "Kick";
            string brandBrush = isTwitch ? "TwitchBrush" : "KickBrush";

            Engine = new DropsInventoryManager(model);
            Connection = new PlatformConnectionState(platformName, brandBrush, $"Login {platformName}")
            {
                LoginButtonText = "Checking...",
                ConnectionStatus = "Checking...",
                ConnectionColor = "Orange"
            };
            Progress = new PlatformProgressState(platformName, brandBrush);

            _loginService = isTwitch ? new TwitchLoginService() : new KickLoginService();
            _loginService.StatusChanged += OnStatusChanged;
            WireEngineEvents(isTwitch);

            // A disabled account never creates a browser window until it is enabled.
            if (model.Enabled)
            {
                CreateHost();
                _statusText = "Initializing";
                _statusDetails = "Please wait...";
            }
            else
            {
                _statusText = "Disabled";
                _statusDetails = "Account is disabled";
                Connection.ConnectionStatus = "Offline";
                Connection.ConnectionColor = "Gray";
                Connection.LoginButtonText = $"Login {platformName}";
                Connection.IsLoginEnabled = true;
            }
        }

        private HiddenWebViewHost? _host;

        /// <summary>Gets the hidden browser host bound to this account's profile, or <see langword="null"/> while disabled.</summary>
        public HiddenWebViewHost? Host => _host;

        private void CreateHost()
        {
            _host = new HiddenWebViewHost(Model);

            if (IsTwitch)
            {
                _gqlService = new TwitchGqlService(_host);
                Engine.InitializeWebViews(_host, null);
            }
            else
            {
                Engine.InitializeWebViews(null, _host);
            }
        }

        /// <summary>
        /// Closes the browser window and disposes its WebView immediately, releasing its processes and memory.
        /// </summary>
        private void DestroyHost()
        {
            HiddenWebViewHost? host = _host;
            _host = null;
            _gqlService = null;
            Engine.InitializeWebViews(null, null);

            if (host == null)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    host.WebView.Dispose();
                    host.Close();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Accounts", $"Failed closing browser host for {Model.DisplayName}: {ex.Message}");
                }
            });
        }

        private async Task DisableAsync()
        {
            _currentLoadCts?.Cancel();
            await Engine.PauseMiningAsync();

            // Wait for any in-flight campaign load to unwind before tearing down the browser it uses.
            await _loadSemaphore.WaitAsync();
            _loadSemaphore.Release();

            if (Enabled || _disposed)
                return;

            DestroyHost();
            Progress.Reset();
            Connection.ConnectionStatus = "Offline";
            Connection.ConnectionColor = "Gray";
            OnPropertyChanged(nameof(IsConnected));
            StatusText = "Disabled";
            StatusDetails = "Account is disabled";
        }

        /// <summary>Gets the persisted account definition.</summary>
        public AccountModel Model { get; }

        /// <summary>Gets the account's mining engine.</summary>
        public DropsInventoryManager Engine { get; }

        /// <summary>Gets the bindable connection state.</summary>
        public PlatformConnectionState Connection { get; }

        /// <summary>Gets the bindable mining progress.</summary>
        public PlatformProgressState Progress { get; }

        /// <summary>Gets a value indicating whether this is a Twitch account.</summary>
        public bool IsTwitch => Model.Platform == Platform.Twitch;

        /// <summary>Gets the platform name.</summary>
        public string PlatformName => Connection.PlatformName;

        /// <summary>Gets the brand brush resource key.</summary>
        public string BrandBrushKey => Connection.BrandBrushKey;

        /// <summary>Gets the account's connection status.</summary>
        public ConnectionStatus? Status => _loginService.Status;

        /// <summary>Gets a value indicating whether the account is logged in.</summary>
        public bool IsConnected => Enabled && _loginService.Status == ConnectionStatus.Connected;

        /// <summary>Gets a short marker shown next to the name when the account overrides global settings.</summary>
        public string CustomSettingsLabel => Model.Overrides.HasAny ? "  \u2022 custom settings" : string.Empty;

        /// <summary>Re-evaluates mining and refreshes bindings after the account's overrides were edited.</summary>
        public void ApplyOverridesChanged()
        {
            OnPropertyChanged(nameof(CustomSettingsLabel));
            ModelChanged?.Invoke(this);
            Engine.NotifyAccountSettingsChanged();
        }

        /// <summary>Gets a value indicating whether the engine is actively mining.</summary>
        public bool IsMining => _statusText == "Mining";

        /// <summary>Raised when the account becomes connected and Twitch Helix access should be ensured.</summary>
        public event Action<AccountSession>? Connected;

        /// <summary>Raised when the user edited a persisted field (name or enabled state) and the list should be saved.</summary>
        public event Action<AccountSession>? ModelChanged;

        /// <summary>Gets or sets the display name.</summary>
        public string DisplayName
        {
            get => Model.DisplayName;
            set
            {
                if (Model.DisplayName == value)
                    return;

                Model.DisplayName = value;
                OnPropertyChanged();
                ModelChanged?.Invoke(this);
            }
        }

        /// <summary>Gets or sets whether this account participates in mining.</summary>
        public bool Enabled
        {
            get => Model.Enabled;
            set
            {
                if (Model.Enabled == value)
                    return;

                Model.Enabled = value;
                OnPropertyChanged();
                ModelChanged?.Invoke(this);

                OnPropertyChanged(nameof(IsConnected));

                if (value)
                {
                    if (_host == null)
                        CreateHost();

                    StatusText = "Starting";
                    StatusDetails = "Re-enabling account";
                    _ = ValidateAsync();
                }
                else
                {
                    _ = DisableAsync();
                }
            }
        }

        private string _statusText;

        /// <summary>Gets the high-level miner status label.</summary>
        public string StatusText
        {
            get => _statusText;
            private set
            {
                _statusText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsMining));
            }
        }

        private string _statusDetails;

        /// <summary>Gets the detailed miner status message.</summary>
        public string StatusDetails
        {
            get => _statusDetails;
            private set { _statusDetails = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Validates the stored login. Used on startup, after logging in, and when re-enabling the account.
        /// </summary>
        public async Task ValidateAsync()
        {
            if (!Enabled || _disposed || _host == null)
                return;

            try
            {
                await _loginService.ValidateCredentialsAsync(_host);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Accounts", $"Validation failed for {Model.DisplayName} ({PlatformName}).", ex);
            }
        }

        /// <summary>
        /// Opens the platform login window for this account's profile, then re-validates.
        /// </summary>
        public async Task LoginAsync()
        {
            if (IsTwitch)
                new TwitchLoginWindow(Model).ShowDialog();
            else
                new KickLoginWindow(Model).ShowDialog();

            await ValidateAsync();
        }

        /// <summary>
        /// Schedules a debounced campaign load, ensuring rapid triggers produce a single load.
        /// </summary>
        public void ScheduleDropsLoad()
        {
            if (!Enabled || _disposed)
                return;

            lock (_loadTriggerLock)
            {
                if (_loadScheduled)
                    return;

                _loadScheduled = true;
            }

            _ = Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(300);

                lock (_loadTriggerLock)
                    _loadScheduled = false;

                await LoadDropsAsync();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Loads this account's campaigns and progress, then resumes mining.
        /// </summary>
        public async Task LoadDropsAsync()
        {
            if (!Enabled || _disposed)
                return;

            _currentLoadCts?.Cancel();
            await _loadSemaphore.WaitAsync();
            bool gateHeld = false;

            try
            {
                await Engine.PauseMiningAsync();

                if (!IsConnected)
                {
                    StatusText = "Need login";
                    StatusDetails = $"Please login to {PlatformName} to load campaigns.";
                    return;
                }

                await LoadGate.WaitAsync();
                gateHeld = true;

                using CancellationTokenSource cts = new();
                _currentLoadCts = cts;

                StatusText = "Loading Campaigns";
                StatusDetails = "Fetching latest drops...";

                HiddenWebViewHost? host = _host;
                if (host == null)
                    return;

                List<DropsCampaign> campaigns = [];
                await foreach (IReadOnlyList<DropsCampaign> batch in _dropsService.GetAllActiveCampaignsAsync(
                    IsTwitch ? null : host, IsTwitch ? null : Status,
                    IsTwitch ? host : null, IsTwitch ? Status : null,
                    _gqlService, cts.Token))
                {
                    campaigns.AddRange(batch);
                }

                AppLogger.Info("Accounts", $"{Model.DisplayName} ({PlatformName}) loaded {campaigns.Count} campaigns.");
                Engine.UpdateCampaigns(campaigns.AsReadOnly(), _gqlService, startMining: false, knownGames: _dropsService.KnownGames);

                StatusText = "Idle";
                StatusDetails = $"{campaigns.Count} active campaigns loaded";
            }
            catch (OperationCanceledException)
            {
                AppLogger.Debug("Accounts", $"Load canceled for {Model.DisplayName}.");
            }
            catch (Exception ex)
            {
                StatusText = "Failed to load campaigns";
                StatusDetails = ex.Message;
                AppLogger.Error("Accounts", $"Load failed for {Model.DisplayName}.", ex);
            }
            finally
            {
                if (gateHeld)
                    LoadGate.Release();

                _currentLoadCts = null;
                _loadSemaphore.Release();

                if (Enabled && IsConnected && !_disposed)
                    await Engine.ResumeMiningAsync();
            }
        }

        private void WireEngineEvents(bool isTwitch)
        {
            static void OnUi(Action action) => Application.Current.Dispatcher.InvokeAsync(action);

            if (isTwitch)
            {
                Engine.TwitchProgressChanged += (camp, drop) => OnUi(() => { Progress.CampaignProgress = camp; Progress.DropProgress = drop; });
                Engine.TwitchTimeRemainingChanged += (camp, drop) => OnUi(() => Progress.SetTimeRemaining(camp, drop));
                Engine.TwitchChannelChanged += channel => OnUi(() => Progress.MinedChannel = channel);
                Engine.TwitchCampaignChanged += (name, image) => OnUi(() => { Progress.CampaignName = name; Progress.CampaignImageUrl = image ?? string.Empty; });
                Engine.TwitchDropChanged += (name, image) => OnUi(() => { Progress.DropName = name; Progress.DropImageUrl = image ?? string.Empty; });
            }
            else
            {
                Engine.KickProgressChanged += (camp, drop) => OnUi(() => { Progress.CampaignProgress = camp; Progress.DropProgress = drop; });
                Engine.KickTimeRemainingChanged += (camp, drop) => OnUi(() => Progress.SetTimeRemaining(camp, drop));
                Engine.KickChannelChanged += channel => OnUi(() => Progress.MinedChannel = channel);
                Engine.KickCampaignChanged += (name, image) => OnUi(() =>
                {
                    Progress.CampaignName = name;
                    Progress.IsLevelFarming = name == Core.Mining.Kick.KickLevelFarmingCampaign.DisplayName;
                    Progress.CampaignImageUrl = image ?? string.Empty;
                });
                Engine.KickDropChanged += (name, image) => OnUi(() => { Progress.DropName = name; Progress.DropImageUrl = image ?? string.Empty; });
                Engine.KickLevelChanged += level => OnUi(() =>
                {
                    Progress.LevelBadgeUrl = level.BadgeImageUrl ?? string.Empty;
                    Progress.LevelPercent = level.PercentExact;
                    Progress.LevelText = $"Level {level.Level} • {level.ProgressXp:N0} / {level.ProgressXp + level.XpToNextLevel:N0} XP";
                });
            }

            Engine.MinerStatusChanged += status => OnUi(() =>
            {
                if (!Enabled)
                    return;

                switch (status)
                {
                    case "Idle": StatusText = "Idle"; StatusDetails = "Waiting for drops"; break;
                    case "Starting": StatusText = "Starting"; StatusDetails = "Finding streams to mine"; break;
                    case "Evaluating": StatusText = "Evaluating"; StatusDetails = "Checking streams for drops eligibility"; break;
                    case "Mining": StatusText = "Mining"; StatusDetails = "Mining streams to earn drops"; break;
                }
            });
        }

        private void OnStatusChanged(ConnectionStatus status)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                string platform = PlatformName;
                Connection.LoginButtonText = "Checking...";

                switch (status)
                {
                    case ConnectionStatus.NotConnected:
                        Connection.ConnectionStatus = "Not Connected";
                        Connection.ConnectionColor = "Red";
                        Connection.LoginButtonText = $"Login {platform}";
                        Connection.IsLoginEnabled = true;
                        if (Enabled)
                        {
                            StatusText = "Need login";
                            StatusDetails = $"Please login to {platform}.";
                        }
                        break;

                    case ConnectionStatus.Validating:
                        Connection.ConnectionStatus = "Validating...";
                        Connection.ConnectionColor = "Orange";
                        Connection.IsLoginEnabled = false;
                        break;

                    case ConnectionStatus.Connected:
                        Connection.ConnectionStatus = "Connected";
                        Connection.ConnectionColor = "Lime";
                        Connection.LoginButtonText = $"{platform} Logged in";
                        Connection.IsLoginEnabled = false;
                        OnPropertyChanged(nameof(IsConnected));
                        _ = DiscoverDisplayNameAsync();
                        Connected?.Invoke(this);
                        ScheduleDropsLoad();
                        break;

                    case ConnectionStatus.Connecting:
                        Connection.ConnectionStatus = "Connecting...";
                        Connection.ConnectionColor = "Yellow";
                        Connection.IsLoginEnabled = false;
                        break;
                }

                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsConnected));
            });
        }

        /// <summary>
        /// Best-effort lookup of the platform username so accounts are recognizable; keeps the current name on any failure.
        /// </summary>
        private async Task DiscoverDisplayNameAsync()
        {
            try
            {
                HiddenWebViewHost? host = _host;
                if (host == null)
                    return;

                string? name = null;

                if (IsTwitch)
                {
                    name = await host.GetCookieValueAsync("https://twitch.tv", "login");
                }
                else
                {
                    string? encoded = await host.GetCookieValueAsync("https://kick.com", "session_token");
                    if (!string.IsNullOrEmpty(encoded))
                    {
                        string token = JsonSerializer.Serialize(Uri.UnescapeDataString(encoded));
                        string? json = await host.ExecuteAsyncScriptAsync($@"
                            const r = await fetch('https://kick.com/api/v1/user', {{
                                credentials: 'include',
                                headers: {{ 'Accept': 'application/json', 'Authorization': 'Bearer ' + {token} }}
                            }});
                            return r.ok ? await r.text() : '';", 10000);

                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            using JsonDocument doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("username", out JsonElement user))
                                name = user.GetString();
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(name))
                    await Application.Current.Dispatcher.InvokeAsync(() => DisplayName = name);
            }
            catch (Exception ex)
            {
                AppLogger.Debug("Accounts", $"Display name lookup failed for {Model.Id}: {ex.Message}");
            }
        }

        /// <summary>Gets the label used by account pickers, for example "tsgsdev (Kick)".</summary>
        public override string ToString() => $"{Model.DisplayName} ({PlatformName})";

        /// <summary>Raised when a bindable property changes.</summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>Stops mining and releases the browser host.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _currentLoadCts?.Cancel();
            _loginService.StatusChanged -= OnStatusChanged;
            Engine.Dispose();

            DestroyHost();
        }
    }
}