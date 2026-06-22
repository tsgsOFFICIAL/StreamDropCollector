using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Outcome of a successful per-platform stream selection attempt.
    /// </summary>
    public sealed record PlatformMiningResult(
        DropsCampaign Campaign,
        string StreamUrl,
        string Login,
        MiningBaseline Baseline,
        DateTime? SuggestedNextCheckAt);
}