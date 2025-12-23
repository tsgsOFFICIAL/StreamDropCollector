using System.Windows.Threading;
using Microsoft.Win32;
using System.IO.Pipes;
using System.Windows;
using Core.Managers;
using System.IO;

namespace UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";

        private const string MutexName = @"Global\StreamDropCollector_Instance";
        internal const string PipeName = "StreamDropCollector_ActivationPipe";

        private Mutex? _instanceMutex;

        public App()
        {
            // Handle UI thread exceptions
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Handle background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            bool ignoreMutexRule = e.Args.Any(a => a.Equals("--updating", StringComparison.OrdinalIgnoreCase) || a.Equals("--updated", StringComparison.OrdinalIgnoreCase));

            _instanceMutex = new Mutex(true, MutexName, out bool createdNew);

            if (!createdNew && !ignoreMutexRule)
            {
                // Notify existing instance
                TryActivateExistingInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            // Load Colors first
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Themes/Colors.xaml", UriKind.Relative)
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
                // Existing instance not ready yet — safe to ignore
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
            System.Windows.MessageBox.Show($"An undefined error has happened, please contact tsgsOFFICIAL to resolve this issue.\n\nInclude the following Error Message: {e.Exception.Message}", "Undefined Error", MessageBoxButton.OK, MessageBoxImage.Error);

            e.Handled = true; // Prevents the application from crashing
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                System.Windows.MessageBox.Show($"A critical error has happened, please contact tsgsOFFICIAL to resolve this issue.\n\nInclude the following Error Message: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}