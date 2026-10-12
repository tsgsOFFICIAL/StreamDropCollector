using System.Text.Json.Nodes;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Decides whether a Twitch "earnedDropRewards" entry proves a specific drop was claimed.
    /// </summary>
    /// <remarks>
    /// Earned entries only carry the reward's benefit id. Some campaigns (for example tiered "cache" drops) reuse one
    /// benefit id across several drops, so a single claimed entry must not mark every drop that shares the id as claimed.
    /// </remarks>
    public static class EarnedRewardMatcher
    {
        /// <summary>
        /// Determines whether the earned entries mark <paramref name="reward"/> as claimed.
        /// </summary>
        /// <param name="reward">The drop being evaluated.</param>
        /// <param name="campaignId">Id of the campaign that owns the drop.</param>
        /// <param name="campaignRewards">All drops of the campaign.</param>
        /// <param name="earnedDropRewardEdges">The inventory's earnedDropRewards edges.</param>
        /// <param name="hasInProgressRecord">Whether the server returned an in-progress record (with its own claim flag) for the drop.</param>
        /// <returns><see langword="true"/> when the earned entries prove the drop was claimed.</returns>
        public static bool IsClaimedByEarnedEntries(
            DropsReward reward,
            string campaignId,
            IReadOnlyList<DropsReward> campaignRewards,
            JsonArray earnedDropRewardEdges,
            bool hasInProgressRecord)
        {
            List<JsonObject> claimed = [.. earnedDropRewardEdges.OfType<JsonObject>()
                .Select(edge => edge["node"]?.AsObject())
                .OfType<JsonObject>()
                .Where(node => node["item"]?["id"]?.GetValue<string>() == reward.DropInstanceId &&
                               node["status"]?.GetValue<string>() == "CLAIMED")];

            if (claimed.Count == 0)
                return false;

            List<DropsReward> sharing = [.. campaignRewards.Where(r => r.DropInstanceId == reward.DropInstanceId)];
            if (sharing.Count <= 1)
                return true;

            // Shared benefit id: the in-progress record's own claim flag is authoritative.
            if (hasInProgressRecord)
                return false;

            // No live record: attribute the earned entries of this campaign to the cheapest drops first.
            int earnedForCampaign = claimed.Count(node => node["campaign"]?["id"]?.GetValue<string>() == campaignId);
            int rank = sharing
                .OrderBy(r => r.RequiredMinutes)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ToList()
                .FindIndex(r => r.Id == reward.Id);

            return rank >= 0 && rank < earnedForCampaign;
        }
    }
}
