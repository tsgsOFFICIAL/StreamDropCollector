using Core.Enums;
using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Twitch
{
    /// <summary>
    /// Twitch live-channel API: Helix for named campaigns, directory WebView discovery for general drops.
    /// </summary>
    public sealed class TwitchLiveChannelApi : ITwitchLiveChannelApi
    {
        private readonly ITwitchHelixService _helixService;
        private readonly TwitchGeneralDropDirectoryClient _generalDropDirectory;
        private readonly GeneralDropDiscoveryCache _generalDropCache;
        private readonly SemaphoreSlim _generalDropPreloadLock = new(1, 1);

        /// <summary>
        /// Initializes a new instance of the <see cref="TwitchLiveChannelApi"/> class.
        /// </summary>
        public TwitchLiveChannelApi(
            ITwitchHelixService helixService,
            Func<IWebViewHost?> webViewProvider,
            GeneralDropDiscoveryCache? generalDropCache = null)
        {
            _helixService = helixService ?? throw new ArgumentNullException(nameof(helixService));
            _generalDropCache = generalDropCache ?? new GeneralDropDiscoveryCache();
            _generalDropDirectory = new TwitchGeneralDropDirectoryClient(webViewProvider);
        }

        /// <summary>
        /// Fetches normalized channel metadata for a login slug via Twitch Helix.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The channel snapshot, or <see langword="null"/> when the channel could not be resolved.</returns>
        public Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default) =>
            _helixService.GetChannelAsync(channelLogin, ct);

        /// <summary>
        /// Resolves eligible channel logins for a campaign from connect URLs or general-drop directory discovery.
        /// </summary>
        /// <param name="campaign">Drop campaign to resolve streamers for.</param>
        /// <param name="allowDirectoryDiscovery">
        /// When <see langword="false"/>, general-drop campaigns return cached directory logins only (no WebView navigation).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Channel login slugs in viewer-rank order for general drops.</returns>
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
        /// Preloads directory-discovered logins for all general-drop Twitch campaigns (WebView, sequential).
        /// </summary>
        /// <param name="campaigns">Campaigns to inspect; only general-drop Twitch entries are discovered.</param>
        /// <param name="ct">Cancellation token.</param>
        public async Task PreloadGeneralDropDirectoriesAsync(IReadOnlyList<DropsCampaign> campaigns, CancellationToken ct = default)
        {
            List<DropsCampaign> generalDrops = campaigns
                .Where(c => c.Platform == Platform.Twitch && c.IsGeneralDrop)
                .ToList();

            if (generalDrops.Count == 0)
                return;

            await _generalDropPreloadLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                AppLogger.Debug(
                    "TwitchGeneralDrop",
                    $"Preload START count={generalDrops.Count} campaigns=[{string.Join(", ", generalDrops.Select(c => c.Name))}]");

                foreach (DropsCampaign campaign in generalDrops)
                {
                    ct.ThrowIfCancellationRequested();

                    if (_generalDropCache.TryGetFresh(campaign.Id, out _))
                        continue;

                    await GetEligibleLoginsAsync(campaign, allowDirectoryDiscovery: true, ct).ConfigureAwait(false);
                }

                AppLogger.Debug("TwitchGeneralDrop", "Preload DONE");
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
        /// <param name="campaign">Drop campaign whose eligible streamers and game id are evaluated.</param>
        /// <param name="preferredLogin">Optional login to try first (for example the last mined streamer).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The selected login slug, or <see langword="null"/> when no eligible live streamer is found.</returns>
        public async Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default)
        {
            AppLogger.Debug(
                "TwitchMining",
                $"SelectBestLiveLogin START campaign='{campaign.Name}' id={campaign.Id} slug='{campaign.Slug}' " +
                $"gameId={campaign.GameId ?? "(none)"} isGeneralDrop={campaign.IsGeneralDrop} " +
                $"helixAuth={_helixService.IsAuthenticated} preferredLogin={preferredLogin ?? "(none)"}");

            if (campaign.IsGeneralDrop)
                return await SelectBestGeneralDropLoginAsync(campaign, preferredLogin, ct).ConfigureAwait(false);

            return await SelectBestNamedCampaignLoginAsync(campaign, preferredLogin, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns whether <paramref name="channelLogin"/> is live and streaming the campaign's Twitch game.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="campaign">Drop campaign whose <see cref="DropsCampaign.GameId"/> is matched against Helix <c>game_id</c>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><see langword="true"/> when the channel is live and game-eligible; otherwise <see langword="false"/>.</returns>
        public async Task<bool> IsChannelEligibleAsync(string channelLogin, DropsCampaign campaign, CancellationToken ct = default)
        {
            LiveChannelSnapshot? snapshot = await _helixService.GetChannelAsync(channelLogin, ct).ConfigureAwait(false);
            bool eligible = TwitchChannelEligibility.IsEligible(snapshot, campaign);

            AppLogger.Debug(
                "TwitchMining",
                $"IsChannelEligible login={channelLogin} campaignGameId={campaign.GameId ?? "(none)"} helixAuth={_helixService.IsAuthenticated} " +
                $"snapshot={(snapshot is null ? "null" : $"live={snapshot.IsLive} gameId={snapshot.GameId ?? "(none)"}")} -> {eligible}");

            return eligible;
        }

        private async Task<string?> SelectBestGeneralDropLoginAsync(
            DropsCampaign campaign,
            string? preferredLogin,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(campaign.GameId))
            {
                AppLogger.Warn("TwitchMining", $"SelectBestLiveLogin ABORT general '{campaign.Name}' - missing game id.");
                return null;
            }

            IReadOnlyList<string> directoryLogins = await GetEligibleLoginsAsync(campaign, allowDirectoryDiscovery: true, ct)
                .ConfigureAwait(false);
            if (directoryLogins.Count == 0)
            {
                AppLogger.Warn("TwitchMining", $"SelectBestLiveLogin ABORT general '{campaign.Name}' - directory returned no streamers.");
                return null;
            }

            IEnumerable<string> ordered = LiveChannelSelector.BuildOrderedLogins(directoryLogins, preferredLogin);
            int verifiedCount = 0;

            foreach (string login in ordered)
            {
                ct.ThrowIfCancellationRequested();

                if (!await IsChannelEligibleAsync(login, campaign, ct).ConfigureAwait(false))
                {
                    AppLogger.Debug("TwitchMining", $"  general login={login} -> SKIP (Helix verify failed)");
                    continue;
                }

                verifiedCount++;
                AppLogger.Debug(
                    "TwitchMining",
                    $"SelectBestLiveLogin PICKED general login={login} campaignGameId={campaign.GameId} (directory rank #{verifiedCount})");
                return login;
            }

            AppLogger.Warn(
                "TwitchMining",
                $"SelectBestLiveLogin FAILED general '{campaign.Name}' directory={directoryLogins.Count} helixVerified=0");
            return null;
        }

        private async Task<string?> SelectBestNamedCampaignLoginAsync(
            DropsCampaign campaign,
            string? preferredLogin,
            CancellationToken ct)
        {
            IReadOnlyList<string> logins = EligibleStreamerParser.ParseChannelLogins(campaign);
            if (logins.Count == 0)
            {
                AppLogger.Warn(
                    "TwitchMining",
                    $"SelectBestLiveLogin ABORT campaign='{campaign.Name}' - no channel logins in campaign URLs.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(campaign.GameId))
            {
                AppLogger.Warn("TwitchMining", $"SelectBestLiveLogin ABORT campaign='{campaign.Name}' - missing Twitch game id.");
                return null;
            }

            AppLogger.Debug("TwitchMining", $"SelectBestLiveLogin candidates={logins.Count}: [{string.Join(", ", logins)}]");

            IEnumerable<string> ordered = LiveChannelSelector.BuildOrderedLogins(logins, preferredLogin);
            int liveCount = 0;
            int gameMatchCount = 0;

            foreach (string login in ordered)
            {
                ct.ThrowIfCancellationRequested();
                LiveChannelSnapshot? snapshot = await _helixService.GetChannelAsync(login, ct).ConfigureAwait(false);

                if (snapshot is null)
                {
                    AppLogger.Debug("TwitchMining", $"  login={login} -> SKIP (no snapshot / Helix miss)");
                    continue;
                }

                if (!snapshot.IsLive)
                {
                    AppLogger.Debug(
                        "TwitchMining",
                        $"  login={login} display='{snapshot.DisplayName}' -> SKIP offline gameId={snapshot.GameId ?? "(none)"}");
                    continue;
                }

                liveCount++;
                if (!TwitchChannelEligibility.IsEligible(snapshot, campaign))
                {
                    AppLogger.Debug(
                        "TwitchMining",
                        $"  login={login} display='{snapshot.DisplayName}' -> SKIP live but wrong game " +
                        $"campaignGameId={campaign.GameId} streamGameId={snapshot.GameId ?? "(none)"}");
                    continue;
                }

                gameMatchCount++;
                AppLogger.Debug(
                    "TwitchMining",
                    $"SelectBestLiveLogin PICKED login={login} display='{snapshot.DisplayName}' gameId={snapshot.GameId}");
                return login;
            }

            AppLogger.Warn(
                "TwitchMining",
                $"SelectBestLiveLogin FAILED campaign='{campaign.Name}' gameId={campaign.GameId} " +
                $"candidates={logins.Count} live={liveCount} gameMatch={gameMatchCount}");
            return null;
        }
    }
}