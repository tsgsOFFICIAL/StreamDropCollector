using Core.Managers;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Extension methods for evaluating progress and completion metrics on <see cref="DropsCampaign"/> instances.
    /// </summary>
    public static class DropsCampaignExtensions
    {
        /// <summary>
        /// Determines whether the campaign has unclaimed rewards that still need mine progress,
        /// using the current auto-claim setting from <see cref="UISettingsManager"/>.
        /// </summary>
        /// <param name="campaign">The campaign to evaluate.</param>
        /// <returns><see langword="true"/> when progress remains; otherwise <see langword="false"/>.</returns>
        public static bool HasProgressToMake(this DropsCampaign campaign) =>
            campaign.HasProgressToMake(UISettingsManager.Instance.AutoClaimRewards);

        /// <summary>
        /// Determines whether the campaign has unclaimed rewards that still need mine progress.
        /// </summary>
        /// <param name="campaign">The campaign to evaluate.</param>
        /// <param name="autoClaimRewards">When <see langword="true"/>, any unclaimed reward counts as progress to make.</param>
        /// <returns><see langword="true"/> when progress remains; otherwise <see langword="false"/>.</returns>
        public static bool HasProgressToMake(this DropsCampaign campaign, bool autoClaimRewards)
        {
            if (autoClaimRewards)
                return campaign.Rewards.Any(r => !r.IsClaimed);

            return campaign.Rewards.Any(r => !r.IsClaimed && r.ProgressMinutes < r.RequiredMinutes);
        }

        /// <summary>
        /// Determines whether the campaign contains rewards that are fully progressed and ready to claim.
        /// </summary>
        /// <param name="campaign">The campaign to evaluate.</param>
        /// <returns><see langword="true"/> when at least one reward is ready; otherwise <see langword="false"/>.</returns>
        public static bool HasReadyToClaimRewards(this DropsCampaign campaign) =>
            campaign.Rewards.Any(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes);

        /// <summary>
        /// Calculates average completion percentage across rewards that require watch time.
        /// </summary>
        /// <param name="campaign">The campaign to evaluate.</param>
        /// <returns>A value from 0 to 100, or 0 when no timed rewards exist.</returns>
        public static double CompletionPercentage(this DropsCampaign campaign)
        {
            IEnumerable<DropsReward> valid = campaign.Rewards.Where(r => r.RequiredMinutes > 0);

            if (!valid.Any())
                return 0;

            return valid.Average(r => (double)r.ProgressMinutes / r.RequiredMinutes) * 100;
        }
    }
}