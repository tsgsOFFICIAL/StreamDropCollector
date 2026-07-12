using System.Collections.Concurrent;

namespace Core.Services.Mining
{
    /// <summary>
    /// Short-lived cache of directory-discovered logins for general drop campaigns.
    /// </summary>
    public sealed class GeneralDropDiscoveryCache
    {
        public const int MaxLogins = 10;
        private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

        private readonly ConcurrentDictionary<string, CacheEntry> _entries =
            new(StringComparer.OrdinalIgnoreCase);

        private sealed record CacheEntry(IReadOnlyList<string> Logins, DateTime FetchedAtUtc);

        /// <summary>
        /// Gets cached logins for a campaign when the entry is still within TTL.
        /// </summary>
        public bool TryGetFresh(string campaignId, out IReadOnlyList<string> logins)
        {
            logins = Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(campaignId))
                return false;

            if (!_entries.TryGetValue(campaignId, out CacheEntry? entry))
                return false;

            if (DateTime.UtcNow - entry.FetchedAtUtc > Ttl)
                return false;

            logins = entry.Logins;
            return logins.Count > 0;
        }

        /// <summary>
        /// Gets the most recent cached logins regardless of TTL (for UI display while a refresh is pending).
        /// </summary>
        public IReadOnlyList<string> Get(string campaignId)
        {
            if (string.IsNullOrWhiteSpace(campaignId))
                return Array.Empty<string>();

            return _entries.TryGetValue(campaignId, out CacheEntry? entry)
                ? entry.Logins
                : Array.Empty<string>();
        }

        /// <summary>
        /// Stores directory-discovered logins for a campaign.
        /// </summary>
        public void Set(string campaignId, IReadOnlyList<string> logins)
        {
            if (string.IsNullOrWhiteSpace(campaignId))
                return;

            _entries[campaignId] = new CacheEntry(logins, DateTime.UtcNow);
        }
    }
}