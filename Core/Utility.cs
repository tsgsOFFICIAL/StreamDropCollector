using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using Core.Logging;
using System.IO;

namespace Core
{
    /// <summary>
    /// Provides cross-platform helpers for launching URLs, resolving executable paths, and command binding.
    /// </summary>
    public static class Utility
    {
        /// <summary>
        /// Launches a web URL in the system's default browser on Windows, Linux, and macOS.
        /// </summary>
        /// <param name="url">The URL to open in the default browser.</param>
        public static void LaunchWeb(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Utility", $"Process.Start direct launch failed for url '{url}'. Falling back by platform. {ex.Message}");
                // Process.Start(url) can fail on .NET Core; use platform-specific shell commands as fallback.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    url = url.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    throw new Exception("Could not open the browser on this machine");
                }
            }
        }

        internal static void WriteToRegistry(string keyName, string keyValue, string[]? arguments = null)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                AppLogger.Debug("Utility", $"Registry Key Check: {key.GetValue(keyName)}");
                AppLogger.Debug("Utility", $"Registry Key Write: \"{keyValue}\" {string.Join(" ", arguments ?? [])}");

                if (arguments != null)
                    key.SetValue(keyName, $"\"{keyValue}\" {string.Join(" ", arguments)}");
                else
                    key.SetValue(keyName, $"\"{keyValue}\"");

                key.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Utility", $"Failed to write startup registry key '{keyName}'.", ex);
                MessageBox.Show(ex.Message, "Stream Drop Collector", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal static void RemoveFromRegistry(string keyName)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");

                AppLogger.Debug("Utility", $"{keyName}");
                AppLogger.Debug("Utility", $"Registry Key Before Delete: {key.GetValue(keyName)}");

                if (key.GetValue(keyName) != null)
                    key.DeleteValue(keyName);

                key.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Utility", $"Failed to remove startup registry key '{keyName}'.", ex);
                MessageBox.Show(ex.Message, "Stream Drop Collector", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Gets the full path to the running application executable.
        /// </summary>
        /// <returns>The absolute path to the current process executable, or a best-effort fallback path when the main module is unavailable.</returns>
        public static string GetExePath()
        {
            string? exeLocation = Process.GetCurrentProcess().MainModule?.FileName;

            string executingDir = AppDomain.CurrentDomain.BaseDirectory;
            string executingName = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

            return exeLocation ?? $"{Path.Combine(executingDir, executingName)}.exe";
        }

        /// <summary>
        /// Represents an asynchronous command that can be bound to UI actions.
        /// </summary>
        /// <typeparam name="T">The type of command parameter passed to the execute delegate.</typeparam>
        /// <param name="executeAsync">The asynchronous action invoked when the command executes.</param>
        public class RelayCommand<T>(Func<T?, Task> executeAsync) : ICommand
        {
            /// <summary>
            /// Occurs when changes occur that affect whether the command should execute.
            /// </summary>
            public event EventHandler? CanExecuteChanged;
            /// <summary>
            /// Determines whether the command can execute in its current state.
            /// </summary>
            /// <param name="parameter">The command parameter.</param>
            /// <returns>Always <see langword="true"/> for this command implementation.</returns>
            public bool CanExecute(object? parameter) => true;
            /// <summary>
            /// Invokes the asynchronous execute delegate for the command.
            /// </summary>
            /// <param name="parameter">The command parameter. When it is of type <typeparamref name="T"/>, it is passed to the delegate; otherwise, <see langword="default"/> is used.</param>
            public async void Execute(object? parameter) => await executeAsync(parameter is T t ? t : default);
        }
    }
}