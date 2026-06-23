using System.Windows;
using UI.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace UI.Controls
{
    /// <summary>
    /// Card showing Twitch and Kick connection state with platform login actions.
    /// </summary>
    public partial class ConnectionStatusCard : UserControl
    {
        /// <summary>Identifies the <see cref="KickLoginClick"/> routed event.</summary>
        public static readonly RoutedEvent KickLoginClickEvent =
            EventManager.RegisterRoutedEvent(nameof(KickLoginClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ConnectionStatusCard));

        /// <summary>Identifies the <see cref="TwitchLoginClick"/> routed event.</summary>
        public static readonly RoutedEvent TwitchLoginClickEvent =
            EventManager.RegisterRoutedEvent(nameof(TwitchLoginClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ConnectionStatusCard));

        /// <summary>Raised when the user requests Kick login.</summary>
        public event RoutedEventHandler KickLoginClick
        {
            add => AddHandler(KickLoginClickEvent, value);
            remove => RemoveHandler(KickLoginClickEvent, value);
        }

        /// <summary>Raised when the user requests Twitch login.</summary>
        public event RoutedEventHandler TwitchLoginClick
        {
            add => AddHandler(TwitchLoginClickEvent, value);
            remove => RemoveHandler(TwitchLoginClickEvent, value);
        }

        /// <summary>Identifies the <see cref="TwitchConnection"/> dependency property.</summary>
        public static readonly DependencyProperty TwitchConnectionProperty =
            DependencyProperty.Register(nameof(TwitchConnection), typeof(PlatformConnectionState), typeof(ConnectionStatusCard));

        /// <summary>Identifies the <see cref="KickConnection"/> dependency property.</summary>
        public static readonly DependencyProperty KickConnectionProperty =
            DependencyProperty.Register(nameof(KickConnection), typeof(PlatformConnectionState), typeof(ConnectionStatusCard));

        /// <summary>Bindable Twitch connection and login state.</summary>
        public PlatformConnectionState? TwitchConnection
        {
            get => (PlatformConnectionState?)GetValue(TwitchConnectionProperty);
            set => SetValue(TwitchConnectionProperty, value);
        }

        /// <summary>Bindable Kick connection and login state.</summary>
        public PlatformConnectionState? KickConnection
        {
            get => (PlatformConnectionState?)GetValue(KickConnectionProperty);
            set => SetValue(KickConnectionProperty, value);
        }

        /// <summary>Initializes the connection status card.</summary>
        public ConnectionStatusCard()
        {
            InitializeComponent();
        }

        private void OnKickLoginClick(object sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(KickLoginClickEvent, this));

        private void OnTwitchLoginClick(object sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(TwitchLoginClickEvent, this));
    }
}