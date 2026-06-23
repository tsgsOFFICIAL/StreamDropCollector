using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using Core.Services.Twitch.Helix;

namespace Core.Services.Mining.Twitch
{
    /// <summary>
    /// Twitch live-channel API backed by Helix REST and mine-only EventSub (no DOM scraping).
    /// </summary>
    /// <remarks>
    /// Delegates channel lookups to <see cref="ITwitchHelixService"/>. Streamer selection and health
    /// checks use cached Helix snapshots; real-time updates for the mined channel are handled by EventSub
    /// inside the Helix service.
    /// </remarks>
    public sealed class TwitchLiveChannelApi : ITwitchLiveChannelApi
    {
        private readonly ITwitchHelixService _helixService;

        /// <summary>
        /// Initializes a new instance of the <see cref="TwitchLiveChannelApi"/> class.
        /// </summary>
        /// <param name="helixService">Helix service that supplies channel snapshots and mining watchers.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="helixService"/> is null.</exception>
        public TwitchLiveChannelApi(ITwitchHelixService helixService)
        {
            _helixService = helixService ?? throw new ArgumentNullException(nameof(helixService));
        }

        /// <summary>
        /// Fetches normalized channel metadata for a login slug.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The channel snapshot, or <see langword="null"/> when the channel could not be resolved.</returns>
        public Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default) =>
            _helixService.GetChannelAsync(channelLogin, ct);

        /// <summary>
        /// Picks the best live channel login for a campaign, preferring <paramref name="preferredLogin"/> when eligible.
        /// </summary>
        /// <param name="campaign">Drop campaign whose eligible streamers and game id are evaluated.</param>
        /// <param name="preferredLogin">Optional login to try first (for example the last mined streamer).</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The selected login slug, or <see langword="null"/> when no eligible live streamer is found.
        /// </returns>
        public async Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default)
        {
            AppLogger.Info(
                "TwitchMining",
                $"SelectBestLiveLogin START campaign='{campaign.Name}' id={campaign.Id} slug='{campaign.Slug}' gameId={campaign.GameId ?? "(none)"} " +
                $"helixAuth={_helixService.IsAuthenticated} preferredLogin={preferredLogin ?? "(none)"}");

            IReadOnlyList<string> logins = EligibleStreamerParser.ParseChannelLogins(campaign);
            if (logins.Count == 0)
            {
                AppLogger.Warn(
                    "TwitchMining",
                    $"SelectBestLiveLogin ABORT campaign='{campaign.Name}' isGeneralDrop={campaign.IsGeneralDrop} " +
                    $"connectUrls={campaign.ConnectUrls.Count} - no channel logins in campaign URLs.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(campaign.GameId))
            {
                AppLogger.Warn("TwitchMining", $"SelectBestLiveLogin ABORT campaign='{campaign.Name}' - missing Twitch game id from drops inventory.");
                return null;
            }

            AppLogger.Info("TwitchMining", $"SelectBestLiveLogin candidates={logins.Count}: [{string.Join(", ", logins)}]");

            IEnumerable<string> ordered = string.IsNullOrWhiteSpace(preferredLogin)
                ? logins
                : new[] { preferredLogin }.Concat(logins.Where(l => !string.Equals(l, preferredLogin, StringComparison.OrdinalIgnoreCase)));

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
                bool gameOk = TwitchChannelEligibility.IsEligible(snapshot, campaign);
                if (!gameOk)
                {
                    AppLogger.Info(
                        "TwitchMining",
                        $"  login={login} display='{snapshot.DisplayName}' -> SKIP live but wrong game " +
                        $"campaignGameId={campaign.GameId} streamGameId={snapshot.GameId ?? "(none)"} " +
                        $"categories=[{string.Join(", ", snapshot.CategorySlugs)}]");
                    continue;
                }

                gameMatchCount++;
                AppLogger.Info(
                    "TwitchMining",
                    $"SelectBestLiveLogin PICKED login={login} display='{snapshot.DisplayName}' " +
                    $"gameId={snapshot.GameId} campaignGameId={campaign.GameId}");
                return login;
            }

            AppLogger.Warn(
                "TwitchMining",
                $"SelectBestLiveLogin FAILED campaign='{campaign.Name}' gameId={campaign.GameId} " +
                $"candidates={logins.Count} live={liveCount} gameMatch={gameMatchCount}");
            return null;
        }

        /// <summary>
        /// Returns whether <paramref name="channelLogin"/> is live and streaming the campaign's Twitch game.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="campaign">Drop campaign whose Twitch <see cref="DropsCampaign.GameId"/> is matched against Helix <c>game_id</c>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><see langword="true"/> when the channel is live and game-eligible; otherwise <see langword="false"/>.</returns>
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
    }
}