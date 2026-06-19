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
            AppLogger.Initialize();

            FileVersionInfo localVersionInfo = FileVersionInfo.GetVersionInfo(Utility.GetExePath());
            string versionInfo = localVersionInfo.FileVersion ?? "N/A";

            // Log app startup + Version
            AppLogger.Info("App", $"Starting StreamDropCollector version {versionInfo}");

            bool ignoreMutexRule = e.Args.Any(a => a.Equals("--updating", StringComparison.OrdinalIgnoreCase) || a.Equals("--updated", StringComparison.OrdinalIgnoreCase));

            _instanceMutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew && !ignoreMutexRule)
            {
                // Notify existing instance
                AppLogger.Warn("App", "Second instance detected; signaling existing instance and shutting down.");
                TryActivateExistingInstance();
                Shutdown();
                return;
            }

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
        }

        private static void TryActivateExistingInstance()
        {
            try
            {
                using NamedPipeClientStream client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out);

                client.Connect(500);
                using StreamWriter writer = new StreamWriter(client);
                writer.WriteLine("ACTIVATE");
                writer.Flush();
            }
            catch
            {
                // Existing instance not ready yet - safe to ignore
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

        // Clean up event when app closes
        protected override void OnExit(ExitEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
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