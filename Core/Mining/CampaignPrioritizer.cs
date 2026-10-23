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
        /// <param name="gamePriority">Game slugs in priority order; campaigns of earlier games win over later ones and unlisted games.</param>
        /// <returns>The selection result, including whether the pin was released because it is no longer pursuable.</returns>
        public static CampaignSelectionResult SelectBest(
            IReadOnlyList<DropsCampaign> campaigns,
            string? pinnedCampaignId,
            MiningPriorityMode mode,
            bool prioritizeBadges = false,
            IReadOnlyList<string>? gamePriority = null)
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
                return new CampaignSelectionResult(SelectByPriority(TopPriorityGame(campaigns, gamePriority), mode, prioritizeBadges), PinReleased: true);
            }

            return new CampaignSelectionResult(SelectByPriority(TopPriorityGame(campaigns, gamePriority), mode, prioritizeBadges), PinReleased: false);
        }

        /// <summary>
        /// Narrows the candidates to the campaigns of the highest-priority game present; the mining priority mode then
        /// decides among them. Games missing from the priority list rank last, and without a list nothing is narrowed.
        /// </summary>
        private static IReadOnlyList<DropsCampaign> TopPriorityGame(IReadOnlyList<DropsCampaign> campaigns, IReadOnlyList<string>? gamePriority)
        {
            if (gamePriority is not { Count: > 0 } || campaigns.Count == 0)
                return campaigns;

            int Rank(DropsCampaign c)
            {
                string slug = c.Slug?.Trim() ?? string.Empty;
                for (int i = 0; i < gamePriority.Count; i++)
                {
                    if (string.Equals(gamePriority[i], slug, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return int.MaxValue;
            }

            int best = campaigns.Min(Rank);
            AppLogger.Debug("Selection", $"Game priority narrowed candidates to rank {best}.");
            return [.. campaigns.Where(c => Rank(c) == best)];
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