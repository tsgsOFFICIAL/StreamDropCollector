using Core.Models;

namespace Core.Interfaces
{
    /// <summary>
    /// API for Twitch live-channel lookup and eligibility (selection + health checks).
    /// </summary>
    public interface ITwitchLiveChannelApi
    {
        /// <summary>
        /// Picks the best live channel login for a campaign, preferring <paramref name="preferredLogin"/> when eligible.
        /// </summary>
        Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default);

        /// <summary>
        /// Returns whether <paramref name="channelLogin"/> is live and streaming <paramref name="gameSlug"/>.
        /// </summary>
        Task<bool> IsChannelEligibleAsync(string channelLogin, string gameSlug, CancellationToken ct = default);
    }
}