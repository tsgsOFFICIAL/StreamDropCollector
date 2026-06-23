using Core.Helpers;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining
{
    /// <summary>
    /// Picks the best eligible live login from a campaign's connect URLs.
    /// </summary>
    public static class LiveChannelSelector
    {
        /// <summary>
        /// Tries the preferred login first, then remaining campaign logins, returning the first eligible match.
        /// </summary>
        public static async Task<string?> SelectBestLiveLoginAsync(
            DropsCampaign campaign,
            string? preferredLogin,
            Func<string, CancellationToken, Task<LiveChannelSnapshot?>> getChannelAsync,
            string logScope,
            CancellationToken ct = default)
        {
            IReadOnlyList<string> logins = EligibleStreamerParser.ParseChannelLogins(campaign);

            if (logins.Count == 0)
            {
                AppLogger.Warn(logScope, $"No channel logins for campaign '{campaign.Name}'. General drops need directory discovery.");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredLogin))
            {
                LiveChannelSnapshot? preferredSnapshot = await getChannelAsync(preferredLogin, ct);
                if (LiveChannelEligibility.IsEligible(preferredSnapshot, campaign.Slug))
                {
                    AppLogger.Debug(logScope, $"Selected preferred login '{preferredLogin}' for '{campaign.Name}' (live, category ok).");
                    return preferredLogin;
                }

                AppLogger.Debug(logScope, $"Preferred login '{preferredLogin}' is not eligible for '{campaign.Name}'.");
            }

            foreach (string login in logins)
            {
                if (string.Equals(login, preferredLogin, StringComparison.OrdinalIgnoreCase))
                    continue;

                LiveChannelSnapshot? snapshot = await getChannelAsync(login, ct);
                if (LiveChannelEligibility.IsEligible(snapshot, campaign.Slug))
                {
                    AppLogger.Debug(logScope, $"Selected login '{login}' for '{campaign.Name}' (live, category ok).");
                    return login;
                }
            }

            AppLogger.Warn(logScope, $"No eligible live streamer found for campaign '{campaign.Name}' among {logins.Count} candidate(s).");
            return null;
        }
    }
}