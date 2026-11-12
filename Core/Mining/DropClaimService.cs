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
        /// <param name="onTwitchCampaignUntracked">Called with the id of a Twitch campaign whose locally ready drop Twitch
        /// has no progress record for at all.</param>
        /// <param name="applyTwitchServerProgress">Called with Twitch's own reward progress for a campaign whose local
        /// counter ran ahead of it.</param>
        /// <returns>Updated <paramref name="nextCheckAt"/> after any failed claims.</returns>
        public async Task<DateTime> ProcessAutoClaimsAsync(
            IReadOnlyList<DropsCampaign> campaigns,
            DateTime nextCheckAt,
            IGqlService? twitchGqlService,
            IWebViewHost? kickWebView,
            Func<string, string, bool> markRewardClaimed,
            bool autoClaimRewards,
            AccountModel? account = null,
            CancellationToken cancellationToken = default,
            Action<string>? onTwitchCampaignUntracked = null,
            Action<string, IReadOnlyList<DropsReward>>? applyTwitchServerProgress = null)
        {
            List<DropsReward> readyToClaimRewards = [.. campaigns.SelectMany(c => c.Rewards.Where(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes))];

            // Local minute counters run ahead of (or entirely without) Twitch's own tracking, so every Twitch drop
            // that looks ready is checked against the inventory before a claim is sent.
            // This runs with auto-claim off too, so a "ready to claim" notice is never sent for a drop that was not earned.
            TwitchServerProgress? twitchServer = null;
            List<DropsCampaign> twitchReady = [.. campaigns.Where(c => c.Platform == Platform.Twitch && c.Rewards.Any(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes))];

            if (twitchGqlService != null && twitchReady.Count > 0)
            {
                twitchServer = await ServerProgressFetcher.FetchTwitchRewardProgressAsync(twitchGqlService, twitchReady, cancellationToken);
                AppLogger.Debug("DropClaim", $"Twitch claim verification fetched. succeeded={twitchServer.Succeeded} readyCampaigns={twitchReady.Count} trackedByServer={twitchServer.Campaigns.Count}");

                // The local counter usually finishes a little before Twitch credits the last minute. The stream is still
                // playing at this point, so give Twitch a moment to catch up instead of restarting the stream for it.
                for (int wait = 1; wait <= NearlyCreditedMaxWaits && HasNearlyCreditedDrop(twitchServer, twitchReady); wait++)
                {
                    AppLogger.Debug("DropClaim", $"Twitch is within {NearlyCreditedGapMinutes} minute(s) of crediting a ready drop; waiting ({wait}/{NearlyCreditedMaxWaits}).");
                    await Task.Delay(NearlyCreditedWait, cancellationToken);

                    TwitchServerProgress refreshed = await ServerProgressFetcher.FetchTwitchRewardProgressAsync(twitchGqlService, twitchReady, cancellationToken);
                    if (refreshed.Succeeded)
                        twitchServer = refreshed;
                }

                foreach (DropsCampaign campaign in twitchReady)
                {
                    bool untrackedReported = false;
                    bool serverProgressApplied = false;

                    foreach (DropsReward item in campaign.Rewards.Where(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes).ToList())
                    {
                        TwitchClaimDecision decision = TwitchClaimVerifier.Decide(twitchServer, campaign.Id, item, out _);
                        AppLogger.Debug("DropClaim", $"Twitch claim decision={decision} campaignId={campaign.Id} rewardId={item.Id} rewardName='{item.Name}'");

                        switch (decision)
                        {
                            case TwitchClaimDecision.Untracked:
                                // Twitch never credited these minutes (reward already owned, ineligible stream, ...). A claim
                                // request would still be "accepted", so none is sent; the campaign is handed back to the miner.
                                if (!untrackedReported)
                                {
                                    untrackedReported = true;
                                    AppLogger.Warn("DropClaim", $"Twitch tracks no progress for campaign '{campaign.Name}' ({campaign.Id}); '{item.Name}' was not earned. Claim skipped.");
                                    onTwitchCampaignUntracked?.Invoke(campaign.Id);
                                }
                                break;

                            case TwitchClaimDecision.NotReady:
                                // The local counter ran ahead; adopt Twitch's minutes so the remainder gets mined. No early
                                // re-check is scheduled: the miner comes back by itself once those minutes are counted, and
                                // reloading the stream sooner would only get in the way of Twitch crediting them.
                                if (!serverProgressApplied)
                                {
                                    serverProgressApplied = true;
                                    AppLogger.Info("DropClaim", $"Twitch has not finished crediting '{item.Name}' yet; syncing progress and continuing to mine.");
                                    applyTwitchServerProgress?.Invoke(campaign.Id, twitchServer.Campaigns[campaign.Id]);
                                }
                                break;

                            case TwitchClaimDecision.AlreadyClaimed:
                                AppLogger.Info("DropClaim", $"Twitch already reports '{item.Name}' as claimed; marking it claimed.");
                                markRewardClaimed(campaign.Id, item.Id);
                                break;

                            default:
                                continue;
                        }

                        readyToClaimRewards.Remove(item);
                    }
                }
            }

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
                        // Only drops Twitch confirmed (Claim) or could not be asked about (Unverified) are still in the list.
                        TwitchClaimDecision decision = TwitchClaimVerifier.Decide(twitchServer, parentCampaign.Id, item, out string? serverInstanceId);

                        claimResult = await twitchGqlService.ClaimDropAsync(parentCampaign.Id, item.Id, cancellationToken, serverInstanceId);
                        AppLogger.Debug("DropClaim", $"Twitch ClaimDropAsync result={claimResult} verified={decision == TwitchClaimDecision.Claim} campaignId={parentCampaign.Id} rewardId={item.Id}");

                        // Only a claim the inventory confirmed beforehand is remembered as definitely claimed.
                        if (claimResult && decision == TwitchClaimDecision.Claim)
                            twitchGqlService.RecordAcceptedClaim(parentCampaign.Id, item.Id);
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
                    .Where(r => readyToClaimRewards.Contains(r))
                    .Select(r => $"{r.Name} ({c.GameName}, {c.Platform})")))));

                NotificationManager.ShowNotification("Drop Ready to Claim", $"You have {readyToClaimRewards.Count} drops rewards ready to claim. Please claim them manually.", NotificationType.DropReadyToClaim, details: details);
            }

            return nextCheckAt;
        }

        // How close Twitch must be to crediting a locally ready drop for the claim pass to wait for it, and for how long.
        private const int NearlyCreditedGapMinutes = 2;
        private const int NearlyCreditedMaxWaits = 3;
        private static readonly TimeSpan NearlyCreditedWait = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Whether any locally ready Twitch drop is tracked by Twitch but still a minute or two short there.
        /// </summary>
        private static bool HasNearlyCreditedDrop(TwitchServerProgress server, IReadOnlyList<DropsCampaign> twitchReady)
        {
            foreach (DropsCampaign campaign in twitchReady)
            {
                foreach (DropsReward reward in campaign.Rewards.Where(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes))
                {
                    if (TwitchClaimVerifier.Decide(server, campaign.Id, reward, out _) != TwitchClaimDecision.NotReady)
                        continue;

                    DropsReward? serverReward = server.Campaigns[campaign.Id].FirstOrDefault(r => r.Id == reward.Id);
                    if (serverReward != null && reward.RequiredMinutes - serverReward.ProgressMinutes <= NearlyCreditedGapMinutes)
                        return true;
                }
            }

            return false;
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