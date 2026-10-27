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
        /// <param name="autoClaimRewards">Whether ready rewards are claimed automatically for this account.</param>
        /// <param name="account">The account being processed, named in webhook notifications.</param>
        /// <param name="cancellationToken">Cancellation token for the mining cycle.</param>
        /// <returns>Updated <paramref name="nextCheckAt"/> after any failed claims.</returns>
        public async Task<DateTime> ProcessAutoClaimsAsync(
            IReadOnlyList<DropsCampaign> campaigns,
            DateTime nextCheckAt,
            IGqlService? twitchGqlService,
            IWebViewHost? kickWebView,
            Func<string, string, bool> markRewardClaimed,
            bool autoClaimRewards,
            AccountModel? account = null,
            CancellationToken cancellationToken = default)
        {
            List<DropsReward> readyToClaimRewards = [.. campaigns.SelectMany(c => c.Rewards.Where(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes))];

            AppLogger.Debug(
                "DropClaim",
                $"ProcessAutoClaimsAsync START autoClaimEnabled={autoClaimRewards} " +
                $"readyToClaimCount={readyToClaimRewards.Count} " +
                $"rewardIds=[{string.Join(", ", readyToClaimRewards.Select(r => r.Id))}]");

            if (autoClaimRewards)
            {
                foreach (DropsReward item in readyToClaimRewards)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    DropsCampaign? parentCampaign = campaigns.FirstOrDefault(c => c.Rewards.Contains(item));
                    if (parentCampaign == null)
                    {
                        AppLogger.Warn("DropClaim", $"No parent campaign found for ready reward id={item.Id} name='{item.Name}' - skipped.");
                        continue;
                    }

                    if (parentCampaign.Platform == Platform.Kick && KickUnlinkedClaims.IsBlocked(kickWebView?.AccountId ?? string.Empty, parentCampaign.Id, item.Id))
                    {
                        AppLogger.Debug("DropClaim", $"Skipping Kick claim - account not linked. campaignId={parentCampaign.Id} rewardId={item.Id}");
                        continue;
                    }

                    AppLogger.Debug(
                        "DropClaim",
                        $"Claim attempt START platform={parentCampaign.Platform} campaignId={parentCampaign.Id} rewardId={item.Id} " +
                        $"rewardName='{item.Name}' progressMinutes={item.ProgressMinutes}/{item.RequiredMinutes} " +
                        $"twitchGqlAvailable={twitchGqlService != null} kickWebViewAvailable={kickWebView != null}");

                    bool claimResult = false;
                    if (parentCampaign.Platform == Platform.Twitch && twitchGqlService != null)
                    {
                        claimResult = await twitchGqlService.ClaimDropAsync(parentCampaign.Id, item.Id);
                        AppLogger.Debug("DropClaim", $"Twitch ClaimDropAsync result={claimResult} campaignId={parentCampaign.Id} rewardId={item.Id}");
                    }
                    else if (parentCampaign.Platform == Platform.Kick && kickWebView != null)
                    {
                        claimResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await kickWebView.ClaimKickDropAsync(parentCampaign.Id, item.Id));
                        AppLogger.Debug("DropClaim", $"Kick ClaimKickDropAsync result={claimResult} campaignId={parentCampaign.Id} rewardId={item.Id}");
                    }
                    else
                    {
                        AppLogger.Warn(
                            "DropClaim",
                            $"Claim SKIPPED - no claim path available for platform={parentCampaign.Platform} " +
                            $"(twitchGqlAvailable={twitchGqlService != null}, kickWebViewAvailable={kickWebView != null}). campaignId={parentCampaign.Id} rewardId={item.Id}");
                    }

                    if (claimResult)
                    {
                        bool inventoryUpdated = markRewardClaimed(parentCampaign.Id, item.Id);
                        if (!inventoryUpdated)
                            AppLogger.Warn("Miner", $"Failed to apply immediate claimed-state update. campaignId={parentCampaign.Id}, rewardId={item.Id}");
                        else
                            AppLogger.Debug("Miner", $"Applied immediate claimed-state update. campaignId={parentCampaign.Id}, rewardId={item.Id}");

                        if (UISettingsManager.Instance.NotifyOnAutoClaimed)
                            NotificationManager.ShowNotification("Drop Claimed", $"Successfully claimed drop reward: {item.Name}", NotificationType.DropClaimed, details: BuildDetails(account, parentCampaign, item, "Claimed automatically"));
                    }
                    else if (parentCampaign.Platform == Platform.Kick && KickUnlinkedClaims.IsBlocked(kickWebView?.AccountId ?? string.Empty, parentCampaign.Id, item.Id))
                    {
                        NotificationManager.ShowNotification("Account Not Linked", $"Link your game account on Kick, then claim manually: {item.Name}", NotificationType.AccountNotLinked, details: BuildDetails(account, parentCampaign, item, "Cannot claim - game account not linked"));
                    }
                    else
                    {
                        nextCheckAt = DateTime.Now.AddMinutes(1);
                        NotificationManager.ShowNotification("Drop Claim Failed", $"Failed to claim drop reward, re-trying in a minute: {item.Name}", NotificationType.DropClaimFailed, details: BuildDetails(account, parentCampaign, item, "Claim failed - retrying in 1 minute"));
                    }
                }
            }
            else if (UISettingsManager.Instance.NotifyOnReadyToClaim && readyToClaimRewards.Count > 0)
            {
                List<(string Name, string Value)> details = AccountDetails(account);
                details.Add(("Auto-claim", "Off - claim manually"));
                details.Add(("Ready to claim", string.Join("\n", campaigns.SelectMany(c => c.Rewards
                    .Where(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes)
                    .Select(r => $"{r.Name} ({c.GameName}, {c.Platform})")))));

                NotificationManager.ShowNotification("Drop Ready to Claim", $"You have {readyToClaimRewards.Count} drops rewards ready to claim. Please claim them manually.", NotificationType.DropReadyToClaim, details: details);
            }

            return nextCheckAt;
        }

        private static List<(string Name, string Value)> AccountDetails(AccountModel? account)
        {
            List<(string Name, string Value)> details = [];
            if (account != null)
            {
                details.Add(("Account", string.IsNullOrWhiteSpace(account.DisplayName) ? account.Id : account.DisplayName));
                details.Add(("Platform", account.Platform.ToString()));
            }

            return details;
        }

        private static List<(string Name, string Value)> BuildDetails(AccountModel? account, DropsCampaign campaign, DropsReward reward, string status)
        {
            List<(string Name, string Value)> details = AccountDetails(account);
            if (account == null)
                details.Add(("Platform", campaign.Platform.ToString()));

            details.Add(("Status", status));
            details.Add(("Game", campaign.GameName));
            details.Add(("Campaign", campaign.Name));
            details.Add(("Reward", reward.Name));
            details.Add(("Progress", $"{DurationFormatter.Format(reward.ProgressMinutes)} / {DurationFormatter.Format(reward.RequiredMinutes)}"));
            return details;
        }
    }
}