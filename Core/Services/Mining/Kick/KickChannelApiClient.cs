using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Fetches Kick <c>GET /api/v2/channels/{slug}</c> via in-page WebView <c>fetch</c> (kick.com origin required).
    /// </summary>
    public sealed class KickChannelApiClient
    {
        private const string KickOrigin = "https://kick.com/";
        private readonly Func<IWebViewHost?> _hostProvider;

        public KickChannelApiClient(Func<IWebViewHost?> hostProvider)
        {
            _hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
        }

        /// <summary>
        /// Fetches channel metadata for the given login slug.
        /// </summary>
        public async Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channelLogin))
                return null;

            IWebViewHost? host = _hostProvider();
            if (host == null)
            {
                AppLogger.Warn("KickChannelApi", "Kick WebView not initialized; cannot fetch channel metadata.");
                return null;
            }

            string slug = channelLogin.Trim().ToLowerInvariant();
            string url = $"https://kick.com/api/v2/channels/{Uri.EscapeDataString(slug)}";

            try
            {
                string? json = await WebViewJsonFetch.GetAsync(host, url, KickOrigin, ct);
                if (string.IsNullOrWhiteSpace(json))
                {
                    AppLogger.Warn("KickChannelApi", $"GET /api/v2/channels/{slug} returned no body.");
                    return null;
                }

                return KickChannelResponseParser.Parse(json, slug);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("KickChannelApi", $"Failed to fetch Kick channel '{slug}'. {ex.Message}");
                return null;
            }
        }
    }
}