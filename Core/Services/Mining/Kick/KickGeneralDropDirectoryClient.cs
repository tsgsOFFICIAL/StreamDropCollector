using System.Windows;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using Core.Services.Mining;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Discovers live streamers from Kick's browse/category listing pages via the hidden WebView.
    /// </summary>
    internal sealed class KickGeneralDropDirectoryClient
    {
        private readonly Func<IWebViewHost?> _webViewProvider;

        public KickGeneralDropDirectoryClient(Func<IWebViewHost?> webViewProvider)
        {
            _webViewProvider = webViewProvider ?? throw new ArgumentNullException(nameof(webViewProvider));
        }

        /// <summary>
        /// Navigates to the campaign directory URL (browse or category listing) and scrapes up to <paramref name="limit"/> logins.
        /// </summary>
        public async Task<IReadOnlyList<string>> DiscoverLoginsAsync(
            DropsCampaign campaign,
            int limit = GeneralDropDiscoveryCache.MaxLogins,
            CancellationToken ct = default)
        {
            if (!campaign.IsGeneralDrop || campaign.ConnectUrls.Count == 0)
                return Array.Empty<string>();

            IWebViewHost? host = _webViewProvider();
            if (host is null)
            {
                AppLogger.Warn("KickGeneralDrop", $"Directory discovery skipped for '{campaign.Name}' - Kick WebView is null.");
                return Array.Empty<string>();
            }

            string directoryUrl = campaign.ConnectUrls[0];

            return await await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                ct.ThrowIfCancellationRequested();

                AppLogger.Debug(
                    "KickGeneralDrop",
                    $"Directory discovery START campaign='{campaign.Name}' url={directoryUrl} limit={limit}");

                await host.NavigateAsync(directoryUrl);
                bool idle = await host.WaitForNetworkIdleAsync(8000, 500, ct);
                if (!idle)
                    AppLogger.Warn("KickGeneralDrop", $"Directory page network idle timeout for '{campaign.Name}'; scraping anyway.");

                string raw = await host.ExecuteScriptAsync(KickGeneralDropDirectoryScripts.GetTopStreamerLoginsJs(limit));
                IReadOnlyList<string> logins = ParseLoginsResult(raw);

                AppLogger.Debug(
                    "KickGeneralDrop",
                    $"Directory discovery DONE campaign='{campaign.Name}' found={logins.Count} logins=[{string.Join(", ", logins)}]");

                return logins;
            });
        }

        private static IReadOnlyList<string> ParseLoginsResult(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            string trimmed = raw.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed))
                return Array.Empty<string>();

            return trimmed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}