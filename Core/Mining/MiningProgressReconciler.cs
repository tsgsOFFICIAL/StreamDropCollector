using Core.Logging;
using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// Aligns local mining counters with authoritative server-reported reward progress.
    /// </summary>
    /// <remarks>
    /// Server values win when they differ from local estimates. After a merge, live second counters are
    /// re-baselined so the 1-second ticker continues from the corrected minute totals.
    /// </remarks>
    public static class MiningProgressReconciler
    {
        /// <summary>
        /// Merges server reward progress into a campaign and re-baselines live second counters when values differ.
        /// </summary>
        /// <param name="campaign">Locally tracked campaign currently being mined.</param>
        /// <param name="serverRewards">Authoritative reward progress returned by the platform API.</param>
        /// <param name="state">Per-platform second counters to realign when server data changes.</param>
        /// <param name="updatedCampaign">The campaign with merged reward progress when <see langword="true"/> is returned.</param>
        /// <returns><see langword="true"/> when any reward progress or claim state changed; otherwise <see langword="false"/>.</returns>
        public static bool TryReconcile(
            DropsCampaign campaign,
            IReadOnlyList<DropsReward> serverRewards,
            PlatformProgressState state,
            out DropsCampaign updatedCampaign)
        {
            updatedCampaign = campaign;

            AppLogger.Debug(
                "Reconcile",
                $"TryReconcile START campaignId={campaign.Id} localRewardIds=[{string.Join(", ", campaign.Rewards.Select(r => r.Id))}] " +
                $"serverRewardCount={serverRewards.Count} serverRewardIds=[{string.Join(", ", serverRewards.Select(r => r.Id))}]");

            if (serverRewards.Count == 0)
            {
                AppLogger.Debug("Reconcile", $"TryReconcile ABORT campaignId={campaign.Id} - server returned zero rewards.");
                return false;
            }

            Dictionary<string, DropsReward> serverById = serverRewards
                .Where(r => !string.IsNullOrEmpty(r.Id))
                .ToDictionary(r => r.Id, StringComparer.Ordinal);
            bool changed = false;
            List<DropsReward> mergedRewards = new(campaign.Rewards.Count);

            foreach (DropsReward localReward in campaign.Rewards)
            {
                if (!serverById.TryGetValue(localReward.Id, out DropsReward serverReward))
                {
                    AppLogger.Debug("Reconcile", $"TryReconcile rewardId={localReward.Id} has no server match - keeping local value unchanged.");
                    mergedRewards.Add(localReward);
                    continue;
                }

                int serverMinutes = Math.Min(serverReward.ProgressMinutes, localReward.RequiredMinutes);
                bool serverClaimed = serverReward.IsClaimed;
                bool rewardChanged = localReward.ProgressMinutes != serverMinutes || localReward.IsClaimed != serverClaimed;

                if (rewardChanged)
                {
                    changed = true;
                    AppLogger.Debug(
                        "Reconcile",
                        $"TryReconcile DRIFT rewardId={localReward.Id} localMinutes={localReward.ProgressMinutes} serverMinutes={serverMinutes} " +
                        $"localClaimed={localReward.IsClaimed} serverClaimed={serverClaimed}");
                }
                else
                {
                    AppLogger.Debug("Reconcile", $"TryReconcile rewardId={localReward.Id} no drift (localMinutes==serverMinutes=={serverMinutes}, claimed=={serverClaimed}).");
                }

                mergedRewards.Add(localReward with
                {
                    ProgressMinutes = serverMinutes,
                    IsClaimed = serverClaimed
                });
            }

            if (!changed)
            {
                AppLogger.Debug("Reconcile", $"TryReconcile DONE campaignId={campaign.Id} - no drift detected, nothing applied.");
                return false;
            }

            updatedCampaign = campaign with { Rewards = mergedRewards.AsReadOnly() };
            MiningBaseline baseline = MiningBaselineInitializer.Create(updatedCampaign);
            state.ApplyBaseline(baseline);

            AppLogger.Debug(
                "Reconcile",
                $"TryReconcile DONE campaignId={campaign.Id} - drift applied, baseline re-created and state re-applied.");

            return true;
        }
    }
}