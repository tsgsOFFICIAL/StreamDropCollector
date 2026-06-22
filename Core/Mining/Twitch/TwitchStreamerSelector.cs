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

        public TwitchStreamerSelector(ITwitchLiveChannelApi liveChannelApi, LastMinedStreamersStore lastMinedStreamers)
        {
            _liveChannelApi = liveChannelApi;
            _lastMinedStreamers = lastMinedStreamers;
        }

        public async Task<string> SelectUrlAsync(DropsCampaign campaign)
        {
            _lastMinedStreamers.TryGet(Platform.Twitch, campaign.Slug, out string? rememberedUrl);
            string? preferredLogin = string.IsNullOrWhiteSpace(rememberedUrl)
                ? null
                : StreamerUrlParser.GetLoginFromUrl(rememberedUrl);

            string? login = await _liveChannelApi.SelectBestLiveLoginAsync(campaign, preferredLogin);
            if (string.IsNullOrWhiteSpace(login))
            {
                AppLogger.Warn("TwitchSelection", $"No Twitch streamer resolved for campaign '{campaign.Name}'.");
                return string.Empty;
            }

            string streamerUrl = $"https://www.twitch.tv/{login}";
            AppLogger.Debug("TwitchSelection", $"[TwitchStreamerSelector] Selected '{streamerUrl}' for '{campaign.Name}'.");
            return streamerUrl;
        }
    }
}