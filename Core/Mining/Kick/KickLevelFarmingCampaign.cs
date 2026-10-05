using Core.Enums;
using Core.Models;

namespace Core.Mining.Kick
{
    /// <summary>
    /// Synthetic Kick campaign used to watch any top-viewed live channel when no drops are available (level farming).
    /// </summary>
    public static class KickLevelFarmingCampaign
    {
        /// <summary>
        /// Stable identifier of the synthetic campaign.
        /// </summary>
        public const string CampaignId = "kick-level-farming";

        /// <summary>
        /// Display name of the synthetic campaign.
        /// </summary>
        public const string DisplayName = "Level farming";

        /// <summary>
        /// Creates the synthetic general-drop style campaign; an empty slug makes every live channel eligible.
        /// </summary>
        public static DropsCampaign Create() => new(
            Id: CampaignId,
            Name: DisplayName,
            Slug: string.Empty,
            GameId: null,
            GameName: "Any category",
            GameImageUrl: null,
            StartsAt: DateTimeOffset.UtcNow,
            EndsAt: DateTimeOffset.UtcNow.AddYears(1),
            Rewards: [],
            Platform: Platform.Kick,
            ConnectUrls: ["https://kick.com/browse?sort=viewers_high_to_low"],
            IsGeneralDrop: true);

        /// <summary>
        /// Returns whether the campaign is the synthetic level-farming campaign.
        /// </summary>
        public static bool IsLevelFarming(this DropsCampaign? campaign) =>
            campaign is { Platform: Platform.Kick, Id: CampaignId };
    }
}