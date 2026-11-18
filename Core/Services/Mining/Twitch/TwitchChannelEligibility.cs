using Core.Models;
using Core.Services.Twitch.Helix;

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
        /// <param name="hintGameId">For a reward campaign that names its category only in prose, the game id that hint resolved to.</param>
        public static bool IsEligible(LiveChannelSnapshot? snapshot, DropsCampaign campaign, string? hintGameId = null)
        {
            if (snapshot is not { IsLive: true })
                return false;

            // A reward campaign counts watch time in the categories its summary names (and in its own game, when it has one).
            if (campaign.IsRewardCampaign)
                return IsInRewardCampaignCategory(snapshot, campaign)
                    || (!string.IsNullOrWhiteSpace(hintGameId) && string.Equals(hintGameId, snapshot.GameId, StringComparison.Ordinal));

            if (string.IsNullOrWhiteSpace(campaign.GameId) || string.IsNullOrWhiteSpace(snapshot.GameId))
                return false;

            return string.Equals(campaign.GameId, snapshot.GameId, StringComparison.Ordinal);
        }

        private static bool IsInRewardCampaignCategory(LiveChannelSnapshot snapshot, DropsCampaign campaign)
        {
            if (!string.IsNullOrWhiteSpace(campaign.GameId)
                && string.Equals(campaign.GameId, snapshot.GameId, StringComparison.Ordinal))
                return true;

            if (campaign.ParticipatingCategories is not { Count: > 0 } categories)
                return false;

            // The snapshot lists the stream's category as its name, its slug and its id.
            HashSet<string> streamCategories = new(snapshot.CategorySlugs, StringComparer.OrdinalIgnoreCase);
            return categories.Any(name =>
                streamCategories.Contains(name) || streamCategories.Contains(TwitchGameSlugHelper.Slugify(name)));
        }
    }
}