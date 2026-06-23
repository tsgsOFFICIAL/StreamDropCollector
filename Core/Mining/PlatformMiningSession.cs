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
        public static async Task<PlatformMiningResult?> TrySelectAsync(
            Platform platform,
            string selectionLogScope,
            IReadOnlyList<DropsCampaign> candidates,
            Func<List<DropsCampaign>, Task<DropsCampaign?>> selectBestCampaignAsync,
            Func<DropsCampaign, Task<string>> selectUrlAsync,
            Func<string, DropsCampaign, Task<bool>> isChannelEligibleAsync,
            Func<string, Task> navigateAsync,
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
            AppLogger.Info(
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

                AppLogger.Info(
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

                AppLogger.Info(logScope, $"TrySelectAsync {platform} navigating to {streamUrl}");
                await navigateAsync(streamUrl);
                await Task.Delay(1500, cancellationToken);

                string login = StreamerUrlParser.GetLoginFromUrl(streamUrl);
                AppLogger.Info(logScope, $"TrySelectAsync {platform} post-navigate eligibility check login={login} gameId={best.GameId ?? "(none)"} slug='{best.Slug}'");
                bool eligible = await isChannelEligibleAsync(login, best);

                if (!eligible)
                {
                    AppLogger.Warn(logScope, $"{platform} campaign '{best.Name}' failed post-navigate eligibility. login={login} gameId={best.GameId ?? "(none)"} slug='{best.Slug}'");
                    remaining.Remove(best);
                    continue;
                }

                MiningBaseline baseline = MiningBaselineInitializer.Create(best);
                DateTime? suggestedNextCheck = MiningBaselineInitializer.EstimateSoonestRewardCompletion(best);

                AppLogger.Debug(selectionLogScope, $"Mining {platform} stream: {streamUrl}");
                AppLogger.Info(logScope, $"TrySelectAsync {platform} SUCCESS url={streamUrl} login={login} campaign='{best.Name}' id={best.Id}");
                AppLogger.Info(selectionLogScope, $"Selected {platform} stream '{streamUrl}' for campaign '{best.Name}' ({best.Id}).");
                lastMinedStreamers.Remember(platform, best.Slug, streamUrl);

                return new PlatformMiningResult(best, streamUrl, login, baseline, suggestedNextCheck);
            }

            AppLogger.Warn(logScope, $"TrySelectAsync {platform} FAILED - no campaign passed. attempts={attempt} initialCandidates={candidates.Count}");
            AppLogger.Warn(selectionLogScope, $"No {platform} campaign passed eligibility checks. candidates={candidates.Count}");
            return null;
        }
    }
}