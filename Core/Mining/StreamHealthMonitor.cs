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
            /// <summary>Checks whether the currently mined Twitch channel is still live and game-eligible.</summary>
            public required Func<Task<bool>> IsTwitchEligibleAsync { get; init; }

            /// <summary>Checks whether the currently mined Kick channel is still live and category-eligible.</summary>
            public required Func<Task<bool>> IsKickEligibleAsync { get; init; }

            /// <summary>Returns whether any Twitch campaigns still have progress to make.</summary>
            public required Func<bool> HasTwitchCampaignsWithProgress { get; init; }

            /// <summary>Returns whether any Kick campaigns still have progress to make.</summary>
            public required Func<bool> HasKickCampaignsWithProgress { get; init; }

            /// <summary>Gets the last known Twitch online state for the mined channel.</summary>
            public required Func<bool> GetLastKnownTwitchOnline { get; init; }

            /// <summary>Gets the last known Kick online state for the mined channel.</summary>
            public required Func<bool> GetLastKnownKickOnline { get; init; }

            /// <summary>Updates the cached Twitch online state after an eligibility failure.</summary>
            public required Action<bool> SetLastKnownTwitchOnline { get; init; }

            /// <summary>Updates the cached Kick online state after an eligibility failure.</summary>
            public required Action<bool> SetLastKnownKickOnline { get; init; }

            /// <summary>Triggers a full stream re-selection when a mined channel goes ineligible.</summary>
            public required Func<Task> RequestReevaluationAsync { get; init; }

            /// <summary>Optional: logs the real Kick &lt;video&gt; element playback state (paused, currentTime, buffered, errors) each tick.</summary>
            public Func<Task>? LogKickPlaybackDiagnosticsAsync { get; init; }
        }

        /// <summary>
        /// Starts periodic health monitoring, replacing any existing timer.
        /// </summary>
        /// <param name="host">Callbacks and state accessors used on each 30-second health tick.</param>
        public void Start(Host host)
        {
            AppLogger.Debug("HealthCheck", "StreamHealthMonitor.Start - (re)starting 30s health-check timer.");
            Stop();

            _timer = new System.Timers.Timer(30 * 1000);
            _timer.Elapsed += async (_, _) =>
            {
                await await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    bool twitchHasProgress = host.HasTwitchCampaignsWithProgress();
                    bool kickHasProgress = host.HasKickCampaignsWithProgress();
                    bool twitchWasOnline = host.GetLastKnownTwitchOnline();
                    bool kickWasOnline = host.GetLastKnownKickOnline();

                    if (host.LogKickPlaybackDiagnosticsAsync != null)
                    {
                        try
                        {
                            await host.LogKickPlaybackDiagnosticsAsync();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("KickPlayback", $"LogKickPlaybackDiagnosticsAsync threw: {ex.Message}");
                        }
                    }

                    bool twitchEligible = await host.IsTwitchEligibleAsync();
                    bool kickEligible = await host.IsKickEligibleAsync();

                    AppLogger.Debug("HealthCheck", $"Twitch eligible: {twitchEligible} | Kick eligible: {kickEligible}");
                    AppLogger.Debug(
                        "TwitchMining",
                        $"HealthCheck twitchEligible={twitchEligible} kickEligible={kickEligible} " +
                        $"twitchHasProgress={twitchHasProgress} twitchWasOnline={twitchWasOnline}");
                    AppLogger.Debug(
                        "HealthCheck",
                        $"Kick health tick kickEligible={kickEligible} kickHasProgress={kickHasProgress} kickWasOnline={kickWasOnline}");

                    bool twitchNeedsReevaluation = twitchHasProgress && !twitchEligible && twitchWasOnline;
                    bool kickNeedsReevaluation = kickHasProgress && !kickEligible && kickWasOnline;

                    if (!twitchNeedsReevaluation && !kickNeedsReevaluation)
                    {
                        AppLogger.Debug("HealthCheck", "HealthCheck tick OK - no re-evaluation needed.");
                        return;
                    }

                    if (!twitchEligible)
                        host.SetLastKnownTwitchOnline(false);

                    if (!kickEligible)
                        host.SetLastKnownKickOnline(false);

                    AppLogger.Debug("HealthCheck", "Stream ineligible via API -> forcing re-evaluation");
                    AppLogger.Warn(
                        "HealthCheck",
                        $"Forcing re-evaluation. twitchEligible={twitchEligible}, kickEligible={kickEligible}, " +
                        $"twitchNeedsReevaluation={twitchNeedsReevaluation}, kickNeedsReevaluation={kickNeedsReevaluation}");
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

        /// <summary>
        /// Stops health monitoring and releases the timer.
        /// </summary>
        public void Dispose() => Stop();
    }
}