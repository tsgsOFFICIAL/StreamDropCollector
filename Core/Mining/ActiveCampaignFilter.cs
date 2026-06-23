using Core.Managers;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Shared whitelist and date-window filtering for active campaign lists.
    /// </summary>
    public static class ActiveCampaignFilter
    {
        /// <summary>
        /// Filters campaigns allowed by the current game whitelist.
        /// </summary>
        public static List<DropsCampaign> ApplyWhitelist(IEnumerable<DropsCampaign> source) =>
            source.Where(c => UISettingsManager.Instance.IsCampaignAllowedByWhitelist(c)).ToList();

        /// <summary>
        /// Filters campaigns that have started and not yet ended.
        /// </summary>
        public static List<DropsCampaign> ApplyActiveWindow(IEnumerable<DropsCampaign> source) =>
            source
                .Where(c => c.StartsAt <= DateTimeOffset.Now && c.EndsAt > DateTimeOffset.Now)
                .ToList();

        /// <summary>
        /// Applies whitelist + active window filters and orders for dashboard display.
        /// </summary>
        public static List<DropsCampaign> FilterForDisplay(IEnumerable<DropsCampaign> source) =>
            ApplyActiveWindow(ApplyWhitelist(source))
                .OrderBy(x => x.Platform)
                .ThenBy(x => x.GameName)
                .ToList();
    }
}