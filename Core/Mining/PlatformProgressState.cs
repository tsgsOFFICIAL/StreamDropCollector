using Core.Logging;

namespace Core.Mining
{
    /// <summary>
    /// Per-platform second counters used during live progress ticking.
    /// </summary>
    public sealed class PlatformProgressState
    {
        /// <summary>
        /// Total seconds mined toward the current campaign.
        /// </summary>
        public int MinedSeconds { get; set; }

        /// <summary>
        /// Seconds mined toward the current unclaimed reward.
        /// </summary>
        public int DropMinedSeconds { get; set; }

        /// <summary>
        /// Last whole-minute bucket already persisted to campaign reward progress.
        /// </summary>
        public int AppliedMinuteBucket { get; set; }

        /// <summary>
        /// Id of the last reward reported via drop-changed events.
        /// </summary>
        public string? LastReportedDropId { get; set; }

        /// <summary>
        /// Applies baseline values captured when a stream is selected.
        /// </summary>
        public void ApplyBaseline(MiningBaseline baseline)
        {
            AppLogger.Debug(
                "ProgressState",
                $"ApplyBaseline BEFORE minedSeconds={MinedSeconds} dropMinedSeconds={DropMinedSeconds} appliedBucket={AppliedMinuteBucket} " +
                $"AFTER minedSeconds={baseline.MinedSeconds} dropMinedSeconds={baseline.DropMinedSeconds} appliedBucket={baseline.AppliedMinuteBucket}");

            MinedSeconds = baseline.MinedSeconds;
            DropMinedSeconds = baseline.DropMinedSeconds;
            AppliedMinuteBucket = baseline.AppliedMinuteBucket;
        }

        /// <summary>
        /// Re-syncs the applied minute bucket from current mined seconds (used after UI reset).
        /// </summary>
        public void SyncAppliedBucketFromMinedSeconds()
        {
            int previous = AppliedMinuteBucket;
            AppliedMinuteBucket = MinedSeconds / 60;
            AppLogger.Debug("ProgressState", $"SyncAppliedBucketFromMinedSeconds previousBucket={previous} minedSeconds={MinedSeconds} newBucket={AppliedMinuteBucket}");
        }
    }
}