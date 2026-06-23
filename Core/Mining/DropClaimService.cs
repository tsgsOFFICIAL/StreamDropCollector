using Core.Enums;
using Core.Interfaces;
using Core.Logging;
using Core.Managers;
using Core.Models;
using System.Windows;

namespace Core.Mining
{
    /// <summary>
    /// Processes ready-to-claim rewards and updates the next scheduled re-evaluation time.
    /// </summary>
    public sealed class DropClaimService
    {
        /// <summary>
        /// Attempts auto-claims when enabled, or notifies when manual claim is needed.
        /// </summary>
        /// <param name="campaigns">Active campaign snapshot.</param>
        /// <param name="nextCheckAt">Current scheduled re-evaluation time.</param>
        /// <param name="twitchGqlService">Twitch claim service, if available.</param>
        /// <param name="kickWebView">Kick WebView host for in-page claims.</param>
        /// <param name="markRewardClaimed">Callback that marks a reward claimed in active inventory.</param>
        /// <param name="cancellationToken">Cancellation token for the mining cycle.</param>
        /// <returns>Updated <paramref name="nextCheckAt"/> after any failed claims.</returns>
        public async Task<DateTime> ProcessAutoClaimsAsync(
            IReadOnlyList<DropsCampaign> campaigns,
            DateTime nextCheckAt,
            IGqlService? twitchGqlService,
            IWebViewHost? kickWebView,
            Func<string, string, bool> markRewardClaimed,
            CancellationToken cancellationToken = default)
        {
            List<DropsReward> readyToClaimRewards = [.. campaigns.SelectMany(c => c.Rewards.Where(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes))];

            if (UISettingsManager.Instance.AutoClaimRewards)
            {
                foreach (DropsReward item in readyToClaimRewards)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    DropsCampaign? parentCampaign = campaigns.FirstOrDefault(c => c.Rewards.Contains(item));
                    if (parentCampaign == null)
                        continue;

                    bool claimResult = false;
                    if (parentCampaign.Platform == Platform.Twitch && twitchGqlService != null)
                        claimResult = await twitchGqlService.ClaimDropAsync(parentCampaign.Id, item.Id);
                    else if (parentCampaign.Platform == Platform.Kick && kickWebView != null)
                        claimResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await kickWebView.ClaimKickDropAsync(parentCampaign.Id, item.Id));

                    if (claimResult)
                    {
                        bool inventoryUpdated = markRewardClaimed(parentCampaign.Id, item.Id);
                        if (!inventoryUpdated)
                            AppLogger.Warn("Miner", $"Failed to apply immediate claimed-state update. campaignId={parentCampaign.Id}, rewardId={item.Id}");
                        else
                            AppLogger.Debug("Miner", $"Applied immediate claimed-state update. campaignId={parentCampaign.Id}, rewardId={item.Id}");

                        if (UISettingsManager.Instance.NotifyOnAutoClaimed)
                            NotificationManager.ShowNotification("Drop Claimed", $"Successfully claimed drop reward: {item.Name}");
                    }
                    else
                    {
                        nextCheckAt = DateTime.Now.AddMinutes(1);
                        NotificationManager.ShowNotification("Drop Claim Failed", $"Failed to claim drop reward, re-trying in a minute: {item.Name}");
                    }
                }
            }
            else if (UISettingsManager.Instance.NotifyOnReadyToClaim && readyToClaimRewards.Count > 0)
            {
                NotificationManager.ShowNotification("Drop Ready to Claim", $"You have {readyToClaimRewards.Count} drops rewards ready to claim. Please claim them manually.");
            }

            return nextCheckAt;
        }
    }
}