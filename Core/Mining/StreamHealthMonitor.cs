using Core.Logging;
using System.Timers;
using System.Windows;

namespace Core.Mining
{
    /// <summary>
    /// Periodically checks stream eligibility via live-channel APIs and triggers re-evaluation when needed.
    /// </summary>
    public sealed class StreamHealthMonitor : IDisposable
    {
        private System.Timers.Timer? _timer;

        /// <summary>
        /// Host callbacks and state used during health checks.
        /// </summary>
        public sealed class Host
        {
            public required Func<Task<bool>> IsTwitchEligibleAsync { get; init; }
            public required Func<Task<bool>> IsKickEligibleAsync { get; init; }
            public required Func<bool> HasTwitchCampaignsWithProgress { get; init; }
            public required Func<bool> HasKickCampaignsWithProgress { get; init; }
            public required Func<bool> GetLastKnownTwitchOnline { get; init; }
            public required Func<bool> GetLastKnownKickOnline { get; init; }
            public required Action<bool> SetLastKnownTwitchOnline { get; init; }
            public required Action<bool> SetLastKnownKickOnline { get; init; }
            public required Func<Task> RequestReevaluationAsync { get; init; }
        }

        /// <summary>
        /// Starts periodic health monitoring, replacing any existing timer.
        /// </summary>
        public void Start(Host host)
        {
            Stop();

            _timer = new System.Timers.Timer(30 * 1000);
            _timer.Elapsed += async (_, _) =>
            {
                await await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    bool twitchEligible = await host.IsTwitchEligibleAsync();
                    bool kickEligible = await host.IsKickEligibleAsync();

                    AppLogger.Debug("HealthCheck", $"Twitch eligible: {twitchEligible} | Kick eligible: {kickEligible}");
                    AppLogger.Info("HealthCheck", $"Twitch eligible={twitchEligible}; Kick eligible={kickEligible}");

                    bool twitchNeedsReevaluation = host.HasTwitchCampaignsWithProgress() && !twitchEligible && host.GetLastKnownTwitchOnline();
                    bool kickNeedsReevaluation = host.HasKickCampaignsWithProgress() && !kickEligible && host.GetLastKnownKickOnline();

                    if (!twitchNeedsReevaluation && !kickNeedsReevaluation)
                        return;

                    if (!twitchEligible)
                        host.SetLastKnownTwitchOnline(false);

                    if (!kickEligible)
                        host.SetLastKnownKickOnline(false);

                    AppLogger.Debug("HealthCheck", "Stream ineligible via API -> forcing re-evaluation");
                    AppLogger.Warn("HealthCheck", $"Forcing re-evaluation. twitchEligible={twitchEligible}, kickEligible={kickEligible}");
                    Stop();
                    await host.RequestReevaluationAsync();
                });
            };

            _timer.AutoReset = true;
            _timer.Start();
        }

        /// <summary>
        /// Stops and disposes the health-check timer.
        /// </summary>
        public void Stop()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }

        /// <inheritdoc />
        public void Dispose() => Stop();
    }
}