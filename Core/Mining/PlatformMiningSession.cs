using Core.Enums;
using Core.Helpers;
using Core.Logging;
using Core.Models;
using Core.Stores;

namespace Core.Mining
{
    /// <summary>
    /// Selects and validates a live stream for one platform, trying campaign candidates in priority order.
    /// </summary>
    public static class PlatformMiningSession
    {
        /// <summary>
        /// Attempts to select an eligible stream for the given platform campaigns.
        /// </summary>
        /// <param name="platform">Platform being evaluated (Twitch or Kick).</param>
        /// <param name="selectionLogScope">Log scope label used for non-Twitch selection traces.</param>
        /// <param name="candidates">Campaigns with remaining progress for this platform.</param>
        /// <param name="selectBestCampaignAsync">Chooses the next campaign to try from the remaining candidate list.</param>
        /// <param name="selectUrlAsync">Resolves a stream URL for the chosen campaign.</param>
        /// <param name="isChannelEligibleAsync">Verifies the channel is live and playing the correct game/category.</param>
        /// <param name="navigateAsync">Navigates the platform WebView to the selected stream URL.</param>
        /// <param name="prepareStreamPageAsync">Optional post-navigation setup (quality, mature gate, refresh).</param>
        /// <param name="lastMinedStreamers">Persists the chosen stream URL for future preference.</param>
        /// <param name="onSelectionPreview">Optional callback raised before navigation with the chosen campaign and login.</param>
        /// <param name="cancellationToken">Cancellation token for the mining cycle.</param>
        /// <returns>A mining result with baseline progress, or <see langword="null"/> when no stream could be selected.</returns>
        public static async Task<PlatformMiningResult?> TrySelectAsync(
            Platform platform,
            string selectionLogScope,
            IReadOnlyList<DropsCampaign> candidates,
            Func<List<DropsCampaign>, Task<DropsCampaign?>> selectBestCampaignAsync,
            Func<DropsCampaign, Task<string>> selectUrlAsync,
            Func<string, DropsCampaign, Task<bool>> isChannelEligibleAsync,
            Func<string, Task> navigateAsync,
            Func<Task>? prepareStreamPageAsync,
            LastMinedStreamersStore lastMinedStreamers,
            Action<DropsCampaign, string>? onSelectionPreview,
            CancellationToken cancellationToken)
        {
            if (candidates.Count == 0)
            {
                AppLogger.Warn("TwitchMining", $"TrySelectAsync {platform} ABORT - zero candidates.");
                return null;
            }

            string logScope = platform == Platform.Twitch ? "TwitchMining" : selectionLogScope;
            AppLogger.Debug(
                logScope,
                $"TrySelectAsync {platform} START candidates={candidates.Count} " +
                $"[{string.Join(", ", candidates.Select(c => $"{c.Name}(slug={c.Slug})"))}]");

            List<DropsCampaign> remaining = [.. candidates];
            int attempt = 0;

            while (remaining.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                DropsCampaign? best = await selectBestCampaignAsync(remaining);
                if (best == null)
                {
                    AppLogger.Warn(logScope, $"TrySelectAsync {platform} attempt={attempt} selectBestCampaign returned null; remaining={remaining.Count}");
                    break;
                }

                AppLogger.Debug(
                    logScope,
                    $"TrySelectAsync {platform} attempt={attempt} picked campaign='{best.Name}' id={best.Id} slug='{best.Slug}' remaining={remaining.Count}");

                cancellationToken.ThrowIfCancellationRequested();

                string streamUrl = await selectUrlAsync(best);
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(streamUrl))
                    onSelectionPreview?.Invoke(best, StreamerUrlParser.GetLoginFromUrl(streamUrl));

                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    AppLogger.Warn(logScope, $"{platform} campaign '{best.Name}' produced empty streamer URL; trying next candidate.");
                    remaining.Remove(best);
                    continue;
                }

                AppLogger.Debug(logScope, $"TrySelectAsync {platform} navigating to {streamUrl}");
                await navigateAsync(streamUrl);
                await Task.Delay(1500, cancellationToken);

                if (prepareStreamPageAsync != null)
                {
                    AppLogger.Debug(logScope, $"TrySelectAsync {platform} preparing stream page (mature gate, quality, refresh).");
                    await prepareStreamPageAsync();
                }

                string login = StreamerUrlParser.GetLoginFromUrl(streamUrl);
                AppLogger.Debug(logScope, $"TrySelectAsync {platform} post-navigate eligibility check login={login} gameId={best.GameId ?? "(none)"} slug='{best.Slug}'");
                bool eligible = await isChannelEligibleAsync(login, best);

                if (!eligible)
                {
                    AppLogger.Warn(logScope, $"{platform} campaign '{best.Name}' failed post-navigate eligibility. login={login} gameId={best.GameId ?? "(none)"} slug='{best.Slug}'");
                    remaining.Remove(best);
                    continue;
                }

                MiningBaseline baseline = MiningBaselineInitializer.Create(best);
                DateTime? suggestedNextCheck = MiningBaselineInitializer.EstimateSoonestRewardCompletion(best);

                lastMinedStreamers.Remember(platform, best.Slug, streamUrl);
                AppLogger.Info("Miner", $"{platform}: watching {login} for '{best.Name}'.");

                return new PlatformMiningResult(best, streamUrl, login, baseline, suggestedNextCheck);
            }

            AppLogger.Warn(logScope, $"TrySelectAsync {platform} FAILED - no campaign passed. attempts={attempt} initialCandidates={candidates.Count}");
            AppLogger.Warn(selectionLogScope, $"No {platform} campaign passed eligibility checks. candidates={candidates.Count}");
            return null;
        }
    }
}