using System.Windows;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Twitch
{
    /// <summary>
    /// Discovers drops-enabled live streamers from Twitch's directory page via the hidden WebView.
    /// </summary>
    internal sealed class TwitchGeneralDropDirectoryClient
    {
        private readonly Func<IWebViewHost?> _webViewProvider;

        public TwitchGeneralDropDirectoryClient(Func<IWebViewHost?> webViewProvider)
        {
            _webViewProvider = webViewProvider ?? throw new ArgumentNullException(nameof(webViewProvider));
        }

        /// <summary>
        /// Navigates to the campaign directory URL (<c>?filter=drops</c>) and scrapes up to <paramref name="limit"/> logins.
        /// </summary>
        public async Task<IReadOnlyList<string>> DiscoverLoginsAsync(
            DropsCampaign campaign,
            int limit = TwitchGeneralDropDiscoveryCache.MaxLogins,
            CancellationToken ct = default)
        {
            if (!campaign.IsGeneralDrop || campaign.ConnectUrls.Count == 0)
                return Array.Empty<string>();

            IWebViewHost? host = _webViewProvider();
            if (host is null)
            {
                AppLogger.Warn("TwitchGeneralDrop", $"Directory discovery skipped for '{campaign.Name}' - Twitch WebView is null.");
                return Array.Empty<string>();
            }

            string directoryUrl = campaign.ConnectUrls[0];

            return await await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                ct.ThrowIfCancellationRequested();

                AppLogger.Info(
                    "TwitchGeneralDrop",
                    $"Directory discovery START campaign='{campaign.Name}' url={directoryUrl} limit={limit}");

                await host.NavigateAsync(directoryUrl);
                bool idle = await host.WaitForNetworkIdleAsync(8000, 500, ct);
                if (!idle)
                    AppLogger.Warn("TwitchGeneralDrop", $"Directory page network idle timeout for '{campaign.Name}'; scraping anyway.");

                string raw = await host.ExecuteScriptAsync(TwitchGeneralDropDirectoryScripts.GetTopStreamerLoginsJs(limit));
                IReadOnlyList<string> logins = ParseLoginsResult(raw);

                AppLogger.Info(
                    "TwitchGeneralDrop",
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