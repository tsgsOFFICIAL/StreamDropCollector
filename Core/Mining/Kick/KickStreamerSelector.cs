using Core.Enums;
using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using Core.Stores;

namespace Core.Mining.Kick
{
    /// <summary>
    /// Selects a Kick streamer URL for a campaign via <see cref="IKickLiveChannelApi"/>.
    /// </summary>
    public sealed class KickStreamerSelector
    {
        private readonly IKickLiveChannelApi _liveChannelApi;
        private readonly LastMinedStreamersStore _lastMinedStreamers;

        /// <summary>
        /// Initializes a new instance of the <see cref="KickStreamerSelector"/> class.
        /// </summary>
        /// <param name="liveChannelApi">Kick live-channel API used to resolve eligible streamers.</param>
        /// <param name="lastMinedStreamers">Store of last-mined streamer URLs per campaign slug.</param>
        public KickStreamerSelector(IKickLiveChannelApi liveChannelApi, LastMinedStreamersStore lastMinedStreamers)
        {
            _liveChannelApi = liveChannelApi;
            _lastMinedStreamers = lastMinedStreamers;
        }

        /// <summary>
        /// Selects a Kick stream URL for the campaign, preferring the last-mined streamer when still eligible.
        /// </summary>
        /// <param name="campaign">Kick drop campaign to mine.</param>
        /// <returns>The stream URL, or an empty string when no eligible streamer is found.</returns>
        public async Task<string> SelectUrlAsync(DropsCampaign campaign)
        {
            _lastMinedStreamers.TryGet(Platform.Kick, campaign.Slug, out string? rememberedUrl);
            string? preferredLogin = string.IsNullOrWhiteSpace(rememberedUrl)
                ? null
                : StreamerUrlParser.GetLoginFromUrl(rememberedUrl);

            string? login = await _liveChannelApi.SelectBestLiveLoginAsync(campaign, preferredLogin);
            if (string.IsNullOrWhiteSpace(login))
            {
                AppLogger.Warn("KickSelection", $"No Kick streamer resolved for campaign '{campaign.Name}'.");
                return string.Empty;
            }

            string streamerUrl = $"https://kick.com/{login}";
            AppLogger.Debug("KickSelection", $"[KickStreamerSelector] Selected '{streamerUrl}' for '{campaign.Name}'.");
            return streamerUrl;
        }
    }
}