using System.Text.Json;
using Core.Logging;
using Core.Enums;
using System.IO;

namespace Core.Stores
{
    /// <summary>
    /// Thread-safe store of last-mined streamer URLs per platform and campaign slug.
    /// </summary>
    public sealed class LastMinedStreamersStore
    {
        private static readonly string DefaultFilePath = Path.Combine(
            Environment.ExpandEnvironmentVariables("%APPDATA%"),
            "Stream Drop Collector",
            "LastMinedStreamers.json");

        private readonly object _sync = new();
        private readonly Dictionary<string, string> _twitchBySlug = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _kickBySlug = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _filePath;

        /// <summary>
        /// Creates a store that loads remembered streamers from the default app-data path.
        /// </summary>
        public LastMinedStreamersStore()
            : this(DefaultFilePath)
        {
        }

        /// <summary>
        /// Creates a store that loads remembered streamers from the specified file path.
        /// </summary>
        /// <param name="filePath">Absolute path to the JSON cache file.</param>
        public LastMinedStreamersStore(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        /// <summary>
        /// Attempts to retrieve a remembered streamer URL for the given platform and campaign slug.
        /// </summary>
        /// <param name="platform">The streaming platform.</param>
        /// <param name="campaignSlug">Campaign slug key.</param>
        /// <param name="url">The remembered URL when found.</param>
        /// <returns><see langword="true"/> when a URL was found; otherwise <see langword="false"/>.</returns>
        public bool TryGet(Platform platform, string? campaignSlug, out string? url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(campaignSlug))
                return false;

            lock (_sync)
            {
                Dictionary<string, string> source = GetDictionary(platform);
                if (!source.TryGetValue(campaignSlug, out string? remembered) || string.IsNullOrWhiteSpace(remembered))
                    return false;

                url = remembered;
                return true;
            }
        }

        /// <summary>
        /// Remembers a streamer URL for the given platform and campaign slug, persisting when the value changes.
        /// </summary>
        /// <param name="platform">The streaming platform.</param>
        /// <param name="campaignSlug">Campaign slug key.</param>
        /// <param name="streamerUrl">Streamer channel URL to remember.</param>
        public void Remember(Platform platform, string? campaignSlug, string? streamerUrl)
        {
            if (string.IsNullOrWhiteSpace(campaignSlug) || string.IsNullOrWhiteSpace(streamerUrl))
                return;

            bool changed;

            lock (_sync)
            {
                Dictionary<string, string> target = GetDictionary(platform);
                changed = !target.TryGetValue(campaignSlug, out string? existing)
                          || !string.Equals(existing, streamerUrl, StringComparison.OrdinalIgnoreCase);

                if (changed)
                    target[campaignSlug] = streamerUrl;
            }

            if (changed)
                Save();
        }

        /// <summary>
        /// Removes a remembered streamer URL for the given platform and campaign slug.
        /// </summary>
        /// <param name="platform">The streaming platform.</param>
        /// <param name="campaignSlug">Campaign slug key.</param>
        public void Forget(Platform platform, string? campaignSlug)
        {
            if (string.IsNullOrWhiteSpace(campaignSlug))
                return;

            bool removed;

            lock (_sync)
            {
                removed = GetDictionary(platform).Remove(campaignSlug);
            }

            if (removed)
            {
                Save();
                AppLogger.Debug("Selection", $"Forgot remembered streamer for platform={platform}, campaignSlug='{campaignSlug}'.");
            }
        }

        private Dictionary<string, string> GetDictionary(Platform platform) =>
            platform == Platform.Twitch ? _twitchBySlug : _kickBySlug;

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                    return;

                string json = File.ReadAllText(_filePath);
                LastMinedStreamersState? state = JsonSerializer.Deserialize<LastMinedStreamersState>(json);
                if (state == null)
                    return;

                lock (_sync)
                {
                    _twitchBySlug.Clear();
                    _kickBySlug.Clear();

                    foreach ((string key, string value) in state.TwitchBySlug)
                    {
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                            _twitchBySlug[key] = value;
                    }

                    foreach ((string key, string value) in state.KickBySlug)
                    {
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                            _kickBySlug[key] = value;
                    }
                }

                AppLogger.Debug("StreamSelection", $"Loaded remembered streamers. twitch={_twitchBySlug.Count}, kick={_kickBySlug.Count}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("StreamSelection", $"Failed loading remembered streamers. {ex.Message}");
            }
        }

        private void Save()
        {
            try
            {
                LastMinedStreamersState snapshot;

                lock (_sync)
                {
                    snapshot = new LastMinedStreamersState
                    {
                        TwitchBySlug = _twitchBySlug.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
                        KickBySlug = _kickBySlug.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase)
                    };
                }

                string? directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("StreamSelection", $"Failed saving remembered streamers. {ex.Message}");
            }
        }

        private sealed class LastMinedStreamersState
        {
            public Dictionary<string, string> TwitchBySlug { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> KickBySlug { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}