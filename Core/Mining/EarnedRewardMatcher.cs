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
            // Only entries earned in this campaign count (a benefit id can be shared across campaigns).
            List<JsonObject> claimed = [.. earnedDropRewardEdges.OfType<JsonObject>()
                .Select(edge => edge["node"]?.AsObject())
                .OfType<JsonObject>()
                .Where(node => node["item"]?["id"]?.GetValue<string>() == reward.DropInstanceId &&
                               node["status"]?.GetValue<string>() == "CLAIMED")
                .Where(node => node["campaign"]?["id"]?.GetValue<string>() is not { } earnedCampaignId ||
                               earnedCampaignId == campaignId)];

            if (claimed.Count == 0)
                return false;

            List<DropsReward> sharing = [.. campaignRewards.Where(r => r.DropInstanceId == reward.DropInstanceId)];
            if (sharing.Count <= 1)
                return true;

            // Shared benefit id: the in-progress record's own claim flag is authoritative.
            if (hasInProgressRecord)
                return false;

            // No live record: attribute the earned entries of this campaign to the cheapest drops first.
            int earnedForCampaign = claimed.Count;
            int rank = sharing
                .OrderBy(r => r.RequiredMinutes)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ToList()
                .FindIndex(r => r.Id == reward.Id);

            return rank >= 0 && rank < earnedForCampaign;
        }

        /// <summary>
        /// Determines whether the claimed-drops history ("gameEventDropsConnection") proves <paramref name="reward"/>
        /// was already claimed in <paramref name="campaign"/>.
        /// </summary>
        /// <remarks>
        /// The history only records each benefit's latest award time, not the campaign it came from, so it is used as a
        /// last resort, for exactly the case the other sources cannot see: a campaign finished so long ago that Twitch
        /// no longer lists it as in progress and its claims have dropped off the capped "earnedDropRewards" list.
        /// </remarks>
        /// <param name="reward">The drop being evaluated.</param>
        /// <param name="campaign">The campaign that owns the drop.</param>
        /// <param name="lastAwardedByBenefitId">Latest award time per benefit id from the claimed-drops history.</param>
        /// <param name="hasInProgressRecord">Whether the server returned an in-progress record (with its own claim flag) for the drop.</param>
        /// <param name="oldestEarnedAt">Time of the oldest entry in "earnedDropRewards", or <see langword="null"/> when that list is empty or unavailable.</param>
        /// <returns><see langword="true"/> when the history proves the drop was claimed.</returns>
        public static bool IsClaimedByAwardHistory(
            DropsReward reward,
            DropsCampaign campaign,
            IReadOnlyDictionary<string, DateTimeOffset> lastAwardedByBenefitId,
            bool hasInProgressRecord,
            DateTimeOffset? oldestEarnedAt)
        {
            // The in-progress record's own claim flag is authoritative while Twitch still tracks the drop.
            if (hasInProgressRecord)
                return false;

            // A drop can grant several benefits (one reward entry each); it only counts as claimed when all were awarded.
            List<DropsReward> benefitsOfDrop = [.. campaign.Rewards.Where(r => r.Id == reward.Id)];
            if (benefitsOfDrop.Count == 0)
                benefitsOfDrop.Add(reward);

            foreach (DropsReward benefit in benefitsOfDrop)
            {
                if (string.IsNullOrEmpty(benefit.DropInstanceId) ||
                    !lastAwardedByBenefitId.TryGetValue(benefit.DropInstanceId, out DateTimeOffset awardedAt))
                    return false;

                // One award time cannot tell apart several drops that share a benefit id.
                if (campaign.Rewards.Any(r => r.DropInstanceId == benefit.DropInstanceId && r.Id != benefit.Id))
                    return false;

                // Awarded before the campaign started (or after it ends): that was another campaign's copy.
                if (awardedAt < campaign.StartsAt || awardedAt > campaign.EndsAt)
                    return false;

                // An award recent enough to still be on the earned list is already decided there, with its campaign
                // id. Letting the history overrule it would hide a drop whose benefit was just earned in a sibling
                // campaign running at the same time.
                if (oldestEarnedAt is { } oldest && awardedAt >= oldest)
                    return false;
            }

            return true;
        }
    }
}
