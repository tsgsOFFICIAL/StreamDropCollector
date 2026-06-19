using Core.Models;
using Core.Enums;

namespace UI.Helpers
{
    public static class EligibleStreamerParser
    {
        private static readonly HashSet<string> DirectoryPathSegments = new(StringComparer.OrdinalIgnoreCase)
        {
            "directory", "browse", "category", "drops", "videos", "search"
        };

        public static IReadOnlyList<string> ParseChannelLogins(DropsCampaign campaign)
        {
            if (campaign.IsGeneralDrop || campaign.ConnectUrls.Count == 0)
                return Array.Empty<string>();

            HashSet<string> logins = new(StringComparer.OrdinalIgnoreCase);

            foreach (string url in campaign.ConnectUrls)
            {
                if (TryParseChannelLogin(url, campaign.Platform, out string? login))
                    logins.Add(login);
            }

            return logins.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();
        }

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