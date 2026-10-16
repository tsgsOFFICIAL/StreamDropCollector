using System.Net;
using System.Net.Http;
using Core.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace UI.Diagnostics
{
    /// <summary>
    /// Provides one <see cref="NetworkMeter"/> per WebView2 profile folder for the lifetime of the app.
    /// </summary>
    /// <remarks>
    /// WebView2 instances sharing a profile folder need identical browser arguments, so every WebView on a profile takes
    /// its <see cref="CoreWebView2CreationProperties"/> from <see cref="CreateProperties"/>. Accounts on the legacy
    /// shared profile share one meter with everything else that uses the default profile.
    /// </remarks>
    public static class NetworkMeters
    {
        private const string DefaultProfileKey = "(default profile)";

        private static readonly object Gate = new();
        private static readonly Dictionary<string, NetworkMeter?> Meters = new(StringComparer.OrdinalIgnoreCase);
        private static bool? _canMeter;

        /// <summary>
        /// Gets the meter for a profile folder (<see langword="null"/> for the default shared profile), or
        /// <see langword="null"/> when measuring is not possible.
        /// </summary>
        public static NetworkMeter? ForProfile(string? userDataFolder)
        {
            string key = userDataFolder ?? DefaultProfileKey;

            lock (Gate)
            {
                if (Meters.TryGetValue(key, out NetworkMeter? existing))
                    return existing;

                NetworkMeter? meter = null;
                if (CanMeter())
                {
                    try
                    {
                        meter = new NetworkMeter();
                        AppLogger.Info("NetMeter", $"Counting proxy for profile '{key}' listening on 127.0.0.1:{meter.Port}.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("NetMeter", $"Could not start the counting proxy for profile '{key}': {ex.Message}");
                    }
                }

                Meters[key] = meter;
                return meter;
            }
        }

        /// <summary>
        /// Creates the WebView2 creation properties for a profile folder, including the proxy argument when metered.
        /// </summary>
        public static Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties CreateProperties(string? userDataFolder)
        {
            NetworkMeter? meter = ForProfile(userDataFolder);

            return new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
            {
                UserDataFolder = userDataFolder,
                AdditionalBrowserArguments = meter != null ? $"--proxy-server=http://127.0.0.1:{meter.Port}" : null
            };
        }

        /// <summary>
        /// Whether requests to <paramref name="destination"/> would go through a proxy. <c>IsBypassed</c> is unreliable
        /// for the system proxy, so this checks the address <c>GetProxy</c> returns: none, or the destination itself,
        /// means a direct connection.
        /// </summary>
        private static bool UsesProxy(IWebProxy proxy, Uri destination)
        {
            Uri? proxyUri = proxy.GetProxy(destination);
            return proxyUri != null && !string.Equals(proxyUri.Host, destination.Host, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Measuring replaces the browser's proxy settings, so it is skipped when Windows routes Twitch or Kick through
        /// a system proxy.
        /// </summary>
        private static bool CanMeter()
        {
            if (_canMeter is bool known)
                return known;

            try
            {
                IWebProxy systemProxy = HttpClient.DefaultProxy;
                bool direct = !UsesProxy(systemProxy, new Uri("https://www.twitch.tv")) && !UsesProxy(systemProxy, new Uri("https://kick.com"));
                if (!direct)
                    AppLogger.Info("NetMeter", "A system proxy is configured for Twitch/Kick; network usage is not measured.");

                _canMeter = direct;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("NetMeter", $"Could not read the system proxy settings, network usage is not measured: {ex.Message}");
                _canMeter = false;
            }

            return _canMeter.Value;
        }
    }
}
