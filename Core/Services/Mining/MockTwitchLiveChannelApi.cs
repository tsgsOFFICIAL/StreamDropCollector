using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining
{
    /// <summary>
    /// Placeholder Twitch live-channel API. Replace with a real implementation later.
    /// </summary>
    public sealed class MockTwitchLiveChannelApi : ITwitchLiveChannelApi
    {
        public Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default)
        {
            IReadOnlyList<string> logins = EligibleStreamerParser.ParseChannelLogins(campaign);

            if (logins.Count == 0)
            {
                AppLogger.Warn("MockTwitchApi", $"No channel logins for campaign '{campaign.Name}'. General drops need a real API.");
                return Task.FromResult<string?>(null);
            }

            if (!string.IsNullOrWhiteSpace(preferredLogin)
                && logins.Any(l => string.Equals(l, preferredLogin, StringComparison.OrdinalIgnoreCase)))
            {
                AppLogger.Debug("MockTwitchApi", $"Using preferred login '{preferredLogin}' for '{campaign.Name}'.");
                return Task.FromResult<string?>(preferredLogin);
            }

            string selected = logins[0];
            AppLogger.Debug("MockTwitchApi", $"Using first login '{selected}' for '{campaign.Name}'.");
            return Task.FromResult<string?>(selected);
        }

        public Task<bool> IsChannelEligibleAsync(string channelLogin, string gameSlug, CancellationToken ct = default)
        {
            bool eligible = !string.IsNullOrWhiteSpace(channelLogin) && !string.IsNullOrWhiteSpace(gameSlug);
            AppLogger.Debug("MockTwitchApi", $"IsChannelEligible login={channelLogin}, slug={gameSlug} -> {eligible}");
            return Task.FromResult(eligible);
        }
    }
}