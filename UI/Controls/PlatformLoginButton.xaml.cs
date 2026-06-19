using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace UI.Controls
{
    /// <summary>
    /// Platform-branded login button that raises a bubbling <see cref="LoginClick"/> routed event.
    /// </summary>
    public partial class PlatformLoginButton : UserControl
    {
        /// <summary>Identifies the <see cref="LoginClick"/> routed event.</summary>
        public static readonly RoutedEvent LoginClickEvent =
            EventManager.RegisterRoutedEvent(nameof(LoginClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(PlatformLoginButton));

        /// <summary>Raised when the user clicks the login button.</summary>
        public event RoutedEventHandler LoginClick
        {
            add => AddHandler(LoginClickEvent, value);
            remove => RemoveHandler(LoginClickEvent, value);
        }

        /// <summary>Initializes the platform login button.</summary>
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