using Core.Models;
using Core.Enums;
using Core.Logging;

namespace Core.Helpers
{
    /// <summary>
    /// Extracts eligible streamer channel logins from campaign connect URLs.
    /// </summary>
    public static class EligibleStreamerParser
    {
        // Directory-style path segments are not channel slugs.
        private static readonly HashSet<string> DirectoryPathSegments = new(StringComparer.OrdinalIgnoreCase)
        {
            "directory", "browse", "category", "drops", "videos", "search"
        };

        /// <summary>
        /// Parses unique channel logins from a campaign's connect URLs.
        /// </summary>
        /// <param name="campaign">The drops campaign whose connect URLs are parsed.</param>
        /// <returns>Sorted channel logins, or an empty list for general drops or campaigns without URLs.</returns>
        public static IReadOnlyList<string> ParseChannelLogins(DropsCampaign campaign)
        {
            if (campaign.IsGeneralDrop || campaign.ConnectUrls.Count == 0)
            {
                AppLogger.Debug(
                    "EligibleStreamers",
                    $"ParseChannelLogins SKIP campaignId={campaign.Id} isGeneralDrop={campaign.IsGeneralDrop} connectUrlCount={campaign.ConnectUrls.Count}");
                return Array.Empty<string>();
            }

            HashSet<string> logins = new(StringComparer.OrdinalIgnoreCase);

            foreach (string url in campaign.ConnectUrls)
            {
                if (TryParseChannelLogin(url, campaign.Platform, out string? login))
                    logins.Add(login);
                else
                    AppLogger.Debug("EligibleStreamers", $"ParseChannelLogins could not extract login from url={url} platform={campaign.Platform}.");
            }

            List<string> result = logins.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
            AppLogger.Debug(
                "EligibleStreamers",
                $"ParseChannelLogins DONE campaignId={campaign.Id} connectUrlCount={campaign.ConnectUrls.Count} logins=[{string.Join(", ", result)}]");

            return result;
        }

        /// <summary>
        /// Attempts to extract a channel login from a platform-specific stream URL.
        /// </summary>
        /// <param name="url">Absolute URL to parse.</param>
        /// <param name="platform">Expected streaming platform for host validation.</param>
        /// <param name="login">The parsed channel login when successful.</param>
        /// <returns><see langword="true"/> when a valid channel login was extracted; otherwise <see langword="false"/>.</returns>
        public static bool TryParseChannelLogin(string url, Platform platform, out string login)
        {
            login = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return false;

            string host = uri.Host;

            bool validHost = platform switch
            {
                Platform.Twitch => host.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase),
                Platform.Kick => host.Contains("kick.com", StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            if (!validHost)
                return false;

            string path = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string firstSegment = path.Split('/')[0];

            if (DirectoryPathSegments.Contains(firstSegment))
                return false;

            login = firstSegment;
            return !string.IsNullOrWhiteSpace(login);
        }
    }
}