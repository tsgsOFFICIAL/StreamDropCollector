using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Seconds-based progress baselines captured when a platform mining session starts.
    /// </summary>
    public sealed record MiningBaseline(
        int MinedSeconds,
        int DropMinedSeconds,
        int AppliedMinuteBucket,
        DropsReward? NextReward);
}