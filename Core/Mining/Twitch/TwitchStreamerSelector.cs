using Core.Enums;
using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using Core.Stores;

namespace Core.Mining.Twitch
{
    /// <summary>
    /// Selects a Twitch streamer URL for a campaign via <see cref="ITwitchLiveChannelApi"/>.
    /// </summary>
    public sealed class TwitchStreamerSelector
    {
        private readonly ITwitchLiveChannelApi _liveChannelApi;
        private readonly LastMinedStreamersStore _lastMinedStreamers;

        /// <summary>
        /// Initializes a new instance of the <see cref="TwitchStreamerSelector"/> class.
        /// </summary>
        /// <param name="liveChannelApi">Twitch live-channel API used to resolve eligible streamers.</param>
        /// <param name="lastMinedStreamers">Store of last-mined streamer URLs per campaign slug.</param>
        public TwitchStreamerSelector(ITwitchLiveChannelApi liveChannelApi, LastMinedStreamersStore lastMinedStreamers)
        {
            _liveChannelApi = liveChannelApi;
            _lastMinedStreamers = lastMinedStreamers;
        }

        /// <summary>
        /// Selects a Twitch stream URL for the campaign, preferring the last-mined streamer when still eligible.
        /// </summary>
        /// <param name="campaign">Twitch drop campaign to mine.</param>
        /// <returns>The stream URL, or an empty string when no eligible streamer is found.</returns>
        public async Task<string> SelectUrlAsync(DropsCampaign campaign)
        {
            _lastMinedStreamers.TryGet(Platform.Twitch, campaign.Slug, out string? rememberedUrl);
            string? preferredLogin = string.IsNullOrWhiteSpace(rememberedUrl)
                ? null
                : StreamerUrlParser.GetLoginFromUrl(rememberedUrl);

            AppLogger.Debug(
                "TwitchMining",
                $"SelectUrlAsync campaign='{campaign.Name}' slug='{campaign.Slug}' rememberedUrl={rememberedUrl ?? "(none)"} preferredLogin={preferredLogin ?? "(none)"}");

            string? login = await _liveChannelApi.SelectBestLiveLoginAsync(campaign, preferredLogin);
            if (string.IsNullOrWhiteSpace(login))
            {
                AppLogger.Warn("TwitchMining", $"SelectUrlAsync EMPTY - no streamer for campaign '{campaign.Name}' slug='{campaign.Slug}'.");
                return string.Empty;
            }

            string streamerUrl = $"https://www.twitch.tv/{login}";
            AppLogger.Debug("TwitchMining", $"SelectUrlAsync OK url={streamerUrl} campaign='{campaign.Name}'");
            return streamerUrl;
        }
    }
}