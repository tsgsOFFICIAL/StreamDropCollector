using Core.Enums;
using Core.Helpers;
using Core.Logging;
using Core.Managers;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Picks the user's highest-ranked priority streamer that can currently be mined for a campaign.
    /// </summary>
    public static class PriorityStreamerPicker
    {
        /// <summary>
        /// Returns the first priority streamer that is live and eligible for the campaign, or <see langword="null"/>
        /// so the caller falls back to its normal selection.
        /// </summary>
        /// <param name="platform">Platform whose priority list is used.</param>
        /// <param name="campaign">Campaign about to be mined.</param>
        /// <param name="isEligibleAsync">Checks that a login is live and streaming the campaign's category.</param>
        public static async Task<string?> PickAsync(
            Platform platform,
            DropsCampaign campaign,
            Func<string, Task<bool>> isEligibleAsync)
        {
            IReadOnlyList<string> priority = UISettingsManager.Instance.GetPriorityStreamers(platform);
            if (priority.Count == 0)
                return null;

            // A campaign with its own channel list only credits watch time on those channels, so a priority
            // streamer outside that list must be skipped; general drops accept any eligible live channel.
            IReadOnlyList<string> campaignLogins = campaign.IsGeneralDrop ? [] : EligibleStreamerParser.ParseChannelLogins(campaign);

            foreach (string login in priority)
            {
                if (!campaign.IsGeneralDrop && !campaignLogins.Contains(login, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (await isEligibleAsync(login))
                {
                    AppLogger.Debug("StreamSelection", $"Picked priority streamer '{login}' for '{campaign.Name}'.");
                    return login;
                }
            }

            AppLogger.Debug("StreamSelection", $"No priority streamer is live and eligible for '{campaign.Name}'; using normal selection.");
            return null;
        }
    }
}
