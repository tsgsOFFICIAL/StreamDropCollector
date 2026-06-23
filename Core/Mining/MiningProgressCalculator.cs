using Core.Managers;
using Core.Logging;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Computes live reward progress percentages for dashboard display.
    /// </summary>
    public static class MiningProgressCalculator
    {
        /// <summary>
        /// Calculates overall campaign completion from reward progress minutes.
        /// </summary>
        public static byte CalculateLiveCampaignProgress(DropsCampaign? campaign)
        {
            if (campaign == null)
                return 0;

            int totalRequiredMinutes = campaign.Rewards.Sum(r => r.RequiredMinutes);
            if (totalRequiredMinutes == 0)
                return 100;

            int effectiveMinutes = campaign.Rewards.Sum(r => Math.Min(r.ProgressMinutes, r.RequiredMinutes));
            double percentage = (double)effectiveMinutes / totalRequiredMinutes * 100;
            return (byte)Math.Clamp((int)Math.Floor(percentage), 0, 100);
        }

        /// <summary>
        /// Calculates progress toward the next unclaimed reward in a campaign.
        /// </summary>
        /// <param name="campaign">Campaign whose rewards are evaluated.</param>
        /// <param name="totalMinedSeconds">Seconds mined on the current stream for this campaign.</param>
        /// <returns>0–100 progress percentage, or 0 when no next reward exists.</returns>
        public static byte CalculateLiveDropProgress(DropsCampaign? campaign, int totalMinedSeconds)
        {
            if (campaign == null)
                return 0;

            List<DropsReward> unclaimedRewards = [.. campaign.Rewards.Where(r => !r.IsClaimed)];
            DropsReward? nextReward = unclaimedRewards
                .OrderBy(r => r.RequiredMinutes)
                .FirstOrDefault();

            if (nextReward == null)
            {
                AppLogger.Debug("RewardProgress", $"campaignId={campaign.Id}, no next unclaimed reward found; returning 0.");
                return 0;
            }

            int requiredSeconds = nextReward.RequiredMinutes * 60;
            int effectiveProgressSeconds = Math.Clamp(totalMinedSeconds, 0, requiredSeconds);
            double percentage = (double)effectiveProgressSeconds / requiredSeconds * 100;
            byte result = (byte)Math.Clamp((int)Math.Floor(percentage), 0, 100);

            AppLogger.Debug(
                "RewardProgress",
                $"campaignId={campaign.Id}, campaignName='{campaign.Name}', rewardsUnclaimed={unclaimedRewards.Count}, nextRewardId={nextReward.Id}, nextRewardName='{nextReward.Name}', requiredSeconds={requiredSeconds}, totalMinedSeconds={totalMinedSeconds}, effectiveProgressSeconds={effectiveProgressSeconds}, computedPct={result}");

            return result;
        }
    }
}