using Core.Enums;
using Core.Logging;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Processes one live progress tick for a single platform mining session.
    /// </summary>
    public static class LiveProgressTracker
    {
        /// <summary>
        /// Increments counters, applies minute buckets, and raises progress callbacks.
        /// </summary>
        public static void ProcessTick(
            string platformLabel,
            Platform platform,
            DropsCampaign campaign,
            PlatformProgressState state,
            Action<DropsReward?> onDropChanged,
            Action<Platform, string, int> applyMinuteProgress,
            Action<byte, byte> onProgressChanged,
            Action<string, string>? verboseLog = null)
        {
            state.MinedSeconds++;
            state.DropMinedSeconds++;

            DropsReward? nextReward = campaign.Rewards
                .Where(r => !r.IsClaimed)
                .OrderBy(r => r.RequiredMinutes)
                .FirstOrDefault();

            verboseLog?.Invoke(
                "DropPointer",
                $"{platformLabel} nextReward={nextReward?.Name ?? "none"}, nextRewardId={nextReward?.Id ?? "none"}, requiredMinutes={nextReward?.RequiredMinutes ?? 0}, dropMinedSeconds={state.DropMinedSeconds}");

            RaiseDropChangedIfNeeded(nextReward, state, reward =>
                onDropChanged(reward));

            int minuteBucket = state.MinedSeconds / 60;
            if (minuteBucket > state.AppliedMinuteBucket)
            {
                int minutesToApply = minuteBucket - state.AppliedMinuteBucket;
                int previousBucket = state.AppliedMinuteBucket;
                state.AppliedMinuteBucket = minuteBucket;
                verboseLog?.Invoke(
                    "MinuteBucket",
                    $"{platformLabel} bucket ADVANCE campaignId={campaign.Id} previousBucket={previousBucket} newBucket={minuteBucket} minutesToApply={minutesToApply}");
                applyMinuteProgress(platform, campaign.Id, minutesToApply);
            }

            byte campaignPct = MiningProgressCalculator.CalculateLiveCampaignProgress(campaign);
            byte dropPct = MiningProgressCalculator.CalculateLiveDropProgress(campaign, state.DropMinedSeconds);

            verboseLog?.Invoke(
                "LiveProgress",
                $"{platformLabel} tick campaignId={campaign.Id}, campaignMinedSeconds={state.MinedSeconds}, dropMinedSeconds={state.DropMinedSeconds}, campaignPct={campaignPct}, dropPct={dropPct}");

            onProgressChanged(campaignPct, dropPct);
        }

        /// <summary>
        /// Raises a drop-changed callback only when the targeted reward changes.
        /// </summary>
        public static void RaiseDropChangedIfNeeded(
            DropsReward? reward,
            PlatformProgressState state,
            Action<DropsReward?> onDropChanged)
        {
            if (reward?.Id == state.LastReportedDropId)
                return;

            AppLogger.Debug(
                "DropPointer",
                $"RaiseDropChangedIfNeeded CHANGE previousDropId={state.LastReportedDropId ?? "(none)"} newDropId={reward?.Id ?? "(none)"} newDropName={reward?.Name ?? "(none)"}");

            state.LastReportedDropId = reward?.Id;
            onDropChanged(reward);
        }
    }
}