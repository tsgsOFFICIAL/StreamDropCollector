using UserControl = System.Windows.Controls.UserControl;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.ComponentModel;
using System.Windows;
using Core.Logging;
using Core.Managers;
using Core.Services;
using Core.Services.Twitch.Helix;
using Core.Models;
using Core.Enums;
using UI.Models;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for DashboardView.xaml
    /// </summary>
    public partial class DashboardView : UserControl, INotifyPropertyChanged
    {
        private readonly System.Timers.Timer _refreshTimer = new(TimeSpan.FromHours(1).TotalMilliseconds);

        private readonly SemaphoreSlim _loadDropsSemaphore = new(1, 1);
        private CancellationTokenSource? _currentLoadCts;
        private readonly object _loadTriggerLock = new();
        private bool _loadScheduled = false;

        private HiddenWebViewHost _twitchWebView = new();
        private HiddenWebViewHost _kickWebView = new();
        private TwitchGqlService? _twitchGqlService;

        private static bool _initialValidationCompleted = false;
        private static bool _isInitialized = false;

        private static readonly Lazy<DashboardView> _instance = new(() => new DashboardView());

        /// <summary>
        /// Gets the singleton instance of the dashboard view.
        /// </summary>
        public static DashboardView Instance => _instance.Value;

        // Services
        private readonly TwitchLoginService _twitchService = new();
        private readonly KickLoginService _kickService = new();
        private readonly DropsService _dropsService;

        // Observable collection for UI binding
        private readonly ObservableCollection<DropsCampaign> _activeCampaigns = new();

        /// <summary>
        /// Gets the read-only collection of active drop campaigns shown on the dashboard.
        /// </summary>
        public IReadOnlyCollection<DropsCampaign> ActiveCampaigns => _activeCampaigns;

        /// <summary>
        /// Gets the bindable Twitch account connection state for the dashboard.
        /// </summary>
        public PlatformConnectionState TwitchConnection { get; } = new("Twitch", "TwitchBrush", "Login Twitch");

        /// <summary>
        /// Gets the bindable Kick account connection state for the dashboard.
        /// </summary>
        public PlatformConnectionState KickConnection { get; } = new("Kick", "KickBrush", "Login Kick");

        private string _minerStatus = "Idle";

        /// <summary>
        /// Gets or sets the high-level miner status label shown on the dashboard.
        /// </summary>
        public string MinerStatus
        {
            get => _minerStatus;
            set
            {
                _minerStatus = value;
                OnPropertyChanged();
            }
        }
        private string _minerStatusDetails = "Waiting";

        /// <summary>
        /// Gets or sets the detailed miner status message shown beneath the main status label.
        /// </summary>
        public string MinerStatusDetails
        {
            get => _minerStatusDetails;
            set
            {
                _minerStatusDetails = value;
                OnPropertyChanged();
            }
        }
        /// <summary>
        /// Gets the bindable Twitch mining progress state for the dashboard.
        /// </summary>
        public PlatformProgressState TwitchProgress { get; } = new("Twitch", "TwitchBrush");

        /// <summary>
        /// Gets the bindable Kick mining progress state for the dashboard.
        /// </summary>
        public PlatformProgressState KickProgress { get; } = new("Kick", "KickBrush");

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        /// <remarks>This event is typically raised by the implementation of the INotifyPropertyChanged
        /// interface to notify subscribers that a property value has changed. Handlers receive the name of the property
        /// that changed in the event data. This event is commonly used in data binding scenarios to update UI elements
        /// when underlying data changes.</remarks>
        public event PropertyChangedEventHandler? PropertyChanged;
        /// <summary>
        /// Raises the PropertyChanged event to notify listeners that a property value has changed.
        /// </summary>
        /// <remarks>Use this method to implement the INotifyPropertyChanged interface in classes that
        /// support data binding. Calling this method with the correct property name ensures that UI elements or other
        /// listeners are updated when the property value changes.</remarks>
        /// <param name="name">The name of the property that changed. This value is optional and is automatically provided when called from
        /// a property setter.</param>
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// Initializes a new instance of the DashboardView class and sets up event handlers for login status changes.
        /// </summary>
        /// <remarks>This constructor sets the data context to the current instance and subscribes to
        /// login status events for both Kick and Twitch platforms. Event handlers are automatically unsubscribed when
        /// the view is unloaded to prevent memory leaks.</remarks>
        private DashboardView()
        {
            InitializeComponent();
            DataContext = this;

            TwitchConnection.LoginButtonText = "Checking...";
            TwitchConnection.ConnectionStatus = "Checking...";
            TwitchConnection.ConnectionColor = "Orange";
            KickConnection.LoginButtonText = "Checking...";
            KickConnection.ConnectionStatus = "Checking...";
            KickConnection.ConnectionColor = "Orange";

            MinerStatus = "Initializing";
            MinerStatusDetails = "Please wait...";

            _twitchService = new TwitchLoginService();
            _kickService = new KickLoginService();

            _dropsService = new DropsService();

            _twitchGqlService = new TwitchGqlService(_twitchWebView);

            // Subscribe to progress updates ===
            DropsInventoryManager.Instance.TwitchProgressChanged += (campPct, dropPct) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TwitchProgress.CampaignProgress = campPct;
                    TwitchProgress.DropProgress = dropPct;
                });
            };

            DropsInventoryManager.Instance.KickProgressChanged += (campPct, dropPct) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickProgress.CampaignProgress = campPct;
                    KickProgress.DropProgress = dropPct;
                });
            };

            DropsInventoryManager.Instance.MinerStatusChanged += status =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    switch (status)
                    {
                        case "Idle":
                            MinerStatus = "Idle";
                            MinerStatusDetails = "Waiting for drops";
                            break;
                        case "Starting":
                            MinerStatus = "Starting";
                            MinerStatusDetails = "Finding streams to mine";
                            break;
                        case "Evaluating":
                            MinerStatus = "Evaluating";
                            MinerStatusDetails = "Checking streams for drops eligibility";
                            break;
                        case "Mining":
                            MinerStatus = "Mining";
                            MinerStatusDetails = "Mining streams to earn drops";
                            break;
                    }
                });
            };

            DropsInventoryManager.Instance.KickChannelChanged += channel =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickProgress.MinedChannel = channel;
                });
            };

            DropsInventoryManager.Instance.TwitchChannelChanged += channel =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TwitchProgress.MinedChannel = channel;
                });
            };

            DropsInventoryManager.Instance.KickCampaignChanged += (campaign, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickProgress.CampaignName = campaign;
                    KickProgress.CampaignImageUrl = imageUrl ?? string.Empty;
                });
            };

            DropsInventoryManager.Instance.TwitchCampaignChanged += (campaign, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TwitchProgress.CampaignName = campaign;
                    TwitchProgress.CampaignImageUrl = imageUrl ?? string.Empty;
                });
            };

            DropsInventoryManager.Instance.KickDropChanged += (drop, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickProgress.DropName = drop;
                    KickProgress.DropImageUrl = imageUrl ?? string.Empty;
                });
            };

            DropsInventoryManager.Instance.TwitchDropChanged += (drop, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TwitchProgress.DropName = drop;
                    TwitchProgress.DropImageUrl = imageUrl ?? string.Empty;
                });
            };

            Loaded += async (s, e) => await OnLoadedAsync();
        }

        /// <summary>
        /// Asynchronously refreshes the list of active drops campaigns by retrieving the latest campaigns from the
        /// drops service.
        /// </summary>
        /// <remarks>After calling this method, the active campaigns list is updated to reflect the
        /// current set of active drops campaigns. Any previously stored campaigns are cleared before the new campaigns
        /// are added. This method should be awaited to ensure the refresh completes before accessing the updated
        /// campaigns.</remarks>
        /// <returns>A task that represents the asynchronous refresh operation.</returns>
        public async Task StartAutoRefreshDropsAsync()
        {
            ScheduleDropsLoad();

            _refreshTimer.Elapsed += async (s, e) => await Dispatcher.InvokeAsync(() => ScheduleDropsLoad());
            _refreshTimer.AutoReset = true; // Run forever
            _refreshTimer.Start();
        }
        /// <summary>
        /// Schedules a debounced background load of drops, ensuring that rapid consecutive triggers result in a single
        /// load operation after a delay.
        /// </summary>
        /// <remarks>This method prevents multiple load operations from being scheduled in quick
        /// succession by introducing a 2-second debounce period. It is thread-safe and intended to be called when a
        /// load should be triggered, but only after a period of inactivity. The actual load is performed asynchronously
        /// on the background dispatcher priority.</remarks>
        private void ScheduleDropsLoad()
        {
            // Block all loads until initial validation is done.
            if (!_initialValidationCompleted)
                return;

            lock (_loadTriggerLock)
            {
                if (_loadScheduled) return; // already scheduled
                _loadScheduled = true;
            }

            // Fire once, after 300ms of calm (debounced)
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(300); // absorb any rapid-fire triggers

                lock (_loadTriggerLock)
                {
                    _loadScheduled = false;
                }

                _ = LoadDropsAsync(); // safe - semaphore still protects concurrency
            }, DispatcherPriority.Background);
        }
        /// <summary>
        /// Asynchronously loads the list of active drops campaigns and updates the miner status properties to reflect
        /// the current loading state.
        /// </summary>
        /// <remarks>If a previous load operation is in progress, it will be canceled before starting a
        /// new one. Campaigns from each connected platform are added to the UI as soon as that platform
        /// responds, without waiting for all platforms to finish. This method should be called when the
        /// application needs to refresh the list of available campaigns.</remarks>
        /// <returns>A task that represents the asynchronous operation of loading active drops campaigns.</returns>
        private async Task LoadDropsAsync()
        {
            // Cancel any previous in-flight load
            _currentLoadCts?.Cancel();
            AppLogger.Info("Dashboard", "LoadDropsAsync invoked; previous load cancellation requested if active.");

            // Wait if another load is already running
            await _loadDropsSemaphore.WaitAsync();
            try
            {
                await DropsInventoryManager.Instance.PauseMiningAsync();
                AppLogger.Info("Dashboard", "Miner paused for campaign refresh.");

                using CancellationTokenSource cts = new CancellationTokenSource();
                _currentLoadCts = cts;

                if (_kickService.Status != ConnectionStatus.Connected && _twitchService.Status != ConnectionStatus.Connected)
                {
                    AppLogger.Warn("Dashboard", "Campaign load skipped: neither Twitch nor Kick is connected.");
                    MinerStatus = "Need login";
                    MinerStatusDetails = "Please login to Twitch and/or Kick to load campaigns.";
                    return;
                }

                MinerStatus = "Loading Campaigns";
                MinerStatusDetails = "Fetching latest drops...";

                _activeCampaigns.Clear();
                List<DropsCampaign> allCampaigns = [];

                // Campaigns are added to the UI as each platform responds
                await foreach (IReadOnlyList<DropsCampaign> batch in _dropsService.GetAllActiveCampaignsAsync(
                    _kickWebView, _kickService.Status,
                    _twitchWebView, _twitchService.Status,
                    _twitchGqlService, cts.Token))
                {
                    foreach (DropsCampaign c in batch.OrderBy(x => x.Platform).ThenBy(x => x.GameName))
                        _activeCampaigns.Add(c);

                    allCampaigns.AddRange(batch);
                }

                AppLogger.Info("Dashboard", $"Campaign load completed. totalCampaigns={allCampaigns.Count}, twitchStatus={_twitchService.Status}, kickStatus={_kickService.Status}");

                DropsInventoryManager.Instance.UpdateCampaigns(allCampaigns.AsReadOnly(), _twitchGqlService, startMining: false);

                MinerStatus = "Idle";
                MinerStatusDetails = $"{_activeCampaigns.Count} active campaigns loaded";
            }
            catch (OperationCanceledException ex) when (_currentLoadCts?.IsCancellationRequested == true)
            {
                // Expected when a new load cancels the old one
                AppLogger.Info("Dashboard", $"LoadDropsAsync canceled due to superseding refresh request. {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                MinerStatus = "Failed to load campaigns";
                MinerStatusDetails = ex.Message;
                AppLogger.Error("Dashboard", "LoadDropsAsync failed.", ex);
            }
            finally
            {
                _loadDropsSemaphore.Release();
                _currentLoadCts = null;
                await DropsInventoryManager.Instance.ResumeMiningAsync();
                AppLogger.Info("Dashboard", "Miner resumed after campaign refresh.");
            }
        }
        /// <summary>
        /// Asynchronously validates the current Twitch credentials using the associated web view and service.
        /// </summary>
        /// <returns>A task that represents the asynchronous validation operation.</returns>
        private async Task ValidateTwitchCredentialsAsync()
        {
            await _twitchService.ValidateCredentialsAsync(_twitchWebView);
        }
        /// <summary>
        /// Validates the current Kick service credentials asynchronously.
        /// </summary>
        /// <returns>A task that represents the asynchronous validation operation.</returns>
        private async Task ValidateKickCredentialsAsync()
        {
            await _kickService.ValidateCredentialsAsync(_kickWebView);
        }
        /// <summary>
        /// Asynchronously validates the credentials for external services if they are not already connected.
        /// </summary>
        /// <returns>A task that represents the asynchronous validation operation.</returns>
        private async Task ValidateCredentialsAsync()
        {
            if (_twitchService.Status != ConnectionStatus.Connected)
                await ValidateTwitchCredentialsAsync();

            if (_kickService.Status != ConnectionStatus.Connected)
                await ValidateKickCredentialsAsync();
        }

        #region Event Handlers
        /// <summary>
        /// Performs asynchronous validation of Twitch and Kick services when the component is loaded.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task OnLoadedAsync()
        {
            if (!_isInitialized)
            {
                _twitchService.StatusChanged += OnTwitchStatusChanged;
                _kickService.StatusChanged += OnKickStatusChanged;

                _isInitialized = true;

                await ValidateCredentialsAsync();

                _initialValidationCompleted = true;
                DropsInventoryManager.Instance.InitializeWebViews(_twitchWebView, _kickWebView);

                if (_twitchService.Status == ConnectionStatus.Connected)
                    await EnsureTwitchHelixAsync();

                // Load campaigns / drops
                await StartAutoRefreshDropsAsync();
            }
        }
        /// <summary>
        /// Handles changes to the Kick connection status and updates related UI elements accordingly.
        /// </summary>
        /// <remarks>This method updates the Kick connection status message, color indicator, and the
        /// enabled state of the Kick login button based on the provided status. It should be called whenever the
        /// connection status changes to ensure the UI reflects the current state.</remarks>
        /// <param name="status">The new connection status value indicating the current state of the Kick login process.</param>
        private void OnKickStatusChanged(ConnectionStatus status)
        {
            KickConnection.LoginButtonText = "Checking...";

            switch (status)
            {
                case ConnectionStatus.NotConnected:
                    KickConnection.ConnectionStatus = "Not Connected";
                    KickConnection.ConnectionColor = "Red";
                    KickConnection.LoginButtonText = "Login Kick";
                    KickConnection.IsLoginEnabled = true;
                    break;

                case ConnectionStatus.Validating:
                    KickConnection.ConnectionStatus = "Validating...";
                    KickConnection.ConnectionColor = "Orange";
                    KickConnection.IsLoginEnabled = false;
                    break;

                case ConnectionStatus.Connected:
                    KickConnection.ConnectionStatus = "Connected";
                    KickConnection.ConnectionColor = "Lime";
                    KickConnection.LoginButtonText = "Kick Logged in";
                    KickConnection.IsLoginEnabled = false;
                    ScheduleDropsLoad();
                    break;
                case ConnectionStatus.Connecting:
                    KickConnection.ConnectionStatus = "Connecting...";
                    KickConnection.ConnectionColor = "Yellow";
                    KickConnection.IsLoginEnabled = false;
                    break;
            }
        }
        /// <summary>
        /// Updates the Twitch connection status display and related UI elements based on the specified connection
        /// status.
        /// </summary>
        /// <param name="status">The current connection status of the Twitch login process. Determines how the UI reflects the connection
        /// state.</param>
        private void OnTwitchStatusChanged(ConnectionStatus status)
        {
            TwitchConnection.LoginButtonText = "Checking...";

            switch (status)
            {
                case ConnectionStatus.NotConnected:
                    TwitchConnection.ConnectionStatus = "Not Connected";
                    TwitchConnection.ConnectionColor = "Red";
                    TwitchConnection.LoginButtonText = "Login Twitch";
                    TwitchConnection.IsLoginEnabled = true;
                    break;

                case ConnectionStatus.Validating:
                    TwitchConnection.ConnectionStatus = "Validating...";
                    TwitchConnection.ConnectionColor = "Orange";
                    TwitchConnection.IsLoginEnabled = false;
                    break;

                case ConnectionStatus.Connected:
                    TwitchConnection.ConnectionStatus = "Connected";
                    TwitchConnection.ConnectionColor = "Lime";
                    TwitchConnection.LoginButtonText = "Twitch Logged in";
                    TwitchConnection.IsLoginEnabled = false;
                    _ = EnsureTwitchHelixAsync();
                    ScheduleDropsLoad();
                    break;
                case ConnectionStatus.Connecting:
                    TwitchConnection.ConnectionStatus = "Connecting...";
                    TwitchConnection.ConnectionColor = "Yellow";
                    TwitchConnection.IsLoginEnabled = false;
                    break;
            }
        }
        /// <summary>
        /// Handles the Click event for the Kick login button, displaying the login dialog and saving the session token
        /// if authentication is successful.
        /// </summary>
        /// <param name="sender">The source of the event, typically the Kick login button.</param>
        /// <param name="e">The event data associated with the Click event.</param>
        private void OnKickLoginClick(object sender, RoutedEventArgs e)
        {
            new KickLoginWindow().ShowDialog();
            _ = ValidateKickCredentialsAsync();
        }
        /// <summary>
        /// Handles the Click event for the Twitch login button, displaying the Twitch login window and initiating
        /// Twitch account validation.
        /// </summary>
        /// <param name="sender">The source of the event, typically the button that was clicked.</param>
        /// <param name="e">The event data associated with the click event.</param>
        private void OnTwitchLoginClick(object sender, RoutedEventArgs e)
        {
            new TwitchLoginWindow().ShowDialog();
            _ = ValidateTwitchCredentialsAsync();
        }

        /// <summary>
        /// Ensures Twitch Helix API access via device-code OAuth (separate from WebView drops login).
        /// </summary>
        private async Task EnsureTwitchHelixAsync()
        {
            if (TwitchHelixService.Instance.IsAuthenticated)
            {
                DropsInventoryManager.Instance.ScheduleTwitchStreamerMetadataRefresh();
                return;
            }

            bool authenticated = await TwitchHelixService.Instance.EnsureAuthenticatedAsync(async prompt =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    new TwitchHelixAuthWindow(prompt).ShowDialog();
                });
            });

            if (authenticated)
                DropsInventoryManager.Instance.ScheduleTwitchStreamerMetadataRefresh();
        }
        #endregion
    }
}