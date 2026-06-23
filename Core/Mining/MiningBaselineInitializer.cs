using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Computes mining progress baselines and next-reward scheduling hints from campaign reward state.
    /// </summary>
    public static class MiningBaselineInitializer
    {
        /// <summary>
        /// Builds second-based baselines for live progress ticking after a stream is selected.
        /// </summary>
        public static MiningBaseline Create(DropsCampaign campaign)
        {
            int minedSeconds = campaign.Rewards
                .Sum(r => Math.Min(r.ProgressMinutes, r.RequiredMinutes) * 60);

            DropsReward? nextReward = campaign.Rewards
                .Where(r => !r.IsClaimed)
                .OrderBy(r => r.RequiredMinutes)
                .FirstOrDefault();

            int minutesBeforeNextReward = nextReward == null
                ? 0
                : campaign.Rewards
                    .Where(r => !r.IsClaimed && r.RequiredMinutes < nextReward.RequiredMinutes)
                    .Sum(r => r.RequiredMinutes);

            int dropMinedSeconds = Math.Max(0, (nextReward?.ProgressMinutes ?? 0) - minutesBeforeNextReward) * 60;

            return new MiningBaseline(
                minedSeconds,
                dropMinedSeconds,
                minedSeconds / 60,
                nextReward);
        }

        /// <summary>
        /// Estimates when the soonest unclaimed reward will complete, if any remain.
        /// </summary>
        public static DateTime? EstimateSoonestRewardCompletion(DropsCampaign campaign)
        {
            DropsReward? soonest = campaign.Rewards
                .Where(r => !r.IsClaimed && r.ProgressMinutes < r.RequiredMinutes)
                .OrderBy(r => r.RequiredMinutes - r.ProgressMinutes)
                .FirstOrDefault();

            if (soonest == null)
                return null;

            return DateTime.Now.AddMinutes(soonest.RequiredMinutes - soonest.ProgressMinutes);
        }

        /// <summary>
        /// Returns the earlier of two optional schedule times.
        /// </summary>
        public static DateTime Earliest(DateTime current, DateTime? candidate) =>
            candidate.HasValue && candidate.Value < current ? candidate.Value : current;
    }
}