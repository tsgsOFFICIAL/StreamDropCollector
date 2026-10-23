namespace Core.Models
{
    /// <summary>
    /// A Kick gamification challenge (for example the daily reward) for the logged-in account.
    /// </summary>
    public sealed record KickChallenge(
        string Id,
        string Name,
        string Recurrence,
        string Status,
        string ConditionType,
        long Progress,
        long Threshold,
        DateTimeOffset? WindowEndsAt)
    {
        /// <summary>Gets a value indicating whether this is the daily challenge.</summary>
        public bool IsDaily => string.Equals(Recurrence, "daily", StringComparison.OrdinalIgnoreCase);

        /// <summary>Gets a value indicating whether the challenge has been claimed already.</summary>
        public bool IsClaimed => string.Equals(Status, "claimed", StringComparison.OrdinalIgnoreCase);

        /// <summary>Gets a value indicating whether the watch-time threshold has been reached and the reward not yet claimed.</summary>
        public bool IsClaimable => !IsClaimed && Threshold > 0 && Progress >= Threshold;
    }
}
