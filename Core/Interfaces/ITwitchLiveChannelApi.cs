using Core.Models;

namespace Core.Interfaces
{
    /// <summary>
    /// API for Twitch live-channel lookup and eligibility (selection + health checks).
    /// </summary>
    public interface ITwitchLiveChannelApi
    {
        /// <summary>
        /// Fetches normalized channel metadata for a login slug.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The channel snapshot, or <see langword="null"/> when the channel could not be resolved.</returns>
        Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default);

        /// <summary>
        /// Picks the best live channel login for a campaign, preferring <paramref name="preferredLogin"/> when eligible.
        /// </summary>
        /// <param name="campaign">Drop campaign whose eligible streamers and game slug are evaluated.</param>
        /// <param name="preferredLogin">Optional login to try first (for example the last mined streamer).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The selected login slug, or <see langword="null"/> when no eligible live streamer is found.
        /// </returns>
        Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default);

        /// <summary>
        /// Returns whether <paramref name="channelLogin"/> is live and streaming the campaign's Twitch game.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="campaign">Drop campaign whose <see cref="DropsCampaign.GameId"/> is matched against Helix <c>game_id</c>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><see langword="true"/> when the channel is live and game-eligible; otherwise <see langword="false"/>.</returns>
        Task<bool> IsChannelEligibleAsync(string channelLogin, DropsCampaign campaign, CancellationToken ct = default);
    }
}