using Core.Enums;

namespace Core.Models
{
    /// <summary>
    /// Represents persisted application settings serialized to and from disk.
    /// </summary>
    internal class SettingsModel
    {
        /// <summary>
        /// Gets or sets a value indicating whether the application starts automatically when Windows starts.
        /// </summary>
        public bool StartWithWindows { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the application should start minimized to the system tray on startup.
        /// </summary>
        public bool MinimizeToTrayOnStartup { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the application continues running in the background when the main window is closed.
        /// </summary>
        public bool RunInBackground { get; set; }
        /// <summary>
        /// Gets or sets the name of the application theme.
        /// </summary>
        public string? Theme { get; set; }
        /// <summary>
        /// Gets or sets the frequency at which the application checks for updates.
        /// </summary>
        public UpdateFrequency UpdateFrequency { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether rewards are automatically claimed when they become available.
        /// </summary>
        public bool AutoClaimRewards { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether a notification is sent when a drop reward is unlocked.
        /// </summary>
        public bool NotifyOnDropUnlocked { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether a notification is sent when rewards are ready to be claimed.
        /// </summary>
        public bool NotifyOnReadyToClaim { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether a notification is sent when rewards are automatically claimed.
        /// </summary>
        public bool NotifyOnAutoClaimed { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether verbose debug messages are written to the application log.
        /// </summary>
        public bool VerboseDebugLogging { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether a newer application version is available.
        /// </summary>
        public bool UpdateAvailable { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether a notification is sent when a new update is available.
        /// </summary>
        public bool NotifyOnNewUpdateAvailable { get; set; }
        /// <summary>
        /// Gets or sets the UTC date and time of the last update check, if one has been performed.
        /// </summary>
        public DateTime? LastUpdateCheck { get; set; }
        /// <summary>
        /// Gets or sets the strategy used to prioritize active drop campaigns during mining.
        /// </summary>
        public MiningPriorityMode MiningPriorityMode { get; set; } = MiningPriorityMode.AvailabilityThenProgress;
        /// <summary>
        /// Gets or sets the Twitch game slug whitelist used to filter eligible campaigns.
        /// </summary>
        public List<string> TwitchGameWhitelistSlugs { get; set; } = new List<string>();
        /// <summary>
        /// Gets or sets the Kick game slug whitelist used to filter eligible campaigns.
        /// </summary>
        public List<string> KickGameWhitelistSlugs { get; set; } = new List<string>();
        /// <summary>
        /// Gets or sets a value indicating whether the Twitch game filter operates as a blacklist instead of a whitelist.
        /// </summary>
        public bool TwitchGameFilterBlacklistMode { get; set; }
        /// <summary>
        /// Gets or sets a value indicating whether the Kick game filter operates as a blacklist instead of a whitelist.
        /// </summary>
        public bool KickGameFilterBlacklistMode { get; set; }
    }
}