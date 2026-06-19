using System.Text.Json;
using Core.Logging;
using System.Text;
using System.IO;

namespace Core.Stores
{
    /// <summary>
    /// Persists the user-pinned mining campaign id to disk.
    /// </summary>
    public sealed class PinnedCampaignStore
    {
        private static readonly string DefaultFilePath = Path.Combine(
            Environment.ExpandEnvironmentVariables("%APPDATA%"),
            "Stream Drop Collector",
            "PinnedCampaignCache.json");

        private readonly string _filePath;

        /// <summary>
        /// Gets the currently pinned campaign id, or <see langword="null"/> when none is pinned.
        /// </summary>
        public string? CampaignId { get; private set; }

        /// <summary>
        /// Creates a store that loads any existing pin from the default app-data path.
        /// </summary>
        public PinnedCampaignStore()
            : this(DefaultFilePath)
        {
        }

        /// <summary>
        /// Creates a store that loads any existing pin from the specified file path.
        /// </summary>
        /// <param name="filePath">Absolute path to the JSON cache file.</param>
        public PinnedCampaignStore(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        /// <summary>
        /// Updates the pinned campaign id and persists the change.
        /// </summary>
        /// <param name="campaignId">Campaign id to pin, or <see langword="null"/> to clear the pin.</param>
        public void SetCampaignId(string? campaignId)
        {
            CampaignId = campaignId;
            Save();
        }

        /// <summary>
        /// Clears the pinned campaign id and persists the change.
        /// </summary>
        public void Clear() => SetCampaignId(null);

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return;

                string json = File.ReadAllText(_filePath, Encoding.UTF8);
                PinnedCampaignCacheEntry? entry = JsonSerializer.Deserialize<PinnedCampaignCacheEntry>(json);

                if (entry != null && !string.IsNullOrWhiteSpace(entry.CampaignId))
                {
                    CampaignId = entry.CampaignId;
                    AppLogger.Info("Inventory", $"[PinnedCampaign] Restored pinned campaign '{CampaignId}' from disk.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Inventory", $"[PinnedCampaign] Failed to load cache. {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(
                    new PinnedCampaignCacheEntry { CampaignId = CampaignId },
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_filePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Inventory", $"[PinnedCampaign] Failed to save cache. {ex.Message}");
            }
        }

        private sealed class PinnedCampaignCacheEntry
        {
            public string? CampaignId { get; set; }
        }
    }
}