namespace Core.Enums
{
    /// <summary>
    /// Identifies the kind of application notification being shown.
    /// </summary>
    public enum NotificationType
    {
        /// <summary>
        /// A drop reward was claimed automatically.
        /// </summary>
        DropClaimed,
        /// <summary>
        /// One or more drop rewards are ready to be claimed manually.
        /// </summary>
        DropReadyToClaim,
        /// <summary>
        /// Claiming a drop reward failed and will be retried.
        /// </summary>
        DropClaimFailed,
        /// <summary>
        /// A reward cannot be claimed until the game account is linked.
        /// </summary>
        AccountNotLinked,
        /// <summary>
        /// A new application version is available.
        /// </summary>
        UpdateAvailable,
        /// <summary>
        /// The application was updated successfully.
        /// </summary>
        AppUpdated,
        /// <summary>
        /// The application was minimized to the system tray. Local only, never sent to the webhook.
        /// </summary>
        MinimizedToTray
    }
}
