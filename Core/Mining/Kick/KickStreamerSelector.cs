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

        public KickStreamerSelector(IKickLiveChannelApi liveChannelApi, LastMinedStreamersStore lastMinedStreamers)
        {
            _liveChannelApi = liveChannelApi;
            _lastMinedStreamers = lastMinedStreamers;
        }

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