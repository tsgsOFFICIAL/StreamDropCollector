using Core;
using Core.Logging;
using Core.Managers;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Threading;

namespace UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// Gets a value indicating whether the application was launched with the <c>--debug</c> command-line flag.
        /// </summary>
        public static bool IsDebugMode { get; private set; }

        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";

        private const string MutexName = @"Global\StreamDropCollector_Instance";
        internal const string PipeName = "StreamDropCollector_ActivationPipe";

        private Mutex? _instanceMutex;

        /// <summary>
        /// Initializes the application and registers global unhandled exception handlers.
        /// </summary>
        public App()
        {
            // Handle UI thread exceptions
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Handle background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        /// <summary>
        /// Performs application startup initialization, including single-instance enforcement, theme loading, and logging setup.
        /// </summary>
        /// <param name="e">Startup event data containing command-line arguments.</param>
        protected override void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Auto-update passes --updating/--updated so a second process can run while the old one shuts down.
            bool ignoreMutexRule = e.Args.Any(a => a.Equals("--updating", StringComparison.OrdinalIgnoreCase) || a.Equals("--updated", StringComparison.OrdinalIgnoreCase));

            // TryOpenExisting avoids blocking on new Mutex(true, ...) when another instance already holds the lock.
            if (!ignoreMutexRule && Mutex.TryOpenExisting(MutexName, out Mutex? existingMutex))
            {
                existingMutex.Dispose();
                AppLogger.Initialize();
                AppLogger.Warn("App", "Second instance detected; signaling existing instance and shutting down.");
                TryActivateExistingInstance();
                Shutdown();
                return;
            }

            _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew && !ignoreMutexRule)
            {
                AppLogger.Initialize();
                AppLogger.Warn("App", "Second instance detected after mutex creation; signaling existing instance and shutting down.");
                TryActivateExistingInstance();
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }

            AppLogger.Initialize();

            AppLogger.Info("App", $"Starting StreamDropCollector version {Utility.GetDisplayVersion()}");

            base.OnStartup(e);

            IsDebugMode = e.Args.Contains("--debug");

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            // Load Colors and shared control styles first
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Themes/Colors.xaml", UriKind.Relative)
            });
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Themes/Controls.xaml", UriKind.Relative)
            });

            // Load theme
            ApplyTheme(UISettingsManager.Instance.Theme);

            // Subscribe to settings
            UISettingsManager.Instance.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(UISettingsManager.Theme))
                {
                    ApplyTheme(UISettingsManager.Instance.Theme);
                }
            };

            // REACT TO WINDOWS THEME CHANGE IN REAL TIME
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
        }

        /// <summary>
        /// Signals the running instance over a named pipe so it can restore and focus its main window.
        /// </summary>
        /// <remarks>Retries briefly because the pipe server starts after <see cref="MainWindow"/> loads.</remarks>
        private static void TryActivateExistingInstance()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using NamedPipeClientStream client = new NamedPipeClientStream(
                        ".",
                        PipeName,
                        PipeDirection.Out,
                        PipeOptions.None);

                    client.Connect(300);
                    using StreamWriter writer = new StreamWriter(client);
                    writer.WriteLine("ACTIVATE");
                    writer.Flush();
                    return;
                }
                catch
                {
                    Thread.Sleep(200);
                }
            }
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                // Only refresh if current setting is "System"
                if (UISettingsManager.Instance.Theme == "System")
                {
                    Dispatcher.Invoke(() => ApplyTheme("System"));
                }
            }
        }

        private void ApplyTheme(string requestedTheme)
        {
            string actualTheme = requestedTheme;

            if (requestedTheme == "System")
            {
                actualTheme = IsSystemInDarkMode() ? "Dark" : "Light";
            }

            Uri uri = actualTheme == "Light"
                ? new Uri("/Themes/Light.xaml", UriKind.Relative)
                : new Uri("/Themes/Dark.xaml", UriKind.Relative);

            // Remove old theme dict
            ResourceDictionary? old = Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString.Contains("Light.xaml") == true ||
                                     d.Source?.OriginalString.Contains("Dark.xaml") == true);

            if (old != null)
                Resources.MergedDictionaries.Remove(old);

            // Add new
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
        }

        private static bool IsSystemInDarkMode()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                object? value = key?.GetValue(RegistryValueName);
                return value is int i && i == 0; // 0 = Dark mode
            }
            catch
            {
                return true; // fallback to dark
            }
        }

        /// <summary>
        /// Releases the single-instance mutex and unsubscribes from system theme change notifications.
        /// </summary>
        /// <param name="e">Exit event data.</param>
        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

            if (_instanceMutex != null)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch (System.ApplicationException)
                {
                    // Mutex was not owned by this thread.
                }

                _instanceMutex.Dispose();
                _instanceMutex = null;
            }

            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            AppLogger.Error("App", "Unhandled UI thread exception.", e.Exception);
            System.Windows.MessageBox.Show($"An undefined error has happened, please contact tsgsOFFICIAL to resolve this issue.\n\nInclude the following Error Message: {e.Exception.Message}", "Undefined Error", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true; // Prevents the application from crashing
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                AppLogger.Error("App", "Unhandled non-UI exception.", ex);
                System.Windows.MessageBox.Show($"A critical error has happened, please contact tsgsOFFICIAL to resolve this issue.\n\nInclude the following Error Message: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}