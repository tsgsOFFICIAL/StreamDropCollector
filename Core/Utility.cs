using System.Runtime.InteropServices;
using System.Diagnostics;

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
    }
}