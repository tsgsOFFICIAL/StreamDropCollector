using Core.Models;

namespace Core.Services.Mining.Twitch
{
    /// <summary>
    /// Eligibility rules for Twitch mining using the canonical Helix <c>game_id</c> from drops inventory.
    /// </summary>
    internal static class TwitchChannelEligibility
    {
        /// <summary>
        /// Returns whether <paramref name="snapshot"/> is live and streaming the same Twitch game as <paramref name="campaign"/>.
        /// </summary>
        public static bool IsEligible(LiveChannelSnapshot? snapshot, DropsCampaign campaign)
        {
            if (snapshot is not { IsLive: true })
                return false;

            if (string.IsNullOrWhiteSpace(campaign.GameId) || string.IsNullOrWhiteSpace(snapshot.GameId))
                return false;

            return string.Equals(campaign.GameId, snapshot.GameId, StringComparison.Ordinal);
        }
    }
}