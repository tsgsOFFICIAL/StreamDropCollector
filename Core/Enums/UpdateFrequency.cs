namespace Core.Enums
{
    /// <summary>
    /// Specifies how often the application checks for available updates.
    /// </summary>
    public enum UpdateFrequency
    {
        /// <summary>
        /// Check for updates each time the application launches.
        /// </summary>
        OnLaunch,
        /// <summary>
        /// Check for updates once per day.
        /// </summary>
        Daily,
        /// <summary>
        /// Check for updates once per week.
        /// </summary>
        Weekly,
        /// <summary>
        /// Do not automatically check for updates.
        /// </summary>
        Never
    }
}