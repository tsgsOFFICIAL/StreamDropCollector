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
            Func<string, string, Task<bool>> isChannelEligibleAsync,
            Func<string, Task> navigateAsync,
            LastMinedStreamersStore lastMinedStreamers,
            Action<DropsCampaign, string>? onSelectionPreview,
            CancellationToken cancellationToken)
        {
            if (candidates.Count == 0)
                return null;

            List<DropsCampaign> remaining = [.. candidates];

            while (remaining.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                DropsCampaign? best = await selectBestCampaignAsync(remaining);
                if (best == null)
                    break;

                cancellationToken.ThrowIfCancellationRequested();

                string streamUrl = await selectUrlAsync(best);
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(streamUrl))
                    onSelectionPreview?.Invoke(best, StreamerUrlParser.GetLoginFromUrl(streamUrl));

                if (string.IsNullOrWhiteSpace(streamUrl))
                {
                    AppLogger.Warn(selectionLogScope, $"{platform} campaign '{best.Name}' produced empty streamer URL; trying next candidate.");
                    remaining.Remove(best);
                    continue;
                }

                await navigateAsync(streamUrl);
                await Task.Delay(1500, cancellationToken);

                string login = StreamerUrlParser.GetLoginFromUrl(streamUrl);
                bool eligible = await isChannelEligibleAsync(login, best.Slug);

                if (!eligible)
                {
                    AppLogger.Warn(selectionLogScope, $"{platform} campaign '{best.Name}' failed streamer eligibility via API. login={login}");
                    remaining.Remove(best);
                    continue;
                }

                MiningBaseline baseline = MiningBaselineInitializer.Create(best);
                DateTime? suggestedNextCheck = MiningBaselineInitializer.EstimateSoonestRewardCompletion(best);

                AppLogger.Debug(selectionLogScope, $"Mining {platform} stream: {streamUrl}");
                AppLogger.Info(selectionLogScope, $"Selected {platform} stream '{streamUrl}' for campaign '{best.Name}' ({best.Id}).");
                lastMinedStreamers.Remember(platform, best.Slug, streamUrl);

                return new PlatformMiningResult(best, streamUrl, login, baseline, suggestedNextCheck);
            }

            AppLogger.Warn(selectionLogScope, $"No {platform} campaign passed eligibility checks. candidates={candidates.Count}");
            return null;
        }
    }
}