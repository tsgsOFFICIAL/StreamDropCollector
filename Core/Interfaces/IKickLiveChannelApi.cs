using Core.Models;

namespace Core.Interfaces
{
    /// <summary>
    /// API for Kick live-channel lookup and eligibility (selection + health checks).
    /// </summary>
    public interface IKickLiveChannelApi
    {
        /// <summary>
        /// Fetches normalized channel metadata for a login slug.
        /// </summary>
        Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default);

        /// <summary>
        /// Resolves eligible channel logins for a campaign (connect URLs, or directory discovery for general drops).
        /// </summary>
        /// <param name="campaign">Drop campaign to resolve streamers for.</param>
        /// <param name="allowDirectoryDiscovery">
        /// When <see langword="false"/>, general-drop campaigns return cached directory logins only (no WebView navigation).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Channel login slugs in page order for general drops.</returns>
        Task<IReadOnlyList<string>> GetEligibleLoginsAsync(
            DropsCampaign campaign,
            bool allowDirectoryDiscovery = true,
            CancellationToken ct = default);

        /// <summary>
        /// Preloads directory-discovered logins for all general-drop Kick campaigns (WebView, sequential).
        /// </summary>
        /// <param name="campaigns">Campaigns to inspect; only general-drop Kick entries are discovered.</param>
        /// <param name="ct">Cancellation token.</param>
        Task PreloadGeneralDropDirectoriesAsync(IReadOnlyList<DropsCampaign> campaigns, CancellationToken ct = default);

        /// <summary>
        /// Picks the best live channel login for a campaign, preferring <paramref name="preferredLogin"/> when eligible.
        /// </summary>
        Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default);

        /// <summary>
        /// Returns whether <paramref name="channelLogin"/> is live and streaming <paramref name="gameSlug"/>.
        /// </summary>
        Task<bool> IsChannelEligibleAsync(string channelLogin, DropsCampaign campaign, CancellationToken ct = default);
    }
}