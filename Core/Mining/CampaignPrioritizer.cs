using Core.Logging;
using Core.Models;
using Core.Enums;

namespace Core.Mining
{
    /// <summary>
    /// Result of selecting the best campaign from a candidate list.
    /// </summary>
    /// <param name="Campaign">The selected campaign, or <see langword="null"/> when none qualify.</param>
    /// <param name="PinReleased">Whether a stale pinned campaign override was cleared.</param>
    public readonly record struct CampaignSelectionResult(DropsCampaign? Campaign, bool PinReleased);

    /// <summary>
    /// Selects which drop campaign to mine based on user pin override and mining priority settings.
    /// </summary>
    public static class CampaignPrioritizer
    {
        /// <summary>
        /// Picks the best campaign from the candidate list, honoring a pinned campaign when still valid.
        /// </summary>
        /// <param name="campaigns">Non-empty candidate campaigns for one platform.</param>
        /// <param name="pinnedCampaignId">Optional user-pinned campaign id from <see cref="Persistence.PinnedCampaignStore"/>.</param>
        /// <param name="mode">Mining priority mode from settings.</param>
        /// <param name="prioritizeBadges">When true, campaigns with an unclaimed badge reward are picked ahead of the rest.</param>
        /// <returns>The selection result, including whether the pin was released because it is no longer pursuable.</returns>
        public static CampaignSelectionResult SelectBest(
            IReadOnlyList<DropsCampaign> campaigns,
            string? pinnedCampaignId,
            MiningPriorityMode mode,
            bool prioritizeBadges = false)
        {
            if (pinnedCampaignId != null)
            {
                DropsCampaign? pinned = campaigns.FirstOrDefault(c => c.Id == pinnedCampaignId);

                if (pinned != null)
                {
                    AppLogger.Debug("Selection", $"Pinned campaign '{pinned.Name}' selected via manual override.");
                    return new CampaignSelectionResult(pinned, PinReleased: false);
                }

                AppLogger.Debug("Selection", $"Pinned campaign '{pinnedCampaignId}' is no longer pursuable, releasing pin.");
                return new CampaignSelectionResult(SelectByPriority(campaigns, mode, prioritizeBadges), PinReleased: true);
            }

            return new CampaignSelectionResult(SelectByPriority(campaigns, mode, prioritizeBadges), PinReleased: false);
        }

        private static DropsCampaign? SelectByPriority(IReadOnlyList<DropsCampaign> campaigns, MiningPriorityMode mode, bool prioritizeBadges)
        {
            AppLogger.Debug("Selection", $"Selecting best campaign with mode={mode}, candidates={campaigns.Count}");

            List<DropsCampaign> prioritizedCampaigns = mode switch
            {
                MiningPriorityMode.EndingSoonest => [.. campaigns
                    .OrderBy(c => c.IsGeneralDrop)
                    .ThenBy(c => c.EndsAt)
                    .ThenBy(c => c.Rewards
                        .Where(r => !r.IsClaimed)
                        .Min(r => r.RequiredMinutes - r.ProgressMinutes))],
                MiningPriorityMode.LeastTimeToNextReward => [.. campaigns
                    .OrderBy(c => c.IsGeneralDrop)
                    .ThenBy(c => c.Rewards
                        .Where(r => !r.IsClaimed)
                        .Min(r => r.RequiredMinutes - r.ProgressMinutes))
                    .ThenByDescending(c => c.CompletionPercentage())],
                MiningPriorityMode.HighestCompletion => [.. campaigns
                    .OrderBy(c => c.IsGeneralDrop)
                    .ThenByDescending(c => c.CompletionPercentage())
                    .ThenBy(c => c.EndsAt)
                    .ThenBy(c => c.Rewards
                        .Where(r => !r.IsClaimed)
                        .Min(r => r.RequiredMinutes - r.ProgressMinutes))],
                _ => [.. campaigns
                    .OrderBy(c => c.IsGeneralDrop)
                    .ThenByDescending(c => c.CompletionPercentage())
                    .ThenBy(c => c.Rewards
                        .Where(r => !r.IsClaimed)
                        .Min(r => r.RequiredMinutes - r.ProgressMinutes))],
            };

            // The mode order still decides among badge campaigns (and among the rest when none has a badge).
            DropsCampaign? selected = (prioritizeBadges ? prioritizedCampaigns.FirstOrDefault(c => c.HasUnclaimedBadge) : null)
                                      ?? prioritizedCampaigns.FirstOrDefault();
            if (selected == null)
                AppLogger.Warn("Selection", "No campaign selected after priority sort.");
            else
                AppLogger.Debug("Selection", $"Selected campaign '{selected.Name}' ({selected.Id}) with mode={mode}.");

            return selected;
        }
    }
}