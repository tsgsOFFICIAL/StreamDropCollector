using System.IO;
using Core.Enums;

namespace Core.Models
{
    /// <summary>
    /// A persisted streaming-platform account. Each account owns an isolated WebView2 browser profile.
    /// </summary>
    public sealed class AccountModel
    {
        /// <summary>
        /// Root folder holding the per-account WebView2 profiles.
        /// </summary>
        public static readonly string AccountsRootFolder = Path.Combine(
            Environment.ExpandEnvironmentVariables("%APPDATA%"),
            "Stream Drop Collector",
            "Accounts");

        /// <summary>
        /// Gets or sets the unique account identifier.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the platform this account belongs to.
        /// </summary>
        public Platform Platform { get; set; }

        /// <summary>
        /// Gets or sets the display name (platform username once known).
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the account takes part in mining.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether the account uses the app's original shared browser profile
        /// (accounts that existed before multi-account support).
        /// </summary>
        public bool UsesLegacyProfile { get; set; }

        /// <summary>
        /// Gets the WebView2 user-data folder for this account, or <see langword="null"/> for the legacy shared profile.
        /// </summary>
        public string? UserDataFolder => UsesLegacyProfile ? null : Path.Combine(AccountsRootFolder, Id);
    }
}