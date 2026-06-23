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

        /// <summary>
        /// Initializes a new instance of the <see cref="KickLiveChannelApi"/> class.
        /// </summary>
        /// <param name="hostProvider">Lazy provider for the Kick WebView host used for in-page API calls.</param>
        public KickLiveChannelApi(Func<IWebViewHost?> hostProvider)
        {
            _hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
            _client = new KickChannelApiClient(hostProvider);
        }

        /// <summary>
        /// Fetches normalized channel metadata for a login slug via Kick's channel API.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The channel snapshot, or <see langword="null"/> when the channel could not be resolved.</returns>
        public Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default) =>
            GetOrFetchSnapshotAsync(channelLogin, ct);

        /// <summary>
        /// Picks the best live channel login for a campaign, preferring <paramref name="preferredLogin"/> when eligible.
        /// </summary>
        /// <param name="campaign">Drop campaign whose eligible streamers and category slug are evaluated.</param>
        /// <param name="preferredLogin">Optional login to try first (for example the last mined streamer).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The selected login slug, or <see langword="null"/> when no eligible live streamer is found.</returns>
        public Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default) =>
            LiveChannelSelector.SelectBestLiveLoginAsync(
                campaign,
                preferredLogin,
                GetOrFetchSnapshotAsync,
                "KickSelection",
                ct);

        /// <summary>
        /// Returns whether <paramref name="channelLogin"/> is live and streaming the campaign's Kick category.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="campaign">Drop campaign whose <see cref="DropsCampaign.Slug"/> is matched against live categories.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><see langword="true"/> when the channel is live and category-eligible; otherwise <see langword="false"/>.</returns>
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