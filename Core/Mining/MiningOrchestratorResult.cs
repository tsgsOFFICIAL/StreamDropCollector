namespace Core.Mining
{
    /// <summary>
    /// Outcome of a single mining orchestration cycle.
    /// </summary>
    public sealed class MiningOrchestratorResult
    {
        /// <summary>
        /// When <see langword="true"/>, the cycle completed the full selection path (timers may start).
        /// When <see langword="false"/>, the cycle exited early (idle / no campaigns).
        /// </summary>
        public bool CompletedSelectionCycle { get; init; }

        /// <summary>
        /// Miner status label to publish after the cycle.
        /// </summary>
        public required string MinerStatus { get; init; }

        /// <summary>
        /// When to schedule the next stream re-evaluation.
        /// </summary>
        public DateTime NextCheckAt { get; init; }

        /// <summary>
        /// Twitch selection result, if a stream was chosen.
        /// </summary>
        public PlatformMiningResult? Twitch { get; init; }

        /// <summary>
        /// Kick selection result, if a stream was chosen.
        /// </summary>
        public PlatformMiningResult? Kick { get; init; }
    }
}