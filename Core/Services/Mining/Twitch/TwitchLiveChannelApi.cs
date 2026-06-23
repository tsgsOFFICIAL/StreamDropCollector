using Core.Enums;
using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using Core.Services.Twitch.Helix;

namespace Core.Services.Mining.Twitch
{
    /// <summary>
    /// Twitch live-channel API: Helix for named campaigns, directory WebView discovery for general drops.
    /// </summary>
    public sealed class TwitchLiveChannelApi : ITwitchLiveChannelApi
    {
        private readonly ITwitchHelixService _helixService;
        private readonly TwitchGeneralDropDirectoryClient _generalDropDirectory;
        private readonly TwitchGeneralDropDiscoveryCache _generalDropCache;
        private readonly SemaphoreSlim _generalDropPreloadLock = new(1, 1);

        /// <summary>
        /// Initializes a new instance of the <see cref="TwitchLiveChannelApi"/> class.
        /// </summary>
        public TwitchLiveChannelApi(
            ITwitchHelixService helixService,
            Func<IWebViewHost?> webViewProvider,
            TwitchGeneralDropDiscoveryCache? generalDropCache = null)
        {
            _helixService = helixService ?? throw new ArgumentNullException(nameof(helixService));
            _generalDropCache = generalDropCache ?? new TwitchGeneralDropDiscoveryCache();
            _generalDropDirectory = new TwitchGeneralDropDirectoryClient(webViewProvider);
        }

        /// <inheritdoc />
        public Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default) =>
            _helixService.GetChannelAsync(channelLogin, ct);

        /// <inheritdoc />
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
                .DiscoverLoginsAsync(campaign, TwitchGeneralDropDiscoveryCache.MaxLogins, ct)
                .ConfigureAwait(false);

            if (discovered.Count > 0)
                _generalDropCache.Set(campaign.Id, discovered);

            return discovered;
        }

        /// <inheritdoc />
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
                AppLogger.Info(
                    "TwitchGeneralDrop",
                    $"Preload START count={generalDrops.Count} campaigns=[{string.Join(", ", generalDrops.Select(c => c.Name))}]");

                foreach (DropsCampaign campaign in generalDrops)
                {
                    ct.ThrowIfCancellationRequested();

                    if (_generalDropCache.TryGetFresh(campaign.Id, out _))
                        continue;

                    await GetEligibleLoginsAsync(campaign, allowDirectoryDiscovery: true, ct).ConfigureAwait(false);
                }

                AppLogger.Info("TwitchGeneralDrop", "Preload DONE");
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

        /// <inheritdoc />
        public async Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default)
        {
            AppLogger.Info(
                "TwitchMining",
                $"SelectBestLiveLogin START campaign='{campaign.Name}' id={campaign.Id} slug='{campaign.Slug}' " +
                $"gameId={campaign.GameId ?? "(none)"} isGeneralDrop={campaign.IsGeneralDrop} " +
                $"helixAuth={_helixService.IsAuthenticated} preferredLogin={preferredLogin ?? "(none)"}");

            if (campaign.IsGeneralDrop)
                return await SelectBestGeneralDropLoginAsync(campaign, preferredLogin, ct).ConfigureAwait(false);

            return await SelectBestNamedCampaignLoginAsync(campaign, preferredLogin, ct).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> IsChannelEligibleAsync(string channelLogin, DropsCampaign campaign, CancellationToken ct = default)
        {
            LiveChannelSnapshot? snapshot = await _helixService.GetChannelAsync(channelLogin, ct).ConfigureAwait(false);
            bool eligible = TwitchChannelEligibility.IsEligible(snapshot, campaign);

            AppLogger.Info(
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

            IEnumerable<string> ordered = BuildOrderedLogins(directoryLogins, preferredLogin);
            int verifiedCount = 0;

            foreach (string login in ordered)
            {
                ct.ThrowIfCancellationRequested();

                if (!await IsChannelEligibleAsync(login, campaign, ct).ConfigureAwait(false))
                {
                    AppLogger.Info("TwitchMining", $"  general login={login} -> SKIP (Helix verify failed)");
                    continue;
                }

                verifiedCount++;
                AppLogger.Info(
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

            AppLogger.Info("TwitchMining", $"SelectBestLiveLogin candidates={logins.Count}: [{string.Join(", ", logins)}]");

            IEnumerable<string> ordered = BuildOrderedLogins(logins, preferredLogin);
            int liveCount = 0;
            int gameMatchCount = 0;

            foreach (string login in ordered)
            {
                ct.ThrowIfCancellationRequested();
                LiveChannelSnapshot? snapshot = await _helixService.GetChannelAsync(login, ct).ConfigureAwait(false);

                if (snapshot is null)
                {
                    AppLogger.Info("TwitchMining", $"  login={login} -> SKIP (no snapshot / Helix miss)");
                    continue;
                }

                if (!snapshot.IsLive)
                {
                    AppLogger.Info(
                        "TwitchMining",
                        $"  login={login} display='{snapshot.DisplayName}' -> SKIP offline gameId={snapshot.GameId ?? "(none)"}");
                    continue;
                }

                liveCount++;
                if (!TwitchChannelEligibility.IsEligible(snapshot, campaign))
                {
                    AppLogger.Info(
                        "TwitchMining",
                        $"  login={login} display='{snapshot.DisplayName}' -> SKIP live but wrong game " +
                        $"campaignGameId={campaign.GameId} streamGameId={snapshot.GameId ?? "(none)"}");
                    continue;
                }

                gameMatchCount++;
                AppLogger.Info(
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

        private static IEnumerable<string> BuildOrderedLogins(IReadOnlyList<string> logins, string? preferredLogin)
        {
            if (string.IsNullOrWhiteSpace(preferredLogin))
                return logins;

            return new[] { preferredLogin }
                .Concat(logins.Where(l => !string.Equals(l, preferredLogin, StringComparison.OrdinalIgnoreCase)));
        }
    }
}