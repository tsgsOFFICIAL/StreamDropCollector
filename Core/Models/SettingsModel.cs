namespace Core.Models
{
    internal class SettingsModel
    {
        public bool StartWithWindows { get; set; }
        public bool MinimizeToTrayOnStartup { get; set; }
        public string? Theme { get; set; }
        public bool AutoClaimRewards { get; set; }
        public bool NotifyOnDropUnlocked { get; set; }
        public bool NotifyOnReadyToClaim { get; set; }
        public bool NotifyOnAutoClaimed { get; set; }
    }
}