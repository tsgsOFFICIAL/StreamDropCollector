namespace Core.Mining
{
    /// <summary>
    /// Works out which rewards of a Twitch reward campaign's chest have been granted.
    /// </summary>
    /// <remarks>
    /// A chest is one watch-time group whose pool holds several rewards, and every completed window grants one of them
    /// (<c>grantCount</c> of them so far). Twitch does not say which; it is assumed to follow the pool's listed order.
    /// </remarks>
    public static class RewardCampaignPoolGrants
    {
        /// <summary>
        /// Gets the state of one pool reward.
        /// </summary>
        /// <param name="index">The reward's position in the pool.</param>
        /// <param name="grantCount">How many rewards of the group Twitch has granted.</param>
        /// <param name="watchedMinutes">The group's minutes in the current window.</param>
        /// <param name="requiredMinutes">The minutes a window needs.</param>
        /// <param name="groupClaimed">Whether Twitch marks the whole group as claimed.</param>
        /// <param name="windowExpiresAt">When the group's current window ends.</param>
        /// <returns>Whether the reward was granted, the progress to show for it, and when watching counts again (if it must wait).</returns>
        public static (bool Claimed, int ProgressMinutes, DateTimeOffset? AvailableAt) Resolve(
            int index, int grantCount, int watchedMinutes, int requiredMinutes, bool groupClaimed, DateTimeOffset? windowExpiresAt)
        {
            if (groupClaimed || index < grantCount)
                return (true, requiredMinutes, null);

            // This window already paid out its reward: nothing more is earned until it ends, then the count starts again.
            if (grantCount > 0 && watchedMinutes >= requiredMinutes)
                return (false, 0, windowExpiresAt is { } expires && expires > DateTimeOffset.Now ? expires : null);

            return (false, Math.Min(watchedMinutes, requiredMinutes), null);
        }

        /// <summary>Reads a group's <c>currentWindow.expiresAt</c>.</summary>
        public static DateTimeOffset? ReadWindowExpiry(System.Text.Json.Nodes.JsonObject? self) =>
            DateTimeOffset.TryParse(self?["currentWindow"]?["expiresAt"]?.GetValue<string>(), out DateTimeOffset expires) ? expires : null;
    }
}
