using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace UI.Controls
{
    public partial class PlatformLoginButton : UserControl
    {
        public static readonly RoutedEvent LoginClickEvent =
            EventManager.RegisterRoutedEvent(nameof(LoginClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PlatformLoginButton));

        public event RoutedEventHandler LoginClick
        {
            add => AddHandler(LoginClickEvent, value);
            remove => RemoveHandler(LoginClickEvent, value);
        }

        public PlatformLoginButton()
        {
            InitializeComponent();
        }

        private void OnLoginClick(object sender, RoutedEventArgs e)
        {
            RaiseEvent(new RoutedEventArgs(LoginClickEvent, this));
        }
    }
}