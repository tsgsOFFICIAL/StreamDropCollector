using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Reflection.Metadata;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using UI.Views;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ICommand? JoinDiscordCommand { get; }
        public ICommand? ToggleWindowCommand { get; }
        public ICommand? CloseCommand { get; }
        public ICommand? OpenGithubCommand { get; }

        private bool _isTrayIconVisible;
        public bool IsTrayIconVisible
        {
            get => _isTrayIconVisible;
            set
            {
                _isTrayIconVisible = value;
                UpdateTrayIconVisibility();
            }
        }
        private UserControl? _currentPage;

        public MainWindow()
        {
            InitializeComponent();

            Loaded += (_, _) => StartActivationServer();

            // Initialize tray icon visibility
            IsTrayIconVisible = true;
            DataContext = this;

            // Initialize commands
            ToggleWindowCommand = new RelayCommand(o => ToggleWindowState());
            CloseCommand = new RelayCommand(o => CloseApplication());
            OpenGithubCommand = new RelayCommand(o => Core.Utility.LaunchWeb("https://github.com/tsgsOFFICIAL/StreamDropCollector"));
            JoinDiscordCommand = new RelayCommand(o => Core.Utility.LaunchWeb("https://discord.gg/Cddu5aJ"));

            // Event handler for double-click on TaskbarIcon
            MyNotifyIcon.TrayMouseDoubleClick += OnTrayIconDoubleClick;

            // Default page
            SwitchPage(DashboardView.Instance);
        }
        /// <summary>
        /// Replaces the current page displayed in the main content area with the specified page, applying a fade
        /// animation during the transition.
        /// </summary>
        /// <remarks>If the specified page is already displayed, no action is taken. The transition uses a
        /// fade-out animation for the current page followed by a fade-in animation for the new page to provide a smooth
        /// visual effect.</remarks>
        /// <param name="newPage">The new <see cref="UserControl"/> to display as the main content. Cannot be null.</param>
        private void SwitchPage(UserControl newPage)
        {
            // Same instance? Do nothing (prevents flicker + no new WebViews)
            if (ReferenceEquals(_currentPage, newPage))
                return;

            // Animate out -> in
            if (_currentPage is not null)
            {
                DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120));
                fadeOut.Completed += (_, __) =>
                {
                    MainContent.Content = _currentPage = newPage;
                    DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
                    newPage.BeginAnimation(OpacityProperty, fadeIn);
                };

                _currentPage.BeginAnimation(OpacityProperty, fadeOut);
            }
            else
            {
                // First load
                _currentPage = newPage;
                MainContent.Content = newPage;

                newPage.Opacity = 0;
                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));

                newPage.BeginAnimation(OpacityProperty, fadeIn);
            }
        }
        /// <summary>
        /// Handles the Click event for sidebar navigation buttons, switching the displayed page based on the button's
        /// tag.
        /// </summary>
        /// <remarks>The method expects the sender to be a Button whose Tag property is set to a
        /// recognized page identifier (such as "Dashboard", "Inventory", "Settings", or "Help"). If the Tag does not
        /// match a known page, no action is taken.</remarks>
        /// <param name="sender">The source of the event, expected to be a Button with its Tag property indicating the target page.</param>
        /// <param name="e">The event data associated with the button click.</param>
        private void SidebarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
                return;

            UserControl? targetPage = (btn.Tag?.ToString()) switch
            {
                "Dashboard" => _currentPage is DashboardView ? _currentPage : DashboardView.Instance,
                "Inventory" => _currentPage is InventoryView ? _currentPage : InventoryView.Instance,
                "Settings" => _currentPage is SettingsView ? _currentPage : SettingsView.Instance,
                "Help" => _currentPage is HelpView ? _currentPage : HelpView.Instance,
                _ => null
            };

            if (targetPage is not null)
                SwitchPage(targetPage);
        }
        /// <summary>
        /// Closes the application
        /// </summary>
        private void CloseApplication()
        {
            Close();
            Environment.Exit(0);
        }
        /// <summary>
        /// Updates the visibility of the tray icon to reflect the current value of the IsTrayIconVisible property.
        /// </summary>
        /// <remarks>Call this method after changing the IsTrayIconVisible property to ensure the tray
        /// icon's visibility is updated accordingly.</remarks>
        private void UpdateTrayIconVisibility()
        {
            // Show or hide the tray icon based on IsTrayIconVisible
            MyNotifyIcon.Visibility =
                IsTrayIconVisible ? Visibility.Visible
                : Visibility.Collapsed;
        }
        /// <summary>
        /// Toggles the window state between minimized and normal, showing or hiding the window as appropriate.
        /// </summary>
        /// <remarks>If the window is currently minimized, this method restores it to the normal state and
        /// makes it visible. If the window is not minimized, it minimizes and hides the window. This method is
        /// typically used to implement minimize-to-tray or similar window management behaviors.</remarks>
        private void ToggleWindowState()
        {
            if (WindowState == WindowState.Minimized)
            {
                Show();
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Minimized;
                Hide();
            }
        }
        /// <summary>
        /// Displays a notification with the specified title and message for a limited duration.
        /// </summary>
        /// <param name="title">The title text to display in the notification. Cannot be null or empty.</param>
        /// <param name="message">The message content to display in the notification. Cannot be null or empty.</param>
        /// <param name="timeoutSeconds">The duration, in seconds, for which the notification is visible. Must be greater than zero. The default is 1
        /// second.</param>
        private static void ShowNotification(string title, string message, double timeoutSeconds = 1)
        {
            Core.Managers.NotificationManager.ShowNotification(title, message, timeoutSeconds);
        }
        /// <summary>
        /// Brings the window to the foreground, restoring it if minimized and ensuring it is visible and active.
        /// </summary>
        /// <remarks>This method restores the window from a minimized state if necessary, makes it
        /// visible, and activates it. It also adjusts the window's Z-order to ensure it appears above other windows,
        /// addressing platform-specific behavior on Windows.</remarks>
        private void BringToFront()
        {
            // If hidden to tray
            if (!IsVisible)
            {
                Show();
                ShowInTaskbar = true;
            }

            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            // This is essential
            Activate();

            // Windows focus-stealing workaround (safe & common)
            Topmost = true;
            Topmost = false;
        }

        #region Event Handlers
        /// <summary>
        /// Starts an asynchronous server that listens for activation messages on a named pipe and brings the
        /// application window to the foreground when an activation request is received.
        /// </summary>
        /// <remarks>This method runs the activation server in a background task and does not block the
        /// calling thread. The server continuously waits for incoming connections and responds to activation messages.
        /// This is typically used to allow external processes to activate the application window. The method should be
        /// called once during application startup to enable activation functionality.</remarks>
        private void StartActivationServer()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    using NamedPipeServerStream server = new NamedPipeServerStream(
                        App.PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync();

                    using StreamReader reader = new StreamReader(server);
                    string? message = await reader.ReadLineAsync();

                    if (message == "ACTIVATE")
                    {
                        Dispatcher.Invoke(BringToFront);
                    }
                }
            });
        }
        /// <summary>
        /// Event handler for when the window header is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnWindowHeaderMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click: Maximize or restore the window
                WindowState = (WindowState == WindowState.Normal)
                    ? WindowState.Maximized
                    : WindowState.Normal;
            }
            else
            {
                // Single click: Drag the window
                if (WindowState == WindowState.Maximized)
                {
                    // Get the absolute mouse position *before* restoring
                    System.Windows.Point mousePosition = PointToScreen(e.GetPosition(this));

                    // Get the active screen where the mouse is located
                    Screen activeScreen = Screen.FromPoint(new System.Drawing.Point((int)mousePosition.X, (int)mousePosition.Y));
                    Rectangle screenBounds = activeScreen.WorkingArea; // Use WorkingArea to exclude taskbar

                    // Calculate cursor position as percentage of the screen's working area
                    double percentX = (mousePosition.X - screenBounds.X) / screenBounds.Width;
                    double percentY = (mousePosition.Y - screenBounds.Y) / screenBounds.Height;

                    // Restore the window
                    WindowState = WindowState.Normal;

                    // Calculate new position based on restored size
                    double newWidth = RestoreBounds.Width;
                    double newHeight = RestoreBounds.Height;

                    // Adjust Left and Top to keep cursor at the same relative percentage
                    Left = mousePosition.X - (percentX * newWidth);
                    Top = mousePosition.Y - (percentY * newHeight);

                    // Clamp to ensure the window stays within the screen bounds
                    Left = Math.Max(screenBounds.X, Math.Min(Left, screenBounds.Right - newWidth));
                    Top = Math.Max(screenBounds.Y, Math.Min(Top, screenBounds.Bottom - newHeight));
                }

                DragMove();
            }
        }
        /// <summary>
        /// Event handler for when the minimize button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnMinimizeButtonClicked(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
        /// <summary>
        /// Event handler for when the maximize button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        public void OnMaximizeButtonClicked(object sender, RoutedEventArgs e)
        {
            if (WindowState.Equals(WindowState.Maximized))
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }
        /// <summary>
        /// Event handler for when the close button is clicked
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
        {
            CloseApplication();
        }
        /// <summary>
        /// Handles the double-click event on the tray icon to display and restore the window.
        /// </summary>
        /// <param name="sender">The source of the event, typically the tray icon control.</param>
        /// <param name="e">The event data associated with the double-click action.</param>
        private void OnTrayIconDoubleClick(object sender, RoutedEventArgs e)
        {
            BringToFront();
        }
        /// <summary>
        /// Handles changes to the window's state and updates the user interface and notification area accordingly.
        /// </summary>
        /// <remarks>This method updates the context menu and notification icon to reflect the current
        /// window state. When the window is minimized, it hides the window and displays a notification. When restored
        /// or maximized, it adjusts the window padding and restores the context menu text. Override this method to
        /// customize window state change behavior in derived classes.</remarks>
        /// <param name="e">An <see cref="System.EventArgs"/> that contains the event data.</param>
        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            // Find context menu items and update texts based on WindowState
            System.Windows.Controls.ContextMenu menu = MyNotifyIcon.ContextMenu;
            System.Windows.Controls.MenuItem toggleMenuItem = MinimizeAndRestore;

            if (WindowState == WindowState.Maximized)
                RootBorder.Padding = new Thickness(7); // Add safe padding (7px is deafult Windows border size)
            else
                RootBorder.Padding = new Thickness(0);

            if (WindowState == WindowState.Minimized)
            {
                string toolTipText = "Stream Drop Collector is minimized";

                Hide();
                ShowInTaskbar = false;
                ShowNotification("Stream Drop Collector", toolTipText);
                toggleMenuItem.Header = "Restore";
                MyNotifyIcon.ToolTipText = toolTipText;
            }
            else
            {
                ShowInTaskbar = true;
                toggleMenuItem.Header = "Minimize";
                MyNotifyIcon.ToolTipText = "Stream Drop Collector by tsgsOFFICIAL";
            }
        }
        #endregion
    }
}