using UserControl = System.Windows.Controls.UserControl;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Windows;
using Core.Managers;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for DashboardView.xaml
    /// </summary>
    public partial class DashboardView : UserControl, INotifyPropertyChanged
    {
        /// <summary>
        /// Initializes a new instance of the DashboardView class and sets up event handlers for login status changes.
        /// </summary>
        /// <remarks>This constructor sets the data context to the current instance and subscribes to
        /// login status events for both Kick and Twitch platforms. Event handlers are automatically unsubscribed when
        /// the view is unloaded to prevent memory leaks.</remarks>
        public DashboardView()
        {
            InitializeComponent();
            DataContext = this;
            LoginManager.KickStatusChanged += OnKickStatusChanged;
            LoginManager.TwitchStatusChanged += OnTwitchStatusChanged;
            Loaded += DashboardView_Loaded;
            Unloaded += (s, e) =>
            {
                LoginManager.KickStatusChanged -= OnKickStatusChanged;
                LoginManager.TwitchStatusChanged -= OnTwitchStatusChanged;
            };
        }

        private string _twitchConnectionStatus = "Not Connected";
        public string TwitchConnectionStatus
        {
            get => $"Twitch: {_twitchConnectionStatus}";
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
            get => $"Kick: {_kickConnectionStatus}";
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
        /// Handles changes to the Kick connection status and updates related UI elements accordingly.
        /// </summary>
        /// <remarks>This method updates the Kick connection status message, color indicator, and the
        /// enabled state of the Kick login button based on the provided status. It should be called whenever the
        /// connection status changes to ensure the UI reflects the current state.</remarks>
        /// <param name="status">The new connection status value indicating the current state of the Kick login process.</param>
        private void OnKickStatusChanged(LoginManager.ConnectionStatus status)
        {
            switch (status)
            {
                case LoginManager.ConnectionStatus.NotConnected:
                    KickConnectionStatus = "Not Connected";
                    KickConnectionColor = "Red";
                    KickLoginButton.IsEnabled = true;
                    break;

                case LoginManager.ConnectionStatus.Validating:
                    KickConnectionStatus = "Validating...";
                    KickConnectionColor = "Orange";
                    KickLoginButton.IsEnabled = false;
                    break;

                case LoginManager.ConnectionStatus.Connected:
                    KickConnectionStatus = "Connected";
                    KickConnectionColor = "Lime";
                    KickLoginButton.IsEnabled = false; // disable when already logged in
                    break;
            }
        }
        /// <summary>
        /// Updates the Twitch connection status display and related UI elements based on the specified connection
        /// status.
        /// </summary>
        /// <param name="status">The current connection status of the Twitch login process. Determines how the UI reflects the connection
        /// state.</param>
        private void OnTwitchStatusChanged(LoginManager.ConnectionStatus status)
        {
            switch (status)
            {
                case LoginManager.ConnectionStatus.NotConnected:
                    TwitchConnectionStatus = "Not Connected";
                    TwitchConnectionColor = "Red";
                    TwitchLoginButton.IsEnabled = true;
                    break;

                case LoginManager.ConnectionStatus.Validating:
                    TwitchConnectionStatus = "Validating...";
                    TwitchConnectionColor = "Orange";
                    TwitchLoginButton.IsEnabled = false;
                    break;

                case LoginManager.ConnectionStatus.Connected:
                    TwitchConnectionStatus = "Connected";
                    TwitchConnectionColor = "Lime";
                    TwitchLoginButton.IsEnabled = false; // disable when already logged in
                    break;
            }
        }
        /// <summary>
        /// Handles the Loaded event of the DashboardView control.
        /// </summary>
        /// <param name="sender">The source of the event, typically the DashboardView control.</param>
        /// <param name="e">The event data associated with the Loaded event.</param>
        private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            VerifyKickAccountIsConnected();
            VerifyTwitchAccountIsConnected();
        }
        /// <summary>
        /// Handles the Click event for the Kick login button, displaying the login dialog and saving the session token
        /// if authentication is successful.
        /// </summary>
        /// <param name="sender">The source of the event, typically the Kick login button.</param>
        /// <param name="e">The event data associated with the Click event.</param>
        private void LoginKick_Click(object sender, RoutedEventArgs e)
        {
            KickLoginWindow login = new KickLoginWindow();
            bool? result = login.ShowDialog();

            if (result == true && login.SessionToken != null)
                LoginManager.SaveKickToken(login.SessionToken);

            VerifyKickAccountIsConnected();
        }
        /// <summary>
        /// Handles the Click event for the Twitch login button, prompting the user to log in and saving the session
        /// token if authentication is successful.
        /// </summary>
        /// <param name="sender">The source of the event, typically the Twitch login button.</param>
        /// <param name="e">The event data associated with the Click event.</param>
        private void LoginTwitch_Click(object sender, RoutedEventArgs e)
        {
            TwitchLoginWindow login = new TwitchLoginWindow();
            bool? result = login.ShowDialog();

            if (result == true && login.AuthToken != null && login.UniqueId != null)
                LoginManager.SaveTwitchTokens([login.AuthToken, login.UniqueId]);

            VerifyTwitchAccountIsConnected();
        }
        /// <summary>
        /// Verifies that the Kick account is currently connected and authenticated.
        /// </summary>
        /// <remarks>This method performs an asynchronous check to ensure the Kick account's
        /// authentication token is valid and the account is connected. If the account is not connected, the method may
        /// trigger re-authentication or other corrective actions as defined by the underlying login manager. This
        /// method is intended for internal use and should not be awaited or called from user interface threads, as it
        /// is an async void method.</remarks>
        private static async void VerifyKickAccountIsConnected()
        {
            HiddenWebViewHost host = new HiddenWebViewHost();
            await host.EnsureInitializedAsync();

            await LoginManager.ValidateKickTokenAsync(host);
        }
        /// <summary>
        /// Verifies that a Twitch account is connected and that authentication tokens are valid.
        /// </summary>
        /// <remarks>This method initializes a hidden web view host and validates the current Twitch
        /// authentication tokens. It is intended for internal use and should not be called directly from user
        /// code.</remarks>
        private static async void VerifyTwitchAccountIsConnected()
        {
            HiddenWebViewHost host = new HiddenWebViewHost();
            await host.EnsureInitializedAsync();
            await LoginManager.ValidateTwitchTokensAsync(host);
        }
    }
}