using System.Text.Json.Serialization;

namespace Core.Models
{
    /// <summary>
    /// Represents information about an update.
    /// </summary>
    public class UpdateInfo
    {
        /// <summary>
        /// Gets or sets the version string of the update.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; }
        /// <summary>
        /// Gets or sets the release type of the update (for example, stable or beta).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        /// <summary>
        /// Gets or sets the changelog text describing changes in this update.
        /// </summary>
        [JsonPropertyName("changelog")]
        public string? Changelog { get; set; }
        /// <summary>
        /// Gets or sets the list of previous release versions and their metadata.
        /// </summary>
        [JsonPropertyName("historic_versions")]
        public List<HistoricVersion>? HistoricVersions { get; set; }

        /// <summary>
        /// Represents historical version information.
        /// </summary>
        public class HistoricVersion
        {
            /// <summary>
            /// Gets or sets the version string of the historical release.
            /// </summary>
            [JsonPropertyName("version")]
            public string? Version { get; set; }
            /// <summary>
            /// Gets or sets the release type of the historical version.
            /// </summary>
            [JsonPropertyName("type")]
            public string? Type { get; set; }
            /// <summary>
            /// Gets or sets the changelog text for the historical release.
            /// </summary>
            [JsonPropertyName("changelog")]
            public string? Changelog { get; set; }
        }
    }
}