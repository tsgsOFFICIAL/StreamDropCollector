using Core.Mining.Kick;
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
            if (campaign == null || campaign.IsLevelFarming())
                return 0;

            int totalRequiredMinutes = campaign.Rewards.Sum(r => r.RequiredMinutes);
            if (totalRequiredMinutes == 0)
                return 100;

            int effectiveMinutes = campaign.Rewards.Sum(r => Math.Min(r.ProgressMinutes, r.RequiredMinutes));
            double percentage = (double)effectiveMinutes / totalRequiredMinutes * 100;
            byte result = (byte)Math.Clamp((int)Math.Floor(percentage), 0, 100);

            AppLogger.Debug(
                "CampaignProgress",
                $"campaignId={campaign.Id}, campaignName='{campaign.Name}', totalRequiredMinutes={totalRequiredMinutes}, effectiveMinutes={effectiveMinutes}, computedPct={result}");

            return result;
        }

        /// <summary>
        /// Calculates the seconds left until the whole campaign and the next unclaimed reward are complete.
        /// </summary>
        /// <param name="campaign">Campaign being mined, or <see langword="null"/> when nothing is selected.</param>
        /// <param name="state">Live mining counters for the platform.</param>
        /// <returns>Seconds remaining for the campaign and for the next reward; 0 when unknown or finished.</returns>
        public static (int Campaign, int Drop) CalculateRemainingSeconds(DropsCampaign? campaign, PlatformProgressState state)
        {
            if (campaign == null || campaign.IsLevelFarming())
                return (0, 0);

            // Reward minutes only advance once per minute bucket; credit the seconds mined since the last bucket.
            int secondsIntoMinute = Math.Max(0, state.MinedSeconds - state.AppliedMinuteBucket * 60);
            int campaignSeconds = campaign.RemainingMinutes > 0
                ? Math.Max(0, campaign.RemainingMinutes * 60 - secondsIntoMinute)
                : 0;

            DropsReward? nextReward = campaign.Rewards
                .Where(r => !r.IsClaimed)
                .OrderBy(r => r.RequiredMinutes)
                .FirstOrDefault();

            int dropSeconds = nextReward == null
                ? 0
                : Math.Max(0, nextReward.RequiredMinutes * 60 - state.DropMinedSeconds);

            return (campaignSeconds, dropSeconds);
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