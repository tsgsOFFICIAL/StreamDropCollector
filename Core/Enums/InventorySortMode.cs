namespace Core.Enums
{
    /// <summary>
    /// Defines how campaigns are ordered on the inventory page.
    /// </summary>
    public enum InventorySortMode
    {
        /// <summary>
        /// Keep the order the platform returned the campaigns in.
        /// </summary>
        Default = 0,
        /// <summary>
        /// Campaigns that end first are listed first.
        /// </summary>
        EndingSoonest = 1,
        /// <summary>
        /// Campaigns are grouped by game name, A to Z.
        /// </summary>
        GameName = 2,
        /// <summary>
        /// Campaigns needing the least remaining watch time to finish are listed first.
        /// </summary>
        LeastTimeRemaining = 3
    }
}
