using System.Collections.Concurrent;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Kick live-channel API backed by in-page <c>GET /api/v2/channels/{slug}</c>.
    /// </summary>
    public sealed class KickLiveChannelApi : IKickLiveChannelApi
    {
        private static readonly TimeSpan SnapshotCacheTtl = TimeSpan.FromSeconds(20);

        private readonly Func<IWebViewHost?> _hostProvider;
        private readonly KickChannelApiClient _client;
        private readonly ConcurrentDictionary<string, CacheEntry> _snapshotCache = new(StringComparer.OrdinalIgnoreCase);

        public KickLiveChannelApi(Func<IWebViewHost?> hostProvider)
        {
            _hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
            _client = new KickChannelApiClient(hostProvider);
        }

        public Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default) =>
            GetOrFetchSnapshotAsync(channelLogin, ct);

        public Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default) =>
            LiveChannelSelector.SelectBestLiveLoginAsync(
                campaign,
                preferredLogin,
                GetOrFetchSnapshotAsync,
                "KickSelection",
                ct);

        public async Task<bool> IsChannelEligibleAsync(string channelLogin, DropsCampaign campaign, CancellationToken ct = default)
        {
            LiveChannelSnapshot? snapshot = await GetOrFetchSnapshotAsync(channelLogin, ct);
            bool eligible = LiveChannelEligibility.IsEligible(snapshot, campaign.Slug);

            AppLogger.Debug(
                "KickSelection",
                $"IsChannelEligible login={channelLogin}, slug={campaign.Slug}, live={snapshot?.IsLive == true}, categories=[{string.Join(", ", snapshot?.CategorySlugs ?? [])}] -> {eligible}");

            return eligible;
        }

        private async Task<LiveChannelSnapshot?> GetOrFetchSnapshotAsync(string channelLogin, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(channelLogin))
                return null;

            if (_hostProvider() == null)
                return null;

            string cacheKey = channelLogin.Trim().ToLowerInvariant();
            if (_snapshotCache.TryGetValue(cacheKey, out CacheEntry cached)
                && DateTime.UtcNow - cached.FetchedAtUtc < SnapshotCacheTtl)
            {
                return cached.Snapshot;
            }

            LiveChannelSnapshot? snapshot = await _client.GetChannelAsync(cacheKey, ct);
            if (snapshot != null)
                _snapshotCache[cacheKey] = new CacheEntry(snapshot, DateTime.UtcNow);

            return snapshot;
        }

        private sealed record CacheEntry(LiveChannelSnapshot Snapshot, DateTime FetchedAtUtc);
    }
}