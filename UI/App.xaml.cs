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