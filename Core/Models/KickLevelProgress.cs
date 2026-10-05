namespace Core.Models
{
    /// <summary>
    /// Kick viewer level progress for the logged-in account.
    /// </summary>
    /// <param name="Level">Current level (the number on the badge).</param>
    /// <param name="ProgressXp">XP earned within the current level.</param>
    /// <param name="XpToNextLevel">XP still needed to reach the next level.</param>
    /// <param name="TotalXp">Lifetime XP, when reported.</param>
    /// <param name="BadgeImageUrl">Kick's level badge image, when reported.</param>
    public sealed record KickLevelProgress(int Level, long ProgressXp, long XpToNextLevel, long? TotalXp, string? BadgeImageUrl = null)
    {
        /// <summary>
        /// Gets the exact completion percentage (0-100) of the current level.
        /// </summary>
        public double PercentExact
        {
            get
            {
                long size = ProgressXp + XpToNextLevel;
                return size <= 0 ? 0 : Math.Clamp(ProgressXp * 100.0 / size, 0, 100);
            }
        }

        /// <summary>
        /// Gets the completion percentage (0-100) of the current level.
        /// </summary>
        public byte Percent
        {
            get
            {
                long size = ProgressXp + XpToNextLevel;
                return size <= 0 ? (byte)0 : (byte)Math.Clamp(ProgressXp * 100 / size, 0, 100);
            }
        }
    }
}