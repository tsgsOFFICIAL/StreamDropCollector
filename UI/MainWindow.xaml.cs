using UserControl = System.Windows.Controls.UserControl;
using Button = System.Windows.Controls.Button;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.IO.MemoryMappedFiles;
using System.Windows.Input;
using System.Diagnostics;
using System.Windows;
using System.IO;
using UI.Views;

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
        private string _currentPage = string.Empty;

        public MainWindow()
        {
            InitializeComponent();

            // Subscribe to th event of another instance trying to launch
            Task.Run(() => OnAnotherInstanceStarted());

            // Initialize tray icon visibility
            IsTrayIconVisible = true;
            DataContext = this;

            // Initialize commands
            ToggleWindowCommand = new RelayCommand(o => ToggleWindowState());
            CloseCommand = new RelayCommand(o => CloseApplication());
            OpenGithubCommand = new RelayCommand(o => Core.Utility.LaunchWeb("https://github.com/tsgsOFFICIAL/CS2-AutoAccept"));
            JoinDiscordCommand = new RelayCommand(o => Core.Utility.LaunchWeb("https://discord.gg/Cddu5aJ"));

            // Event handler for double-click on TaskbarIcon
            MyNotifyIcon.TrayMouseDoubleClick += OnTrayIconDoubleClick;

            // Default page
            SwitchPage(new DashboardView());
        }

        /// <summary>
        /// Switches the current content with a fade-in animation.
        /// </summary>
        /// <param name="newPage">The UserControl page to display</param>
        private void SwitchPage(UserControl newPage)
        {
            bool addAnimation = _currentPage != newPage.GetType().Name;

            // If animations are disabled for this switch, do a direct swap (or no-op if same page instance/type)
            if (!addAnimation)
            {
                // If the currently displayed content is already the same page type, do nothing.
                if (MainContent.Content?.GetType() == newPage.GetType())
                {
                    return;
                }

                // Replace without animations
                newPage.Opacity = 1;
                MainContent.Content = newPage;
                _currentPage = newPage.GetType().Name;
                return;
            }

            if (MainContent.Content != null)
            {
                // Fade out current page
                DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(100));
                fadeOut.Completed += (s, e) =>
                {
                    MainContent.Content = newPage;

                    // Fade in new page
                    DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                    newPage.BeginAnimation(OpacityProperty, fadeIn);
                };

                if (MainContent.Content is UserControl current)
                {
                    current.BeginAnimation(OpacityProperty, fadeOut);
                }
            }
            else
            {
                // No current content, just fade in new page
                newPage.Opacity = 0;
                MainContent.Content = newPage;
                DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                newPage.BeginAnimation(OpacityProperty, fadeIn);
            }

            _currentPage = newPage.GetType().Name;
        }

        /// <summary>
        /// Handles the Click event for sidebar navigation buttons, switching the main view to the corresponding page
        /// based on the button's tag.
        /// </summary>
        /// <remarks>The method expects the sender to be a Button with its Tag property set to a
        /// recognized page identifier such as "Dashboard", "Inventory", "Settings", or "Help". If the Tag does not
        /// match a known value, no action is taken.</remarks>
        /// <param name="sender">The source of the event, expected to be a Button representing a sidebar navigation option.</param>
        /// <param name="e">The event data associated with the button click.</param>
        private void SidebarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn)
            {
                switch (btn.Tag?.ToString())
                {
                    case "Dashboard":
                        SwitchPage(new DashboardView());
                        break;
                    case "Inventory":
                        SwitchPage(new InventoryView());
                        break;
                    case "Settings":
                        SwitchPage(new SettingsView());
                        break;
                    case "Help":
                        SwitchPage(new HelpView());
                        break;
                }
            }
        }

        /// <summary>
        /// Closes the application
        /// </summary>
        private void CloseApplication()
        {
            Close();
            Environment.Exit(0);
        }

        private void UpdateTrayIconVisibility()
        {
            // Show or hide the tray icon based on IsTrayIconVisible
            MyNotifyIcon.Visibility =
                IsTrayIconVisible ? Visibility.Visible
                : Visibility.Collapsed;
        }

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

        private static void ShowNotification(string title, string message, double timeoutSeconds = 1)
        {
            Core.Managers.NotificationManager.ShowNotification(title, message, timeoutSeconds);
        }
        #region Event Handlers
        /// <summary>
        /// Event handler for when another instance of the application is started
        /// </summary>
        private void OnAnotherInstanceStarted()
        {
            using (MemoryMappedFile mmf = MemoryMappedFile.CreateOrOpen("StreamDropCollector_MMF", 1024))
            using (MemoryMappedViewStream view = mmf.CreateViewStream())
            {
                BinaryReader reader = new BinaryReader(view);
                EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, "StreamDropCollector_Event");
                Mutex mutex = new Mutex(false, "StreamDropCollector by tsgsOFFICIAL");

                while (true)
                {
                    signal.WaitOne();
                    mutex.WaitOne();
                    reader.BaseStream.Position = 0;
                    string message = reader.ReadString();

                    if (message == "New instance started")
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Activate();
                            Show();
                            WindowState = WindowState.Normal;
                        });
                    }

                    mutex.ReleaseMutex();
                }
            }
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
        private void OnTrayIconDoubleClick(object sender, RoutedEventArgs e)
        {
            // Show the window and restore it to normal state
            Show();
            WindowState = WindowState.Normal;
        }
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
                ShowNotification("Stream Drop Collector", toolTipText);
                toggleMenuItem.Header = "Restore";
                MyNotifyIcon.ToolTipText = toolTipText;
            }
            else
            {
                toggleMenuItem.Header = "Minimize";
                MyNotifyIcon.ToolTipText = "Stream Drop Collector by tsgsOFFICIAL";
            }
        }

        #endregion
    }
}