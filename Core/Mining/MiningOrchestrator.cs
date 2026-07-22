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
        /// <param name="campaignSnapshot">Active campaigns at the start of the cycle.</param>
        /// <param name="twitchGqlService">Twitch GQL service for claims and progress, if available.</param>
        /// <param name="twitchWebView">Twitch WebView host used for stream navigation.</param>
        /// <param name="kickWebView">Kick WebView host used for stream navigation.</param>
        /// <param name="selectBestCampaignAsync">Chooses the next campaign to mine from a candidate list.</param>
        /// <param name="selectTwitchUrlAsync">Resolves a Twitch stream URL for a campaign.</param>
        /// <param name="selectKickUrlAsync">Resolves a Kick stream URL for a campaign.</param>
        /// <param name="isTwitchEligibleAsync">Verifies a Twitch channel is still eligible for a campaign.</param>
        /// <param name="isKickEligibleAsync">Verifies a Kick channel is still eligible for a campaign.</param>
        /// <param name="navigateTwitchAsync">Navigates the Twitch WebView to a stream URL.</param>
        /// <param name="navigateKickAsync">Navigates the Kick WebView to a stream URL.</param>
        /// <param name="prepareTwitchStreamPageAsync">Optional Twitch post-navigation setup.</param>
        /// <param name="prepareKickStreamPageAsync">Optional Kick post-navigation setup.</param>
        /// <param name="lastMinedStreamers">Persists chosen stream URLs for future preference.</param>
        /// <param name="onTwitchSelectionPreview">Raised when a Twitch stream is chosen, before navigation.</param>
        /// <param name="onKickSelectionPreview">Raised when a Kick stream is chosen, before navigation.</param>
        /// <param name="markRewardClaimed">Marks a reward claimed in the active inventory.</param>
        /// <param name="updateSelectionFlags">Refreshes current-campaign highlight flags in the inventory.</param>
        /// <param name="cancellationToken">Cancellation token for the mining cycle.</param>
        /// <returns>Selection results, miner status, and the next scheduled re-evaluation time.</returns>
        public async Task<MiningOrchestratorResult> RunAsync(
            IReadOnlyList<DropsCampaign> campaignSnapshot,
            IGqlService? twitchGqlService,
            IWebViewHost? twitchWebView,
            IWebViewHost? kickWebView,
            Func<List<DropsCampaign>, Task<DropsCampaign?>> selectBestCampaignAsync,
            Func<DropsCampaign, Task<string>> selectTwitchUrlAsync,
            Func<DropsCampaign, Task<string>> selectKickUrlAsync,
            Func<string, DropsCampaign, Task<bool>> isTwitchEligibleAsync,
            Func<string, DropsCampaign, Task<bool>> isKickEligibleAsync,
            Func<string, Task> navigateTwitchAsync,
            Func<string, Task> navigateKickAsync,
            Func<Task>? prepareTwitchStreamPageAsync,
            Func<Task>? prepareKickStreamPageAsync,
            LastMinedStreamersStore lastMinedStreamers,
            Action<DropsCampaign, string> onTwitchSelectionPreview,
            Action<DropsCampaign, string> onKickSelectionPreview,
            Func<string, string, bool> markRewardClaimed,
            Action updateSelectionFlags,
            CancellationToken cancellationToken)
        {
            AppLogger.Debug(
                "TwitchMining",
                $"MiningOrchestrator.RunAsync START snapshotCount={campaignSnapshot.Count} " +
                $"twitchWebView={(twitchWebView is null ? "null" : "ok")} kickWebView={(kickWebView is null ? "null" : "ok")}");

            if (!campaignSnapshot.Any())
            {
                AppLogger.Debug("Miner", "[MiningOrchestrator] No active campaigns with progress to make. Stopping stream mining.");
                AppLogger.Info("Miner", "No active campaigns found during start; switching to Idle.");
                AppLogger.Warn("TwitchMining", "MiningOrchestrator ABORT - campaign snapshot empty.");
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

            int twitchWithProgress = campaignSnapshot.Count(c => c.Platform == Platform.Twitch && c.HasProgressToMake());
            int kickWithProgress = campaignSnapshot.Count(c => c.Platform == Platform.Kick && c.HasProgressToMake());
            AppLogger.Debug(
                "TwitchMining",
                $"MiningOrchestrator progress gate twitchWithProgress={twitchWithProgress} kickWithProgress={kickWithProgress} " +
                $"readyToClaimOnly={readyToClaimOnlyCampaigns.Count}");

            if (!campaignSnapshot.Any(c => c.HasProgressToMake()))
            {
                if (readyToClaimOnlyCampaigns.Any())
                    AppLogger.Info("Miner", $"No remaining mine progress remains; {readyToClaimOnlyCampaigns.Count} campaign(s) are waiting for manual claim.");

                AppLogger.Debug("Miner", "[MiningOrchestrator] No campaigns with progress to make after claim. Stopping stream mining.");
                AppLogger.Info("Miner", "No campaigns with progress after claim pass; switching to Idle.");
                AppLogger.Warn("TwitchMining", "MiningOrchestrator ABORT - no campaigns with HasProgressToMake().");
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

            AppLogger.Debug(
                "TwitchMining",
                $"MiningOrchestrator twitchCandidates={twitchCampaigns.Count} kickCandidates={kickCampaigns.Count} " +
                $"twitchCampaigns=[{string.Join(", ", twitchCampaigns.Select(c => $"{c.Name}(slug={c.Slug},id={c.Id})"))}]");

            PlatformMiningResult? twitchResult = null;
            if (twitchCampaigns.Count == 0)
            {
                AppLogger.Warn("TwitchMining", "MiningOrchestrator SKIP Twitch selection - no Twitch campaigns with progress.");
            }
            else if (twitchWebView == null)
            {
                AppLogger.Warn("TwitchMining", $"MiningOrchestrator SKIP Twitch selection - twitchWebView is null ({twitchCampaigns.Count} candidates ignored).");
            }
            else
            {
                twitchResult = await PlatformMiningSession.TrySelectAsync(
                    Platform.Twitch,
                    "TwitchSelection",
                    twitchCampaigns,
                    selectBestCampaignAsync,
                    selectTwitchUrlAsync,
                    isTwitchEligibleAsync,
                    navigateTwitchAsync,
                    prepareTwitchStreamPageAsync,
                    lastMinedStreamers,
                    onTwitchSelectionPreview,
                    cancellationToken);
            }

            if (twitchResult is null)
                AppLogger.Warn("TwitchMining", "MiningOrchestrator twitchResult=null after TrySelectAsync.");
            else
                AppLogger.Debug(
                    "TwitchMining",
                    $"MiningOrchestrator twitchResult OK campaign='{twitchResult.Campaign.Name}' login={twitchResult.Login} url={twitchResult.StreamUrl}");

            PlatformMiningResult? kickResult = null;
            if (kickCampaigns.Count == 0)
            {
                AppLogger.Warn("KickSelection", "MiningOrchestrator SKIP Kick selection - no Kick campaigns with progress.");
            }
            else if (kickWebView == null)
            {
                AppLogger.Warn("KickSelection", $"MiningOrchestrator SKIP Kick selection - kickWebView is null ({kickCampaigns.Count} candidates ignored).");
            }
            else
            {
                kickResult = await PlatformMiningSession.TrySelectAsync(
                    Platform.Kick,
                    "KickSelection",
                    kickCampaigns,
                    selectBestCampaignAsync,
                    selectKickUrlAsync,
                    isKickEligibleAsync,
                    navigateKickAsync,
                    prepareKickStreamPageAsync,
                    lastMinedStreamers,
                    onKickSelectionPreview,
                    cancellationToken);
            }

            if (kickResult is null)
                AppLogger.Warn("KickSelection", "MiningOrchestrator kickResult=null after TrySelectAsync.");
            else
                AppLogger.Debug(
                    "KickSelection",
                    $"MiningOrchestrator kickResult OK campaign='{kickResult.Campaign.Name}' login={kickResult.Login} url={kickResult.StreamUrl}");

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