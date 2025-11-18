using Core.Managers;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for DashboardView.xaml
    /// </summary>
    public partial class DashboardView : UserControl, INotifyPropertyChanged
    {
        public DashboardView()
        {
            InitializeComponent();
            DataContext = this;
            LoginManager.KickStatusChanged += OnKickStatusChanged;
            //LoginManager.TwitchStatusChanged += OnTwitchStatusChanged; // TODO
            Loaded += DashboardView_Loaded;
            Unloaded += (s, e) =>
            {
                LoginManager.KickStatusChanged -= OnKickStatusChanged;
                //LoginManager.TwitchStatusChanged -= OnTwitchStatusChanged; // TODO
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void OnKickStatusChanged(LoginManager.ConnectionStatus status)
        {
            switch (status)
            {
                case LoginManager.ConnectionStatus.NotConnected:
                    KickConnectionStatus = "Kick: Not Connected";
                    KickConnectionColor = "Red";
                    KickLoginButton.IsEnabled = true;
                    break;

                case LoginManager.ConnectionStatus.Validating:
                    KickConnectionStatus = "Kick: Validating...";
                    KickConnectionColor = "Orange";
                    KickLoginButton.IsEnabled = false;
                    break;

                case LoginManager.ConnectionStatus.Connected:
                    KickConnectionStatus = "Kick: Connected";
                    KickConnectionColor = "Lime";
                    KickLoginButton.IsEnabled = false; // disable when already logged in
                    break;
            }
        }

        private void OnTwitchStatusChanged(LoginManager.ConnectionStatus status)
        {
            switch (status)
            {
                case LoginManager.ConnectionStatus.NotConnected:
                    TwitchConnectionStatus = "Twitch: Not Connected";
                    TwitchConnectionColor = "Red";
                    TwitchLoginButton.IsEnabled = true;
                    break;

                case LoginManager.ConnectionStatus.Validating:
                    TwitchConnectionStatus = "Twitch: Validating...";
                    TwitchConnectionColor = "Orange";
                    TwitchLoginButton.IsEnabled = false;
                    break;

                case LoginManager.ConnectionStatus.Connected:
                    TwitchConnectionStatus = "Twitch: Connected";
                    TwitchConnectionColor = "Lime";
                    TwitchLoginButton.IsEnabled = false;
                    break;
            }
        }

        private async void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            VerifyKickAccountIsConnected();
        }

        private void LoginKick_Click(object sender, RoutedEventArgs e)
        {
            KickLoginWindow login = new KickLoginWindow();
            bool? result = login.ShowDialog();

            if (result == true && login.SessionToken != null)
                LoginManager.SaveKickToken(login.SessionToken);

            VerifyKickAccountIsConnected();
        }

        private void LoginTwitch_Click(object sender, RoutedEventArgs e)
        {
            //var login = new TwitchLoginWindow();
            //if (login.ShowDialog() == true)
            //{
            //    // Update LoginManager / status here
            //    LoginManager.TwitchStatusChanged?.Invoke(LoginManager.ConnectionStatus.Connected);
            //}
        }

        private async void VerifyKickAccountIsConnected()
        {
            HiddenWebViewHost host = new HiddenWebViewHost();
            await host.EnsureInitializedAsync();

            await LoginManager.ValidateKickTokenAsync(host);
        }
    }
}