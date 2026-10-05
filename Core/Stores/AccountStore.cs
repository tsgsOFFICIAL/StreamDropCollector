using System.IO;
using System.Text.Json;
using Core.Enums;
using Core.Logging;
using Core.Models;

namespace Core.Stores
{
    /// <summary>
    /// Persists the list of configured accounts to <c>Accounts.json</c>.
    /// </summary>
    public sealed class AccountStore
    {
        private static readonly string DefaultFilePath = Path.Combine(
            Environment.ExpandEnvironmentVariables("%APPDATA%"),
            "Stream Drop Collector",
            "Accounts.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private readonly object _sync = new();
        private readonly string _filePath;

        /// <summary>
        /// Creates a store backed by the default <c>Accounts.json</c>.
        /// </summary>
        public AccountStore() : this(DefaultFilePath)
        {
        }

        /// <summary>
        /// Creates a store backed by the given file.
        /// </summary>
        public AccountStore(string filePath) => _filePath = filePath;

        /// <summary>
        /// Loads the saved accounts. On first run (no file) returns one legacy Twitch and one legacy Kick account that
        /// reuse the original browser profile so existing logins keep working.
        /// </summary>
        public List<AccountModel> Load()
        {
            lock (_sync)
            {
                try
                {
                    if (File.Exists(_filePath))
                    {
                        AccountsState? state = JsonSerializer.Deserialize<AccountsState>(File.ReadAllText(_filePath), JsonOptions);
                        if (state?.Accounts != null)
                            return state.Accounts;
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    AppLogger.Warn("Accounts", $"Failed loading accounts; using defaults. {ex.Message}");
                }
            }

            List<AccountModel> defaults =
            [
                new AccountModel { Id = "legacy-twitch", Platform = Platform.Twitch, DisplayName = "Twitch account", UsesLegacyProfile = true },
                new AccountModel { Id = "legacy-kick", Platform = Platform.Kick, DisplayName = "Kick account", UsesLegacyProfile = true }
            ];

            Save(defaults);
            return defaults;
        }

        /// <summary>
        /// Saves the given accounts.
        /// </summary>
        public void Save(IReadOnlyCollection<AccountModel> accounts)
        {
            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                    File.WriteAllText(_filePath, JsonSerializer.Serialize(new AccountsState { Accounts = [.. accounts] }, JsonOptions));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppLogger.Warn("Accounts", $"Failed saving accounts. {ex.Message}");
                }
            }
        }

        private sealed class AccountsState
        {
            public List<AccountModel> Accounts { get; set; } = [];
        }
    }
}