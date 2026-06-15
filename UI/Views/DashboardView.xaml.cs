using UserControl = System.Windows.Controls.UserControl;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.ComponentModel;
using System.Windows;
using Core.Logging;
using Core.Managers;
using Core.Services;
using Core.Models;
using Core.Enums;

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
        public static DashboardView Instance => _instance.Value;

        // Services
        private readonly TwitchLoginService _twitchService = new();
        private readonly KickLoginService _kickService = new();
        private readonly DropsService _dropsService;

        // Observable collection for UI binding
        private readonly ObservableCollection<DropsCampaign> _activeCampaigns = new();
        public IReadOnlyCollection<DropsCampaign> ActiveCampaigns => _activeCampaigns;

        // UI Properties
        private string _twitchConnectionStatus = "Not Connected";
        public string TwitchConnectionStatus
        {
            get => _twitchConnectionStatus;
            set
            {
                _twitchConnectionStatus = value;
                OnPropertyChanged();
            }
        }
        private string _twitchConnectionColor = "Red";
        public string TwitchConnectionColor
        {
            get => _twitchConnectionColor;
            set
            {
                _twitchConnectionColor = value;
                OnPropertyChanged();
            }
        }
        private string _twitchLoginButtonText = "Login Twitch";
        public string TwitchLoginButtonText
        {
            get => _twitchLoginButtonText;
            set
            {
                _twitchLoginButtonText = value;
                OnPropertyChanged();
            }
        }
        private string _kickConnectionStatus = "Not Connected";
        public string KickConnectionStatus
        {
            get => _kickConnectionStatus;
            set
            {
                _kickConnectionStatus = value;
                OnPropertyChanged();
            }
        }
        private string _kickConnectionColor = "Red";
        public string KickConnectionColor
        {
            get => _kickConnectionColor;
            set
            {
                _kickConnectionColor = value;
                OnPropertyChanged();
            }
        }
        private string _kickLoginButtonText = "Login Kick";
        public string KickLoginButtonText
        {
            get => _kickLoginButtonText;
            set
            {
                _kickLoginButtonText = value;
                OnPropertyChanged();
            }
        }
        private string _minerStatus = "Idle";
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
        public string MinerStatusDetails
        {
            get => _minerStatusDetails;
            set
            {
                _minerStatusDetails = value;
                OnPropertyChanged();
            }
        }
        private byte _twitchCampaignProgress = 0;
        public byte TwitchCampaignProgress
        {
            get => _twitchCampaignProgress;
            set
            {
                _twitchCampaignProgress = value;
                OnPropertyChanged();
            }
        }
        private byte _twitchDropProgress = 0;
        public byte TwitchDropProgress
        {
            get => _twitchDropProgress;
            set
            {
                _twitchDropProgress = value;
                OnPropertyChanged();
            }
        }
        private byte _kickCampaignProgress = 0;
        public byte KickCampaignProgress
        {
            get => _kickCampaignProgress;
            set
            {
                _kickCampaignProgress = value;
                OnPropertyChanged();
            }
        }
        private byte _kickDropProgress = 0;
        public byte KickDropProgress
        {
            get => _kickDropProgress;
            set
            {
                _kickDropProgress = value;
                OnPropertyChanged();
            }
        }
        private string _twitchWatchedChannel = string.Empty;
        public string TwitchWatchedChannel
        {
            get => _twitchWatchedChannel;
            set
            {
                _twitchWatchedChannel = value;
                OnPropertyChanged();
            }
        }
        private string _kickWatchedChannel = string.Empty;
        public string KickWatchedChannel
        {
            get => _kickWatchedChannel;
            set
            {
                _kickWatchedChannel = value;
                OnPropertyChanged();
            }
        }
        private string _twitchCampaignName = string.Empty;
        public string TwitchCampaignName
        {
            get => _twitchCampaignName;
            set
            {
                _twitchCampaignName = value;
                OnPropertyChanged();
            }
        }
        private string _kickCampaignName = string.Empty;
        public string KickCampaignName
        {
            get => _kickCampaignName;
            set
            {
                _kickCampaignName = value;
                OnPropertyChanged();
            }
        }
        private string _twitchCampaignImageUrl = string.Empty;
        public string TwitchCampaignImageUrl
        {
            get => _twitchCampaignImageUrl;
            set
            {
                _twitchCampaignImageUrl = value;
                OnPropertyChanged();
            }
        }
        private string _kickCampaignImageUrl = string.Empty;
        public string KickCampaignImageUrl
        {
            get => _kickCampaignImageUrl;
            set
            {
                _kickCampaignImageUrl = value;
                OnPropertyChanged();
            }
        }
        private string _twitchDropName = string.Empty;
        public string TwitchDropName
        {
            get => _twitchDropName;
            set
            {
                _twitchDropName = value;
                OnPropertyChanged();
            }
        }
        private string _twitchDropImageUrl = string.Empty;
        public string TwitchDropImageUrl
        {
            get => _twitchDropImageUrl;
            set
            {
                _twitchDropImageUrl = value;
                OnPropertyChanged();
            }
        }
        private string _kickDropName = string.Empty;
        public string KickDropName
        {
            get => _kickDropName;
            set
            {
                _kickDropName = value;
                OnPropertyChanged();
            }
        }
        private string _kickDropImageUrl = string.Empty;
        public string KickDropImageUrl
        {
            get => _kickDropImageUrl;
            set
            {
                _kickDropImageUrl = value;
                OnPropertyChanged();
            }
        }

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
                    TwitchCampaignProgress = campPct;
                    TwitchDropProgress = dropPct;
                });
            };

            DropsInventoryManager.Instance.KickProgressChanged += (campPct, dropPct) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickCampaignProgress = campPct;
                    KickDropProgress = dropPct;
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
                            MinerStatusDetails = "Finding stream(s) to watch";
                            break;
                        case "Evaluating":
                            MinerStatus = "Evaluating";
                            MinerStatusDetails = "Checking stream(s) for drops eligibility";
                            break;
                        case "Mining":
                            MinerStatus = "Mining";
                            MinerStatusDetails = "Watching stream(s) to earn drops";
                            break;
                    }
                });
            };

            DropsInventoryManager.Instance.KickChannelChanged += channel =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickWatchedChannel = channel;
                });
            };

            DropsInventoryManager.Instance.TwitchChannelChanged += channel =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TwitchWatchedChannel = channel;
                });
            };

            DropsInventoryManager.Instance.KickCampaignChanged += (campaign, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickCampaignName = campaign;
                    KickCampaignImageUrl = imageUrl ?? string.Empty;
                });
            };

            DropsInventoryManager.Instance.TwitchCampaignChanged += (campaign, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TwitchCampaignName = campaign;
                    TwitchCampaignImageUrl = imageUrl ?? string.Empty;
                });
            };

            DropsInventoryManager.Instance.KickDropChanged += (drop, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KickDropName = drop;
                    KickDropImageUrl = imageUrl ?? string.Empty;
                });
            };

            DropsInventoryManager.Instance.TwitchDropChanged += (drop, imageUrl) =>
            {
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TwitchDropName = drop;
                    TwitchDropImageUrl = imageUrl ?? string.Empty;
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
                await DropsInventoryManager.Instance.PauseWatchingAsync();
                AppLogger.Info("Dashboard", "Watcher paused for campaign refresh.");

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

                DropsInventoryManager.Instance.UpdateCampaigns(allCampaigns.AsReadOnly(), _twitchGqlService, startWatching: false);

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
                await DropsInventoryManager.Instance.ResumeWatchingAsync();
                AppLogger.Info("Dashboard", "Watcher resumed after campaign refresh.");
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
            KickLoginButtonText = "Checking...";

            switch (status)
            {
                case ConnectionStatus.NotConnected:
                    KickConnectionStatus = "Not Connected";
                    KickConnectionColor = "Red";
                    KickLoginButtonText = "Login Kick";
                    KickLoginButton.IsEnabled = true;
                    break;

                case ConnectionStatus.Validating:
                    KickConnectionStatus = "Validating...";
                    KickConnectionColor = "Orange";
                    KickLoginButton.IsEnabled = false;
                    break;

                case ConnectionStatus.Connected:
                    KickConnectionStatus = "Connected";
                    KickConnectionColor = "Lime";
                    KickLoginButtonText = "Kick Logged in";
                    KickLoginButton.IsEnabled = false; // disable when already logged in
                    ScheduleDropsLoad();
                    break;
                case ConnectionStatus.Connecting:
                    KickConnectionStatus = "Connecting...";
                    KickConnectionColor = "Yellow";
                    KickLoginButton.IsEnabled = false;
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
            TwitchLoginButtonText = "Checking...";

            switch (status)
            {
                case ConnectionStatus.NotConnected:
                    TwitchConnectionStatus = "Not Connected";
                    TwitchConnectionColor = "Red";
                    TwitchLoginButtonText = "Login Twitch";
                    TwitchLoginButton.IsEnabled = true;
                    break;

                case ConnectionStatus.Validating:
                    TwitchConnectionStatus = "Validating...";
                    TwitchConnectionColor = "Orange";
                    TwitchLoginButton.IsEnabled = false;
                    break;

                case ConnectionStatus.Connected:
                    TwitchConnectionStatus = "Connected";
                    TwitchConnectionColor = "Lime";
                    TwitchLoginButtonText = "Twitch Logged in";
                    TwitchLoginButton.IsEnabled = false; // disable when already logged in
                    ScheduleDropsLoad();
                    break;
                case ConnectionStatus.Connecting:
                    TwitchConnectionStatus = "Connecting...";
                    TwitchConnectionColor = "Yellow";
                    TwitchLoginButton.IsEnabled = false;
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
        #endregion
    }
}