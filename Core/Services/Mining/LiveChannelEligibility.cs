using Core.Models;

namespace Core.Services.Mining
{
    /// <summary>
    /// Shared eligibility rules for live-channel mining selection and health checks.
    /// </summary>
    public static class LiveChannelEligibility
    {
        /// <summary>
        /// Returns whether a channel snapshot satisfies mining requirements for the target game slug.
        /// </summary>
        /// <param name="snapshot">Channel metadata from the platform API.</param>
        /// <param name="gameSlug">Campaign category slug. When empty, any live channel is eligible.</param>
        public static bool IsEligible(LiveChannelSnapshot? snapshot, string? gameSlug)
        {
            if (snapshot == null || !snapshot.IsLive)
                return false;

            if (string.IsNullOrWhiteSpace(gameSlug))
                return true;

            return snapshot.CategorySlugs.Any(slug =>
                string.Equals(slug, gameSlug, StringComparison.OrdinalIgnoreCase));
        }
    }
}