using Core.Enums;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using Core.Stores;

namespace Core.Mining
{
    /// <summary>
    /// Coordinates claim processing, per-platform stream selection, and scheduling hints for one mining cycle.
    /// </summary>
    public sealed class MiningOrchestrator
    {
        private readonly DropClaimService _dropClaimService = new();

        /// <summary>
        /// Runs one mining evaluation cycle against the provided campaign snapshot.
        /// </summary>
        public async Task<MiningOrchestratorResult> RunAsync(
            IReadOnlyList<DropsCampaign> campaignSnapshot,
            IGqlService? twitchGqlService,
            IWebViewHost? twitchWebView,
            IWebViewHost? kickWebView,
            Func<List<DropsCampaign>, Task<DropsCampaign?>> selectBestCampaignAsync,
            Func<DropsCampaign, Task<string>> selectTwitchUrlAsync,
            Func<DropsCampaign, Task<string>> selectKickUrlAsync,
            Func<string, string, Task<bool>> isTwitchEligibleAsync,
            Func<string, string, Task<bool>> isKickEligibleAsync,
            Func<string, Task> navigateTwitchAsync,
            Func<string, Task> navigateKickAsync,
            LastMinedStreamersStore lastMinedStreamers,
            Action<DropsCampaign, string> onTwitchSelectionPreview,
            Action<DropsCampaign, string> onKickSelectionPreview,
            Func<string, string, bool> markRewardClaimed,
            Action updateSelectionFlags,
            CancellationToken cancellationToken)
        {
            if (!campaignSnapshot.Any())
            {
                AppLogger.Debug("Miner", "[MiningOrchestrator] No active campaigns with progress to make. Stopping stream mining.");
                AppLogger.Info("Miner", "No active campaigns found during start; switching to Idle.");
                return new MiningOrchestratorResult
                {
                    CompletedSelectionCycle = false,
                    MinerStatus = "Idle",
                    NextCheckAt = DateTime.Now.AddHours(1)
                };
            }

            cancellationToken.ThrowIfCancellationRequested();

            DateTime nextCheckAt = DateTime.Now.AddHours(1);

            nextCheckAt = await _dropClaimService.ProcessAutoClaimsAsync(
                campaignSnapshot,
                nextCheckAt,
                twitchGqlService,
                kickWebView,
                markRewardClaimed,
                cancellationToken);

            List<DropsCampaign> readyToClaimOnlyCampaigns = campaignSnapshot
                .Where(c => c.HasReadyToClaimRewards() && !c.HasProgressToMake())
                .ToList();

            if (!campaignSnapshot.Any(c => c.HasProgressToMake()))
            {
                if (readyToClaimOnlyCampaigns.Any())
                    AppLogger.Info("Miner", $"No remaining mine progress remains; {readyToClaimOnlyCampaigns.Count} campaign(s) are waiting for manual claim.");

                AppLogger.Debug("Miner", "[MiningOrchestrator] No campaigns with progress to make after claim. Stopping stream mining.");
                AppLogger.Info("Miner", "No campaigns with progress after claim pass; switching to Idle.");
                updateSelectionFlags();

                return new MiningOrchestratorResult
                {
                    CompletedSelectionCycle = false,
                    MinerStatus = "Idle",
                    NextCheckAt = nextCheckAt
                };
            }

            cancellationToken.ThrowIfCancellationRequested();

            List<DropsCampaign> twitchCampaigns = campaignSnapshot
                .Where(c => c.Platform == Platform.Twitch && c.HasProgressToMake())
                .ToList();

            List<DropsCampaign> kickCampaigns = campaignSnapshot
                .Where(c => c.Platform == Platform.Kick && c.HasProgressToMake())
                .ToList();

            PlatformMiningResult? twitchResult = null;
            if (twitchCampaigns.Count != 0 && twitchWebView != null)
            {
                twitchResult = await PlatformMiningSession.TrySelectAsync(
                    Platform.Twitch,
                    "TwitchSelection",
                    twitchCampaigns,
                    selectBestCampaignAsync,
                    selectTwitchUrlAsync,
                    isTwitchEligibleAsync,
                    navigateTwitchAsync,
                    lastMinedStreamers,
                    onTwitchSelectionPreview,
                    cancellationToken);
            }

            PlatformMiningResult? kickResult = null;
            if (kickCampaigns.Count != 0 && kickWebView != null)
            {
                kickResult = await PlatformMiningSession.TrySelectAsync(
                    Platform.Kick,
                    "KickSelection",
                    kickCampaigns,
                    selectBestCampaignAsync,
                    selectKickUrlAsync,
                    isKickEligibleAsync,
                    navigateKickAsync,
                    lastMinedStreamers,
                    onKickSelectionPreview,
                    cancellationToken);
            }

            if (twitchResult?.SuggestedNextCheckAt != null)
                nextCheckAt = MiningBaselineInitializer.Earliest(nextCheckAt, twitchResult.SuggestedNextCheckAt);

            if (kickResult?.SuggestedNextCheckAt != null)
                nextCheckAt = MiningBaselineInitializer.Earliest(nextCheckAt, kickResult.SuggestedNextCheckAt);

            if (twitchResult == null && kickResult == null)
                AppLogger.Warn("Miner", "No stream selected after evaluation cycle; status may oscillate with health checks.");

            string minerStatus = twitchResult != null || kickResult != null ? "Mining" : "Idle";

            return new MiningOrchestratorResult
            {
                CompletedSelectionCycle = true,
                MinerStatus = minerStatus,
                NextCheckAt = nextCheckAt,
                Twitch = twitchResult,
                Kick = kickResult
            };
        }
    }
}