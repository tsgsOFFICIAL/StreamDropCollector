using Core.Enums;
using Core.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace Core.Mining
{
    /// <summary>
    /// Mutates the active campaign inventory and synchronizes current selection flags.
    /// </summary>
    public sealed class ActiveCampaignUpdater
    {
        /// <summary>
        /// Applies minute progress to a campaign in the active inventory.
        /// </summary>
        public void ApplyMinuteProgress(
            ObservableCollection<DropsCampaign> activeCampaigns,
            ActiveCampaignSelectionContext selection,
            Platform platform,
            string campaignId,
            int minutesToAdd,
            Action<string, string>? verboseLog = null)
        {
            if (minutesToAdd <= 0)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                DropsCampaign? campaign = activeCampaigns.FirstOrDefault(c => c.Platform == platform && c.Id == campaignId);
                if (campaign == null || !campaign.HasProgressToMake())
                {
                    ClearCurrentCampaign(selection, platform);
                    UpdateSelectionFlags(activeCampaigns, selection);
                    return;
                }

                int campaignIndex = activeCampaigns.IndexOf(campaign);
                if (campaignIndex < 0)
                    return;

                List<DropsReward> updatedRewards = new List<DropsReward>(campaign.Rewards.Count);
                foreach (DropsReward reward in campaign.Rewards)
                {
                    int newProgress = reward.IsClaimed || reward.ProgressMinutes >= reward.RequiredMinutes
                        ? reward.ProgressMinutes
                        : Math.Min(reward.ProgressMinutes + minutesToAdd, reward.RequiredMinutes);

                    updatedRewards.Add(reward with { ProgressMinutes = newProgress });
                }

                verboseLog?.Invoke(
                    "MinuteTick",
                    $"campaignId={campaign.Id}, platform={campaign.Platform}, minutesAdded={minutesToAdd}, rewardsUpdated={campaign.Rewards.Count}, unclaimedRewards={campaign.Rewards.Count(r => !r.IsClaimed)}");
                verboseLog?.Invoke(
                    "RewardTransition",
                    $"platform={platform}, campaignId={campaignId}, minutesAdded={minutesToAdd}, rewards={string.Join(", ", updatedRewards.Select(r => $"{r.Name}:{r.ProgressMinutes}/{r.RequiredMinutes}(claimed={r.IsClaimed})"))}");

                DropsCampaign updatedCampaign = campaign with { Rewards = updatedRewards };
                activeCampaigns[campaignIndex] = updatedCampaign;
                SyncCurrentCampaign(selection, platform, campaignId, updatedCampaign);
                UpdateSelectionFlags(activeCampaigns, selection);
            });
        }

        /// <summary>
        /// Marks a reward as claimed in the active inventory.
        /// </summary>
        /// <returns><see langword="true"/> when the reward was found and updated.</returns>
        public bool MarkRewardClaimed(
            ObservableCollection<DropsCampaign> activeCampaigns,
            ActiveCampaignSelectionContext selection,
            string campaignId,
            string rewardId)
        {
            bool updated = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                DropsCampaign? existingCampaign = activeCampaigns.FirstOrDefault(c => c.Id == campaignId);
                if (existingCampaign == null)
                    return;

                int campaignIndex = activeCampaigns.IndexOf(existingCampaign);
                if (campaignIndex < 0)
                    return;

                bool rewardFound = false;
                List<DropsReward> updatedRewards = new List<DropsReward>(existingCampaign.Rewards.Count);
                foreach (DropsReward reward in existingCampaign.Rewards)
                {
                    if (reward.Id == rewardId)
                    {
                        rewardFound = true;
                        updatedRewards.Add(reward with
                        {
                            IsClaimed = true,
                            ProgressMinutes = Math.Max(reward.ProgressMinutes, reward.RequiredMinutes)
                        });
                    }
                    else
                    {
                        updatedRewards.Add(reward);
                    }
                }

                if (!rewardFound)
                    return;

                DropsCampaign updatedCampaign = existingCampaign with { Rewards = updatedRewards };
                activeCampaigns[campaignIndex] = updatedCampaign;

                if (selection.CurrentTwitchCampaign?.Id == campaignId)
                    selection.CurrentTwitchCampaign = updatedCampaign;

                if (selection.CurrentKickCampaign?.Id == campaignId)
                    selection.CurrentKickCampaign = updatedCampaign;

                UpdateSelectionFlags(activeCampaigns, selection);
                updated = true;
            });

            return updated;
        }

        /// <summary>
        /// Updates <see cref="DropsCampaign.IsCurrentCampaign"/> and reward highlight flags.
        /// </summary>
        public void UpdateSelectionFlags(
            ObservableCollection<DropsCampaign> activeCampaigns,
            ActiveCampaignSelectionContext selection)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (activeCampaigns.Count == 0)
                    return;

                List<DropsCampaign> updatedCampaigns = new List<DropsCampaign>(activeCampaigns.Count);

                foreach (DropsCampaign campaign in activeCampaigns.ToList())
                {
                    bool isCurrentCampaign = (campaign.Platform == Platform.Twitch && campaign.Id == selection.CurrentTwitchCampaign?.Id) ||
                                             (campaign.Platform == Platform.Kick && campaign.Id == selection.CurrentKickCampaign?.Id);

                    DropsReward? currentReward = null;
                    if (isCurrentCampaign)
                    {
                        currentReward = campaign.Rewards
                            .Where(r => !r.IsClaimed)
                            .OrderBy(r => Math.Max(0, r.RequiredMinutes - r.ProgressMinutes))
                            .FirstOrDefault();
                    }

                    List<DropsReward> updatedRewards = new List<DropsReward>(campaign.Rewards.Count);
                    foreach (DropsReward reward in campaign.Rewards)
                    {
                        bool isCurrentReward = isCurrentCampaign && currentReward != null && reward.Id == currentReward.Id;
                        updatedRewards.Add(reward with { IsCurrentReward = isCurrentReward });
                    }

                    updatedCampaigns.Add(campaign with
                    {
                        IsCurrentCampaign = isCurrentCampaign,
                        Rewards = updatedRewards
                    });
                }

                activeCampaigns.Clear();
                foreach (DropsCampaign? campaign in updatedCampaigns.OrderBy(x => x.Platform).ThenBy(x => x.GameName))
                    activeCampaigns.Add(campaign);
            });
        }

        private static void ClearCurrentCampaign(ActiveCampaignSelectionContext selection, Platform platform)
        {
            switch (platform)
            {
                case Platform.Twitch:
                    selection.CurrentTwitchCampaign = null;
                    break;
                case Platform.Kick:
                    selection.CurrentKickCampaign = null;
                    break;
            }
        }

        private static void SyncCurrentCampaign(
            ActiveCampaignSelectionContext selection,
            Platform platform,
            string campaignId,
            DropsCampaign updatedCampaign)
        {
            if (platform == Platform.Twitch && selection.CurrentTwitchCampaign?.Id == campaignId)
                selection.CurrentTwitchCampaign = updatedCampaign;

            if (platform == Platform.Kick && selection.CurrentKickCampaign?.Id == campaignId)
                selection.CurrentKickCampaign = updatedCampaign;
        }
    }
}