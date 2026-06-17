using System.Windows;
using UI.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace UI.Controls
{
    public partial class ConnectionStatusCard : UserControl
    {
        public static readonly RoutedEvent KickLoginClickEvent =
            EventManager.RegisterRoutedEvent(nameof(KickLoginClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ConnectionStatusCard));

        public static readonly RoutedEvent TwitchLoginClickEvent =
            EventManager.RegisterRoutedEvent(nameof(TwitchLoginClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ConnectionStatusCard));

        public event RoutedEventHandler KickLoginClick
        {
            add => AddHandler(KickLoginClickEvent, value);
            remove => RemoveHandler(KickLoginClickEvent, value);
        }

        public event RoutedEventHandler TwitchLoginClick
        {
            add => AddHandler(TwitchLoginClickEvent, value);
            remove => RemoveHandler(TwitchLoginClickEvent, value);
        }

        public static readonly DependencyProperty TwitchConnectionProperty =
            DependencyProperty.Register(nameof(TwitchConnection), typeof(PlatformConnectionState), typeof(ConnectionStatusCard));

        public static readonly DependencyProperty KickConnectionProperty =
            DependencyProperty.Register(nameof(KickConnection), typeof(PlatformConnectionState), typeof(ConnectionStatusCard));

        public PlatformConnectionState? TwitchConnection
        {
            get => (PlatformConnectionState?)GetValue(TwitchConnectionProperty);
            set => SetValue(TwitchConnectionProperty, value);
        }

        public PlatformConnectionState? KickConnection
        {
            get => (PlatformConnectionState?)GetValue(KickConnectionProperty);
            set => SetValue(KickConnectionProperty, value);
        }

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