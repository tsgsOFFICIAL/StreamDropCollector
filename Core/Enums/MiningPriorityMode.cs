namespace Core.Enums
{
    /// <summary>
    /// Defines how active drop campaigns are prioritized when selecting which campaign to mine.
    /// </summary>
    public enum MiningPriorityMode
    {
        /// <summary>
        /// Prefer campaigns with available live channels, then by completion percentage and time to the next reward.
        /// </summary>
        AvailabilityThenProgress = 0,
        /// <summary>
        /// Prefer campaigns ending soonest, then by time remaining to the next unclaimed reward.
        /// </summary>
        EndingSoonest = 1,
        /// <summary>
        /// Prefer campaigns with the least time required to reach the next unclaimed reward.
        /// </summary>
        LeastTimeToNextReward = 2,
        /// <summary>
        /// Prefer campaigns with the highest overall completion percentage.
        /// </summary>
        HighestCompletion = 3
    }
}