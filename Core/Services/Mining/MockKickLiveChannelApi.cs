using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining
{
    /// <summary>
    /// Placeholder Kick live-channel API. Replace with a real implementation later.
    /// </summary>
    public sealed class MockKickLiveChannelApi : IKickLiveChannelApi
    {
        public Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default)
        {
            IReadOnlyList<string> logins = EligibleStreamerParser.ParseChannelLogins(campaign);

            if (logins.Count == 0)
            {
                AppLogger.Warn("MockKickApi", $"No channel logins for campaign '{campaign.Name}'. General drops need a real API.");
                return Task.FromResult<string?>(null);
            }

            if (!string.IsNullOrWhiteSpace(preferredLogin)
                && logins.Any(l => string.Equals(l, preferredLogin, StringComparison.OrdinalIgnoreCase)))
            {
                AppLogger.Debug("MockKickApi", $"Using preferred login '{preferredLogin}' for '{campaign.Name}'.");
                return Task.FromResult<string?>(preferredLogin);
            }

            string selected = logins[0];
            AppLogger.Debug("MockKickApi", $"Using first login '{selected}' for '{campaign.Name}'.");
            return Task.FromResult<string?>(selected);
        }

        public Task<bool> IsChannelEligibleAsync(string channelLogin, string gameSlug, CancellationToken ct = default)
        {
            bool eligible = !string.IsNullOrWhiteSpace(channelLogin);
            AppLogger.Debug("MockKickApi", $"IsChannelEligible login={channelLogin}, slug={gameSlug} -> {eligible}");
            return Task.FromResult(eligible);
        }
    }
}