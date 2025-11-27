using Core.Managers;
using Microsoft.Win32;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Threading;

namespace UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string RegistryValueName = "AppsUseLightTheme";

        public App()
        {
            // Handle UI thread exceptions
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Handle background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }
        
        protected override void OnStartup(StartupEventArgs e)
        {
            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            // Check if another instance of the app is already running
            Mutex mutex = new Mutex(true, "StreamDropCollector by tsgsOFFICIAL", out bool createdNew);
            string[] args = Environment.GetCommandLineArgs();
            bool ignoreMutexRule = false; // This is true if the application is started as a replacement, for example when updating or when an error occurs.

            foreach (string arg in args)
            {
                // Application was ignoreMutexRule
                if (arg.ToLower().Equals("--updated"))
                {
                    ignoreMutexRule = true;
                    break;
                }
            }

            // If another instance exists, trigger the event and exit
            if (!createdNew && !ignoreMutexRule)
            {
                // Create a MemoryMappedFile to notify the other instance
                using (MemoryMappedFile mmf = MemoryMappedFile.CreateOrOpen("StreamDropCollector_MMF", 1024))
                using (MemoryMappedViewStream view = mmf.CreateViewStream())
                {
                    BinaryWriter writer = new BinaryWriter(view);
                    EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset, "StreamDropCollector_Event");
                    writer.Write("New instance started");
                    signal.Set(); // Signal the other instance that it should come to the front
                }

                // Shutdown the second instance
                Environment.Exit(-2);

                // Ensure mutex is released only if it was created successfully
                if (createdNew)
                    mutex.ReleaseMutex();

                return;
            }

            // Run the WPF application
            base.OnStartup(e);

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

            // Show window
            new MainWindow().Show();
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