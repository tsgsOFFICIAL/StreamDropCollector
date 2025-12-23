using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.IO;

namespace Core
{
    public static class Utility
    {
        /// <summary>
        /// Launch a web URL on Windows, Linux and OSX
        /// </summary>
        /// <param Name="url">The URL to open in the standard browser</param>
        public static void LaunchWeb(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch
            {
                // Hack for running the above line in DOTNET Core...
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
                Debug.WriteLine($"Registry Key Check: {key.GetValue(keyName)}");
                Debug.WriteLine($"Registry Key Write: \"{keyValue}\" {string.Join(" ", arguments ?? [])}");

                if (arguments != null)
                    key.SetValue(keyName, $"\"{keyValue}\" {string.Join(" ", arguments)}");
                else
                    key.SetValue(keyName, $"\"{keyValue}\"");

                key.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Stream Drop Collector", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        internal static void RemoveFromRegistry(string keyName)
        {
            try
            {
                RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");

                Debug.WriteLine($"{keyName}");
                Debug.WriteLine($"Registry Key Before Delete: {key.GetValue(keyName)}");
                
                if (key.GetValue(keyName) != null)
                    key.DeleteValue(keyName);

                key.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Stream Drop Collector", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static string GetExePath()
        {
            string? exeLocation5 = Process.GetCurrentProcess().MainModule?.FileName;

            string executingDir = AppDomain.CurrentDomain.BaseDirectory;
            string executingName = Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]);

            return exeLocation5 ?? $"{Path.Combine(executingDir, executingName)}.exe";
        }
    }
}