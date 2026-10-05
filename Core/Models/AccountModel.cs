using System.IO;
using Core.Enums;

namespace Core.Models
{
    /// <summary>
    /// Per-account overrides of global settings; unset values fall back to the global setting.
    /// </summary>
    public sealed class AccountSettingsOverrides
    {
        /// <summary>Overrides auto-claiming of rewards; <see langword="null"/> uses the global setting.</summary>
        public bool? AutoClaimRewards { get; set; }

        /// <summary>Overrides Kick level farming; <see langword="null"/> uses the global setting.</summary>
        public bool? KickLevelFarming { get; set; }

        /// <summary>Overrides the mining priority; <see langword="null"/> uses the global setting.</summary>
        public MiningPriorityMode? MiningPriorityMode { get; set; }

        /// <summary>
        /// Overrides the game filter: game slugs to allow (or exclude, see <see cref="GameFilterBlacklistMode"/>).
        /// <see langword="null"/> uses the global filter; an empty list allows every game.
        /// </summary>
        public List<string>? GameWhitelistSlugs { get; set; }

        /// <summary>When the game filter is overridden, mine everything except the listed games.</summary>
        public bool GameFilterBlacklistMode { get; set; }

        /// <summary>Gets a value indicating whether any setting is overridden.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasAny => AutoClaimRewards.HasValue || KickLevelFarming.HasValue
            || MiningPriorityMode.HasValue || GameWhitelistSlugs != null;
    }

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
        /// Gets or sets this account's optional overrides of the global settings.
        /// </summary>
        public AccountSettingsOverrides Overrides { get; set; } = new();

        /// <summary>
        /// Gets the WebView2 user-data folder for this account, or <see langword="null"/> for the legacy shared profile.
        /// </summary>
        public string? UserDataFolder => UsesLegacyProfile ? null : Path.Combine(AccountsRootFolder, Id);
    }
}