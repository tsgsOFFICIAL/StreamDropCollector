using System.Collections.Concurrent;
using System.Linq;
using Core.Enums;
using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using Core.Services.Mining;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Kick live-channel API backed by in-page <c>GET /api/v2/channels/{slug}</c>, with directory WebView discovery for general drops.
    /// </summary>
    public sealed class KickLiveChannelApi : IKickLiveChannelApi
    {
        private static readonly TimeSpan SnapshotCacheTtl = TimeSpan.FromSeconds(20);

        private readonly Func<IWebViewHost?> _hostProvider;
        private readonly KickChannelApiClient _client;
        private readonly KickGeneralDropDirectoryClient _generalDropDirectory;
        private readonly GeneralDropDiscoveryCache _generalDropCache;
        private readonly SemaphoreSlim _generalDropPreloadLock = new(1, 1);
        private readonly ConcurrentDictionary<string, CacheEntry> _snapshotCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="KickLiveChannelApi"/> class.
        /// </summary>
        /// <param name="hostProvider">Lazy provider for the Kick WebView host used for in-page API calls.</param>
        /// <param name="generalDropCache">Optional shared cache for general-drop directory discovery results.</param>
        public KickLiveChannelApi(Func<IWebViewHost?> hostProvider, GeneralDropDiscoveryCache? generalDropCache = null)
        {
            _hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
            _client = new KickChannelApiClient(hostProvider);
            _generalDropCache = generalDropCache ?? new GeneralDropDiscoveryCache();
            _generalDropDirectory = new KickGeneralDropDirectoryClient(hostProvider);
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
        /// Resolves eligible channel logins for a campaign from connect URLs or general-drop directory discovery.
        /// </summary>
        /// <param name="campaign">Drop campaign to resolve streamers for.</param>
        /// <param name="allowDirectoryDiscovery">
        /// When <see langword="false"/>, general-drop campaigns return cached directory logins only (no WebView navigation).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Channel login slugs in page order for general drops.</returns>
        public async Task<IReadOnlyList<string>> GetEligibleLoginsAsync(
            DropsCampaign campaign,
            bool allowDirectoryDiscovery = true,
            CancellationToken ct = default)
        {
            if (!campaign.IsGeneralDrop)
                return EligibleStreamerParser.ParseChannelLogins(campaign);

            if (_generalDropCache.TryGetFresh(campaign.Id, out IReadOnlyList<string> cached))
                return cached;

            if (!allowDirectoryDiscovery)
                return _generalDropCache.Get(campaign.Id);

            IReadOnlyList<string> discovered = await _generalDropDirectory
                .DiscoverLoginsAsync(campaign, GeneralDropDiscoveryCache.MaxLogins, ct)
                .ConfigureAwait(false);

            if (discovered.Count > 0)
                _generalDropCache.Set(campaign.Id, discovered);

            return discovered;
        }

        /// <summary>
        /// Preloads directory-discovered logins for all general-drop Kick campaigns (WebView, sequential).
        /// </summary>
        /// <param name="campaigns">Campaigns to inspect; only general-drop Kick entries are discovered.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task PreloadGeneralDropDirectoriesAsync(IReadOnlyList<DropsCampaign> campaigns, CancellationToken ct = default)
        {
            List<DropsCampaign> generalDrops = campaigns
                .Where(c => c.Platform == Platform.Kick && c.IsGeneralDrop)
                .ToList();

            if (generalDrops.Count == 0)
                return;

            await _generalDropPreloadLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                AppLogger.Debug(
                    "KickGeneralDrop",
                    $"Preload START count={generalDrops.Count} campaigns=[{string.Join(", ", generalDrops.Select(c => c.Name))}]");

                foreach (DropsCampaign campaign in generalDrops)
                {
                    ct.ThrowIfCancellationRequested();

                    if (_generalDropCache.TryGetFresh(campaign.Id, out _))
                        continue;

                    await GetEligibleLoginsAsync(campaign, allowDirectoryDiscovery: true, ct).ConfigureAwait(false);
                }

                AppLogger.Debug("KickGeneralDrop", "Preload DONE");
            }
            finally
            {
                _generalDropPreloadLock.Release();
            }
        }

        /// <summary>
        /// Gets the most recently cached directory logins for a general drop campaign (may be empty or stale).
        /// </summary>
        public IReadOnlyList<string> GetCachedGeneralDropLogins(string campaignId) =>
            _generalDropCache.Get(campaignId);

        /// <summary>
        /// Picks the best live channel login for a campaign, preferring <paramref name="preferredLogin"/> when eligible.
        /// </summary>
        /// <param name="campaign">Drop campaign whose eligible streamers and category slug are evaluated.</param>
        /// <param name="preferredLogin">Optional login to try first (for example the last mined streamer).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The selected login slug, or <see langword="null"/> when no eligible live streamer is found.</returns>
        public Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default)
        {
            AppLogger.Debug(
                "KickSelection",
                $"SelectBestLiveLogin START campaign='{campaign.Name}' id={campaign.Id} slug='{campaign.Slug}' " +
                $"isGeneralDrop={campaign.IsGeneralDrop} preferredLogin={preferredLogin ?? "(none)"}");

            if (campaign.IsGeneralDrop)
                return SelectBestGeneralDropLoginAsync(campaign, preferredLogin, ct);

            return LiveChannelSelector.SelectBestLiveLoginAsync(
                campaign,
                preferredLogin,
                GetOrFetchSnapshotAsync,
                "KickSelection",
                ct);
        }

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

        private async Task<string?> SelectBestGeneralDropLoginAsync(
            DropsCampaign campaign,
            string? preferredLogin,
            CancellationToken ct)
        {
            // Unlike Twitch, Kick's fully-open campaigns have an empty Slug by design, and
            // LiveChannelEligibility.IsEligible treats an empty slug as "any live channel is eligible" -
            // so discovery is always attempted, regardless of whether a category slug is present.
            IReadOnlyList<string> directoryLogins = await GetEligibleLoginsAsync(campaign, allowDirectoryDiscovery: true, ct)
                .ConfigureAwait(false);
            if (directoryLogins.Count == 0)
            {
                AppLogger.Warn("KickSelection", $"SelectBestLiveLogin ABORT general '{campaign.Name}' - directory returned no streamers.");
                return null;
            }

            IEnumerable<string> ordered = LiveChannelSelector.BuildOrderedLogins(directoryLogins, preferredLogin);
            int verifiedCount = 0;

            foreach (string login in ordered)
            {
                ct.ThrowIfCancellationRequested();

                if (!await IsChannelEligibleAsync(login, campaign, ct).ConfigureAwait(false))
                {
                    AppLogger.Debug("KickSelection", $"  general login={login} -> SKIP (not live/category eligible)");
                    continue;
                }

                verifiedCount++;
                AppLogger.Debug(
                    "KickSelection",
                    $"SelectBestLiveLogin PICKED general login={login} slug={campaign.Slug} (directory rank #{verifiedCount})");
                return login;
            }

            AppLogger.Warn(
                "KickSelection",
                $"SelectBestLiveLogin FAILED general '{campaign.Name}' directory={directoryLogins.Count} verified=0");
            return null;
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