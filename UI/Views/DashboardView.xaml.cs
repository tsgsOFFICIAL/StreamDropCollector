using UserControl = System.Windows.Controls.UserControl;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
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

        private HiddenWebViewHost _twitchWebView = new();
        private HiddenWebViewHost _kickWebView = new();

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

            _twitchService = new TwitchLoginService();
            _kickService = new KickLoginService();

            _dropsService = new DropsService();

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
            await LoadDropsAsync();

            _refreshTimer.Elapsed += async (s, e) => await Dispatcher.InvokeAsync(async () => await LoadDropsAsync());
            _refreshTimer.AutoReset = true; // Run forever
            _refreshTimer.Start();
        }
        /// <summary>
        /// Asynchronously loads the list of active drops campaigns and updates the internal campaign collection.
        /// </summary>
        /// <returns>A task that represents the asynchronous load operation.</returns>
        private async Task LoadDropsAsync()
        {
            if (_twitchService.Status != ConnectionStatus.Connected && _kickService.Status != ConnectionStatus.Connected)
            {
                MinerStatus = "Idle";
                return;
            }

            MinerStatus = "Loading Campaigns";

            _activeCampaigns.Clear();
            IReadOnlyList<DropsCampaign> allCampaigns;

            allCampaigns = await _dropsService.GetAllActiveCampaignsAsync(_kickWebView, _twitchWebView);

            foreach (DropsCampaign? c in allCampaigns.OrderBy(x => x.GameName))
                _activeCampaigns.Add(c);

            MinerStatus = "Idle";
        }
        /// <summary>
        /// Asynchronously validates the current Twitch credentials using the associated web view and service.
        /// </summary>
        /// <returns>A task that represents the asynchronous validation operation.</returns>
        private async Task ValidateTwitchCredentialsAsync()
        {
            MinerStatus = "Validating Twitch";
            await _twitchService.ValidateCredentialsAsync(_twitchWebView);
        }
        /// <summary>
        /// Validates the current Kick service credentials asynchronously.
        /// </summary>
        /// <returns>A task that represents the asynchronous validation operation.</returns>
        private async Task ValidateKickCredentialsAsync()
        {
            MinerStatus = "Validating Kick";
            await _kickService.ValidateCredentialsAsync(_kickWebView);
        }
        private async Task ValidateCredentialsAsync()
        {
            if (_twitchService.Status != ConnectionStatus.Connected)
                await ValidateTwitchCredentialsAsync();

            if (_kickService.Status != ConnectionStatus.Connected)
                await ValidateKickCredentialsAsync();

            MinerStatus = "Idle";
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
            switch (status)
            {
                case ConnectionStatus.NotConnected:
                    KickConnectionStatus = "Not Connected";
                    KickConnectionColor = "Red";
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
                    KickLoginButton.IsEnabled = false; // disable when already logged in
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
            switch (status)
            {
                case ConnectionStatus.NotConnected:
                    TwitchConnectionStatus = "Not Connected";
                    TwitchConnectionColor = "Red";
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
                    TwitchLoginButton.IsEnabled = false; // disable when already logged in
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