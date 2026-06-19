using Core.Enums;
using Core.Interfaces;
using Core.Logging;
using Core.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Timers;
using System.Windows;
using System.Windows.Input;

namespace Core.Managers
{
    /// <summary>
    /// Central manager for active drop campaigns, stream mining, progress tracking, and reward claiming across Twitch and Kick.
    /// </summary>
    /// <remarks>This class follows the singleton pattern. It coordinates hidden WebView hosts, selects campaigns to mine,
    /// raises UI-facing progress events, and persists pinned campaign and last-mined streamer state.</remarks>
    public sealed class DropsInventoryManager
    {
        private static readonly Lazy<DropsInventoryManager> _instance = new(() => new DropsInventoryManager());

        /// <summary>
        /// Gets the singleton instance of the drops inventory manager.
        /// </summary>
        public static DropsInventoryManager Instance => _instance.Value;

        /// <summary>
        /// Gets the collection of currently active drop campaigns displayed in the inventory UI.
        /// </summary>
        public ObservableCollection<DropsCampaign> ActiveCampaigns { get; } = new ObservableCollection<DropsCampaign>();

        /// <summary>
        /// Gets the WebView host used for Twitch drops operations, or null if not yet initialized.
        /// </summary>
        public IWebViewHost? TwitchWebView { get; private set; }

        /// <summary>
        /// Gets the WebView host used for Kick drops operations, or null if not yet initialized.
        /// </summary>
        public IWebViewHost? KickWebView { get; private set; }

        /// <summary>
        /// Occurs when live Twitch campaign or drop progress percentages change.
        /// </summary>
        /// <remarks>The first value is overall campaign completion (0–100). The second value is progress toward the current unclaimed reward (0–100).</remarks>
        public event Action<byte, byte>? TwitchProgressChanged;

        /// <summary>
        /// Occurs when live Kick campaign or drop progress percentages change.
        /// </summary>
        /// <remarks>The first value is overall campaign completion (0–100). The second value is progress toward the current unclaimed reward (0–100).</remarks>
        public event Action<byte, byte>? KickProgressChanged;

        /// <summary>
        /// Occurs when the miner status label changes (for example, Idle, Starting, Evaluating, or Mining).
        /// </summary>
        public event Action<string>? MinerStatusChanged;

        /// <summary>
        /// Occurs when the Twitch channel being mined changes.
        /// </summary>
        /// <remarks>An empty string indicates that no Twitch channel is currently being mined.</remarks>
        public event Action<string>? TwitchChannelChanged;

        /// <summary>
        /// Occurs when the Kick channel being mined changes.
        /// </summary>
        /// <remarks>An empty string indicates that no Kick channel is currently being mined.</remarks>
        public event Action<string>? KickChannelChanged;

        /// <summary>
        /// Occurs when the Twitch campaign being mined changes.
        /// </summary>
        /// <remarks>Arguments are the campaign display name and game image URL. An empty name with a null URL means the selection was cleared.</remarks>
        public event Action<string, string?>? TwitchCampaignChanged;

        /// <summary>
        /// Occurs when the Kick campaign being mined changes.
        /// </summary>
        /// <remarks>Arguments are the campaign display name and game image URL. An empty name with a null URL means the selection was cleared.</remarks>
        public event Action<string, string?>? KickCampaignChanged;

        /// <summary>
        /// Occurs when the current Twitch reward being progressed changes.
        /// </summary>
        /// <remarks>Arguments are the reward display name and image URL. An empty name with a null URL means the selection was cleared.</remarks>
        public event Action<string, string?>? TwitchDropChanged;

        /// <summary>
        /// Occurs when the current Kick reward being progressed changes.
        /// </summary>
        /// <remarks>Arguments are the reward display name and image URL. An empty name with a null URL means the selection was cleared.</remarks>
        public event Action<string, string?>? KickDropChanged;

        // Currently mined campaigns
        private DropsCampaign? _currentTwitchCampaign;
        private string? _currentTwitchLogin; // login of the Twitch streamer currently being mined
        private string? _lastTwitchDropId; // id of the last reward reported via TwitchDropChanged
        private string? _lastKickDropId;   // id of the last reward reported via KickDropChanged
        private DropsCampaign? _currentKickCampaign;
        private IGqlService? _twitchGqlService;

        private string? _pinnedCampaignId; // Used to track if the user has manually pinned a campaign to mine regardless of order

        private static readonly string _pinnedCampaignCacheFilePath = Path.Combine(
            Environment.ExpandEnvironmentVariables("%APPDATA%"),
            "Stream Drop Collector",
            "PinnedCampaignCache.json");

        private int _twitchMinedSeconds;
        private int _kickMinedSeconds;
        private int _twitchDropMinedSeconds;
        private int _kickDropMinedSeconds;

        private bool _lastKnownKickOnlineState;
        private bool _lastKnownTwitchOnlineState;

        // Timer for live ticking
        private readonly System.Timers.Timer _liveProgressTimer = new(1000);
        private System.Timers.Timer? _recheckTimer;
        private System.Timers.Timer? _streamHealthTimer;

        private int _twitchAppliedMinuteBucket;
        private int _kickAppliedMinuteBucket;

        private readonly SemaphoreSlim _startMiningLock = new(1, 1);
        private CancellationTokenSource? _startMiningCts;
        private bool _isPaused;
        private readonly object _lastStreamerSync = new();
        private readonly Dictionary<string, string> _lastTwitchStreamers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _lastKickStreamers = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _campaignSnapshotSync = new();
        private List<DropsCampaign> _lastKnownCampaigns = new();

        private static readonly string _lastMinedStreamersFilePath = Path.Combine(
            Environment.ExpandEnvironmentVariables("%APPDATA%"),
            "Stream Drop Collector",
            "LastMinedStreamers.json");

        private static bool IsVerboseDebugEnabled => UISettingsManager.Instance.VerboseDebugLogging;

        /// <summary>
        /// Logs a message at the informational level if verbose debug logging is enabled.
        /// </summary>
        /// <param name="scope">The logical scope or category associated with the log message. Used to group related log entries.</param>
        /// <param name="message">The message to log. Should provide relevant information about the operation or event.</param>
        private static void VerboseLog(string scope, string message)
        {
            if (IsVerboseDebugEnabled)
                AppLogger.Info(scope, message);
        }

        /// <summary>
        /// Gets a command that switches the currently mined campaign to the specified campaign when the miner is not paused.
        /// </summary>
        /// <remarks>When executed, this command updates the pinned campaign ID and restarts stream mining.
        /// It has no effect if the miner is paused or the campaign argument is null.</remarks>
        public ICommand SwitchCampaignCommand => new Utility.RelayCommand<DropsCampaign>(async campaign =>
        {
            if (campaign == null || _isPaused)
                return;

            AppLogger.Info("Miner", $"User manually switched to campaign '{campaign.Name}' ({campaign.Id}).");
            _pinnedCampaignId = campaign.Id;
            SavePinnedCampaignToDisk();
            await StartMiningStreams(true);
        });


        /// <summary>
        /// Initializes a new instance of the DropsInventoryManager class.
        /// </summary>
        /// <remarks>This constructor is private to enforce the singleton pattern. It sets up event
        /// handlers and initializes internal state required for managing drops inventory. Instances of this class can
        /// only be created internally within the class.</remarks>
        private DropsInventoryManager()
        {
            LoadLastMinedStreamers();
            LoadPinnedCampaignFromDisk();
            UISettingsManager.Instance.MiningPriorityModeChanged += OnMiningPriorityModeChanged;
            UISettingsManager.Instance.GameWhitelistChanged += OnGameWhitelistChanged;

            _liveProgressTimer.Elapsed += OnLiveProgressTick;
            _liveProgressTimer.AutoReset = true;
        }

        /// <summary>
        /// Handles changes to the mining priority mode by applying the specified mode.
        /// </summary>
        /// <param name="mode">The new mining priority mode to apply.</param>
        private void OnMiningPriorityModeChanged(MiningPriorityMode mode)
        {
            _ = ApplyMiningPriorityModeChangeAsync(mode);
        }
        /// <summary>
        /// Handles changes to the game whitelist for the specified platform.
        /// </summary>
        /// <param name="platform">The platform for which the game whitelist has changed.</param>
        private void OnGameWhitelistChanged(Platform platform)
        {
            _ = ApplyGameWhitelistChangeAsync(platform);
        }
        /// <summary>
        /// Applies a change to the mining priority mode and triggers an immediate re-evaluation of active campaigns if
        /// applicable.
        /// </summary>
        /// <remarks>If the miner is paused, there are no active campaigns, or no webviews are
        /// initialized, the re-evaluation is skipped. Logging is performed to indicate the outcome of the
        /// operation.</remarks>
        /// <param name="mode">The new mining priority mode to apply. Determines how mining resources are prioritized during stream
        /// evaluation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task ApplyMiningPriorityModeChangeAsync(MiningPriorityMode mode)
        {
            try
            {
                AppLogger.Info("Miner", $"Mining priority mode changed to {mode}. Triggering immediate re-evaluation.");

                if (_isPaused)
                {
                    AppLogger.Warn("Miner", "Priority mode changed while miner is paused; re-evaluation skipped.");
                    return;
                }

                if (!ActiveCampaigns.Any())
                {
                    AppLogger.Warn("Miner", "Priority mode changed but there are no active campaigns; re-evaluation skipped.");
                    return;
                }

                if (TwitchWebView == null && KickWebView == null)
                {
                    AppLogger.Warn("Miner", "Priority mode changed but no webviews are initialized; re-evaluation skipped.");
                    return;
                }

                AppLogger.Debug("Miner", $"Immediate re-evaluation starting after priority mode change. activeCampaigns={ActiveCampaigns.Count}");
                await StartMiningStreams(true);
                AppLogger.Info("Miner", "Immediate re-evaluation completed after priority mode change.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Miner", "Failed to apply mining priority mode change immediately.", ex);
            }
        }
        /// <summary>
        /// Applies changes to the game whitelist for the specified platform and triggers an immediate re-evaluation of
        /// active campaigns if appropriate.
        /// </summary>
        /// <remarks>Re-evaluation is skipped if the miner is paused, if there are no active campaigns
        /// after filtering, or if no webviews are initialized. Logging is performed to provide information about the
        /// operation's progress and any conditions that prevent re-evaluation.</remarks>
        /// <param name="platform">The platform for which the game whitelist has changed. Determines which set of campaigns and streams are
        /// affected by the update.</param>
        /// <returns>A task that represents the asynchronous operation of applying the whitelist change and re-evaluating active
        /// campaigns.</returns>
        private async Task ApplyGameWhitelistChangeAsync(Platform platform)
        {
            try
            {
                AppLogger.Info("Miner", $"{platform} game whitelist changed. Triggering immediate re-evaluation.");

                RefreshActiveCampaignsFromLatestSnapshot();

                if (_isPaused)
                {
                    AppLogger.Warn("Miner", "Whitelist changed while miner is paused; re-evaluation skipped.");
                    return;
                }

                if (!ActiveCampaigns.Any())
                {
                    AppLogger.Warn("Miner", "Whitelist changed but there are no active campaigns after filtering; re-evaluation skipped.");
                    return;
                }

                if (TwitchWebView == null && KickWebView == null)
                {
                    AppLogger.Warn("Miner", "Whitelist changed but no webviews are initialized; re-evaluation skipped.");
                    return;
                }

                AppLogger.Debug("Miner", $"Immediate re-evaluation starting after whitelist change. activeCampaigns={ActiveCampaigns.Count}");
                await StartMiningStreams(true);
                AppLogger.Info("Miner", "Immediate re-evaluation completed after whitelist change.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Miner", "Failed to apply game whitelist change immediately.", ex);
            }
        }
        /// <summary>
        /// Refreshes the list of active campaigns using the most recent campaign snapshot and updates the UI
        /// accordingly.
        /// </summary>
        /// <remarks>This method synchronizes the active campaigns with the latest known snapshot and
        /// applies UI filters to determine which campaigns are displayed. It must be called on the UI thread, as it
        /// updates UI-bound collections and settings.</remarks>
        private void RefreshActiveCampaignsFromLatestSnapshot()
        {
            List<DropsCampaign> snapshot;
            lock (_campaignSnapshotSync)
            {
                snapshot = [.. _lastKnownCampaigns];
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                List<DropsCampaign> sourceCampaigns = snapshot.Count != 0
                    ? snapshot
                    : [.. ActiveCampaigns];

                UISettingsManager.Instance.UpdateAvailableGameFilterOptions(sourceCampaigns);

                // Materialize before iterating to avoid concurrent modification
                List<DropsCampaign> filteredCampaigns = sourceCampaigns
                    .Where(c => UISettingsManager.Instance.IsCampaignAllowedByWhitelist(c))
                    .Where(c => c.StartsAt <= DateTimeOffset.Now && c.EndsAt > DateTimeOffset.Now)
                    .OrderBy(x => x.Platform).ThenBy(x => x.GameName)
                    .ToList();

                ActiveCampaigns.Clear();
                // Safe: filtered is materialized list
                foreach (DropsCampaign campaign in filteredCampaigns)
                    ActiveCampaigns.Add(campaign);

                UpdateCurrentSelectionFlags();
            });
        }
        /// <summary>
        /// Applies the specified number of minutes of progress to the active campaign for the given platform and
        /// campaign identifier.
        /// </summary>
        /// <remarks>If the specified campaign is not found or has no progress to make, no changes are
        /// applied. The method updates the progress for all rewards in the campaign and synchronizes the current
        /// campaign selection if applicable. This method must be called from the UI thread, as it updates UI-bound
        /// collections.</remarks>
        /// <param name="platform">The platform on which the campaign is active. Determines which campaign collection to update.</param>
        /// <param name="campaignId">The unique identifier of the campaign to which progress will be applied.</param>
        /// <param name="minutesToAdd">The number of minutes to add to the campaign's progress. Must be greater than zero.</param>
        private void ApplyMinuteProgressToActiveCampaign(Platform platform, string campaignId, int minutesToAdd)
        {
            if (minutesToAdd <= 0)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                DropsCampaign? campaign = ActiveCampaigns.FirstOrDefault(c => c.Platform == platform && c.Id == campaignId);
                if (campaign == null || !campaign.HasProgressToMake())
                {
                    switch (platform)
                    {
                        case Platform.Twitch:
                            _currentTwitchCampaign = null;
                            break;
                        case Platform.Kick:
                            _currentKickCampaign = null;
                            break;
                    }

                    UpdateCurrentSelectionFlags();
                    return;
                }

                int campaignIndex = ActiveCampaigns.IndexOf(campaign);
                if (campaignIndex < 0)
                    return;

                List<DropsReward> updatedRewards = new List<DropsReward>(campaign.Rewards.Count);
                foreach (DropsReward reward in campaign.Rewards)
                {
                    int newProgress = reward.IsClaimed || reward.ProgressMinutes >= reward.RequiredMinutes
                        ? reward.ProgressMinutes
                        : Math.Min(reward.ProgressMinutes + minutesToAdd, reward.RequiredMinutes);

                    updatedRewards.Add(reward with { ProgressMinutes = newProgress });
                }

                VerboseLog("MinuteTick", $"campaignId={campaign.Id}, platform={campaign.Platform}, minutesAdded={minutesToAdd}, rewardsUpdated={campaign.Rewards.Count}, unclaimedRewards={campaign.Rewards.Count(r => !r.IsClaimed)}");
                VerboseLog("RewardTransition", $"platform={platform}, campaignId={campaignId}, minutesAdded={minutesToAdd}, rewards={string.Join(", ", updatedRewards.Select(r => $"{r.Name}:{r.ProgressMinutes}/{r.RequiredMinutes}(claimed={r.IsClaimed})"))}");

                DropsCampaign updatedCampaign = campaign with { Rewards = updatedRewards };
                ActiveCampaigns[campaignIndex] = updatedCampaign;

                if (platform == Platform.Twitch && _currentTwitchCampaign?.Id == campaignId)
                    _currentTwitchCampaign = updatedCampaign;

                if (platform == Platform.Kick && _currentKickCampaign?.Id == campaignId)
                    _currentKickCampaign = updatedCampaign;

                UpdateCurrentSelectionFlags();
            });
        }
        /// <summary>
        /// Handles the timer tick event to update live progress for active Twitch and Kick campaigns.
        /// </summary>
        /// <remarks>This method increments the mined time for each active campaign and raises the
        /// corresponding progress changed events. It is intended to be used as an event handler for timer-based
        /// progress updates.</remarks>
        /// <param name="sender">The source of the event, typically the timer that triggered the tick.</param>
        /// <param name="e">An ElapsedEventArgs object that contains the event data.</param>
        private void OnLiveProgressTick(object? sender, ElapsedEventArgs e)
        {
            DropsCampaign? currentTwitchCampaign = _currentTwitchCampaign;
            if (currentTwitchCampaign != null)
            {
                _twitchMinedSeconds++;
                _twitchDropMinedSeconds++;

                DropsReward? nextTwitchReward = currentTwitchCampaign.Rewards
                    .Where(r => !r.IsClaimed)
                    .OrderBy(r => r.RequiredMinutes)
                    .FirstOrDefault();

                VerboseLog("DropPointer", $"Twitch nextReward={nextTwitchReward?.Name ?? "none"}, nextRewardId={nextTwitchReward?.Id ?? "none"}, requiredMinutes={nextTwitchReward?.RequiredMinutes ?? 0}, dropMinedSeconds={_twitchDropMinedSeconds}");
                RaiseTwitchDropChangedIfNeeded(nextTwitchReward);

                int twitchMinuteBucket = _twitchMinedSeconds / 60;
                if (twitchMinuteBucket > _twitchAppliedMinuteBucket)
                {
                    int minutesToApply = twitchMinuteBucket - _twitchAppliedMinuteBucket;
                    _twitchAppliedMinuteBucket = twitchMinuteBucket;
                    ApplyMinuteProgressToActiveCampaign(Platform.Twitch, currentTwitchCampaign.Id, minutesToApply);
                }

                byte twitchCampPct = CalculateLiveCampaignProgress(currentTwitchCampaign);
                byte twitchDropPct = CalculateLiveDropProgress(currentTwitchCampaign, _twitchDropMinedSeconds);
                VerboseLog("LiveProgress", $"Twitch tick campaignId={currentTwitchCampaign.Id}, campaignMinedSeconds={_twitchMinedSeconds}, dropMinedSeconds={_twitchDropMinedSeconds}, campaignPct={twitchCampPct}, dropPct={twitchDropPct}");
                TwitchProgressChanged?.Invoke(twitchCampPct, twitchDropPct);
            }

            DropsCampaign? currentKickCampaign = _currentKickCampaign;
            if (currentKickCampaign != null)
            {
                _kickMinedSeconds++;
                _kickDropMinedSeconds++;

                DropsReward? nextKickReward = currentKickCampaign.Rewards
                    .Where(r => !r.IsClaimed)
                    .OrderBy(r => r.RequiredMinutes)
                    .FirstOrDefault();

                VerboseLog("DropPointer", $"Kick nextReward={nextKickReward?.Name ?? "none"}, nextRewardId={nextKickReward?.Id ?? "none"}, requiredMinutes={nextKickReward?.RequiredMinutes ?? 0}, dropMinedSeconds={_kickDropMinedSeconds}");
                RaiseKickDropChangedIfNeeded(nextKickReward);

                int kickMinuteBucket = _kickMinedSeconds / 60;
                if (kickMinuteBucket > _kickAppliedMinuteBucket)
                {
                    int minutesToApply = kickMinuteBucket - _kickAppliedMinuteBucket;
                    _kickAppliedMinuteBucket = kickMinuteBucket;
                    ApplyMinuteProgressToActiveCampaign(Platform.Kick, currentKickCampaign.Id, minutesToApply);
                }

                byte kickCampPct = CalculateLiveCampaignProgress(currentKickCampaign);
                byte kickDropPct = CalculateLiveDropProgress(currentKickCampaign, _kickDropMinedSeconds);
                VerboseLog("LiveProgress", $"Kick tick campaignId={currentKickCampaign.Id}, campaignMinedSeconds={_kickMinedSeconds}, dropMinedSeconds={_kickDropMinedSeconds}, campaignPct={kickCampPct}, dropPct={kickDropPct}");
                KickProgressChanged?.Invoke(kickCampPct, kickDropPct);
            }
        }
        /// <summary>
        /// Initializes the Twitch and Kick web views using the specified hosts.
        /// </summary>
        /// <param name="twitch">The host instance to associate with the Twitch web view. Cannot be null.</param>
        /// <param name="kick">The host instance to associate with the Kick web view. Cannot be null.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="twitch"/> or <paramref name="kick"/> is null.</exception>
        public void InitializeWebViews(IWebViewHost twitch, IWebViewHost kick)
        {
            TwitchWebView = twitch ?? throw new ArgumentNullException(nameof(twitch));
            KickWebView = kick ?? throw new ArgumentNullException(nameof(kick));
        }
        /// <summary>
        /// Updates the list of active campaigns based on the specified collection.
        /// </summary>
        /// <remarks>This method clears the current active campaigns and repopulates the list with
        /// eligible campaigns from the provided collection. The update is performed on the application's UI thread.
        /// After updating, the method initiates stream mining for the active campaigns.</remarks>
        /// <param name="campaigns">A collection of <see cref="DropsCampaign"/> objects to evaluate and update as active campaigns. Only
        /// campaigns that have progress to make, have started, and have not yet ended are considered.</param>
        /// <param name="twitchGqlService">The Twitch GraphQL service used for Twitch-specific mining operations, or null if unavailable.</param>
        /// <param name="startMining">true to begin or refresh stream mining after updating campaigns; otherwise, false.</param>
        public void UpdateCampaigns(IEnumerable<DropsCampaign> campaigns, IGqlService? twitchGqlService, bool startMining = true)
        {
            _twitchGqlService = twitchGqlService;
            List<DropsCampaign> allCampaigns = campaigns.ToList();

            lock (_campaignSnapshotSync)
            {
                _lastKnownCampaigns = [.. allCampaigns];
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                UISettingsManager.Instance.UpdateAvailableGameFilterOptions(allCampaigns);

                List<DropsCampaign> filteredCampaigns = allCampaigns
                    .Where(c => UISettingsManager.Instance.IsCampaignAllowedByWhitelist(c))
                    .ToList();

                // Materialize before iterating to avoid deferred execution issues
                List<DropsCampaign> activeCampaignsList = filteredCampaigns
                    .Where(c => c.StartsAt <= DateTimeOffset.Now && c.EndsAt > DateTimeOffset.Now)
                    .OrderBy(x => x.Platform)
                    .ThenBy(x => x.GameName)
                    .ToList();

                ActiveCampaigns.Clear();
                foreach (DropsCampaign? c in activeCampaignsList)
                {
                    ActiveCampaigns.Add(c);
                }

                UpdateCurrentSelectionFlags();
            });

            if (startMining && !_isPaused)
                _ = StartMiningStreams(); // Fire and forget - will handle its own loop
        }
        /// <summary>
        /// Temporarily pauses stream mining and waits for any active mine cycle to exit.
        /// </summary>
        public async Task PauseMiningAsync()
        {
            _isPaused = true;
            _startMiningCts?.Cancel();

            _recheckTimer?.Stop();
            _streamHealthTimer?.Stop();
            _liveProgressTimer.Stop();

            await _startMiningLock.WaitAsync();
            _startMiningLock.Release();
        }
        /// <summary>
        /// Resumes stream mining if it was previously paused.
        /// </summary>
        public async Task ResumeMiningAsync()
        {
            if (!_isPaused)
                return;

            _isPaused = false;
            await StartMiningStreams();
        }
        /// <summary>
        /// Calculates the overall progress percentage for a campaign using a hybrid approach:
        /// - Full credit for the required time of all claimed rewards
        /// - Plus progress from current mined time toward the remaining unclaimed rewards
        /// - Divided by the total required time across ALL rewards in the campaign.
        /// This gives a more "completionist" view of how much of the entire event is effectively done.
        /// </summary>
        /// <param name="campaign">The campaign containing the rewards for which progress is being calculated. Cannot be null.</param>
        /// <param name="totalMinedSeconds">The total number of seconds mined by the user toward earning drops. Must be greater than or
        /// equal to 0.</param>
        /// <returns>A value between 0 and 100 representing the percentage of overall campaign completion.
        /// Returns 100 if all rewards are already claimed or if total required time is zero.</returns>
        private static byte CalculateLiveCampaignProgress(DropsCampaign? campaign)
        {
            if (campaign == null)
                return 0;

            // Total required minutes across ALL rewards (claimed + unclaimed)
            int totalRequiredMinutes = campaign.Rewards.Sum(r => r.RequiredMinutes);

            if (totalRequiredMinutes == 0)
                return 100; // No requirements → done


            int effectiveMinutes = campaign.Rewards.Sum(r => Math.Min(r.ProgressMinutes, r.RequiredMinutes));

            double percentage = (double)effectiveMinutes / totalRequiredMinutes * 100;
            return (byte)Math.Clamp((int)Math.Floor(percentage), 0, 100);

        }
        /// <summary>
        /// Calculates the progress percentage toward the next unclaimed live drop reward in the specified campaign.
        /// </summary>
        /// <param name="campaign">The drops campaign containing the list of rewards and their claim status.</param>
        /// <param name="totalMinedSeconds">The total number of seconds the user has mined, used to determine progress toward the next reward.</param>
        /// <returns>A value between 0 and 100 representing the percentage of progress toward the next unclaimed reward. Returns
        /// 100 if all rewards have been claimed.</returns>
        private static byte CalculateLiveDropProgress(DropsCampaign? campaign, int totalMinedSeconds)
        {
            if (campaign == null)
                return 0;

            // Find the next unclaimed reward
            List<DropsReward> unclaimedRewards = [.. campaign.Rewards.Where(r => !r.IsClaimed)];
            DropsReward? nextReward = unclaimedRewards
                .Where(r => !r.IsClaimed)
                .OrderBy(r => r.RequiredMinutes)
                .FirstOrDefault();

            if (nextReward == null)
            {
                VerboseLog("RewardProgress", $"campaignId={campaign.Id}, no next unclaimed reward found; returning 0.");
                return 0; // Nothing to claim
            }

            int requiredSeconds = nextReward.RequiredMinutes * 60;

            int effectiveProgressSeconds = Math.Clamp(totalMinedSeconds, 0, requiredSeconds);
            double percentage = (double)effectiveProgressSeconds / requiredSeconds * 100;
            byte result = (byte)Math.Clamp((int)Math.Floor(percentage), 0, 100);

            VerboseLog(
                "RewardProgress",
                $"campaignId={campaign.Id}, campaignName='{campaign.Name}', rewardsUnclaimed={unclaimedRewards.Count}, nextRewardId={nextReward.Id}, nextRewardName='{nextReward.Name}', requiredSeconds={requiredSeconds}, totalMinedSeconds={totalMinedSeconds}, effectiveProgressSeconds={effectiveProgressSeconds}, computedPct={result}");

            return result;
        }
        /// <summary>
        /// Initiates monitoring of active campaign streams to progress eligible rewards on supported platforms.
        /// </summary>
        /// <remarks>This method evaluates all active campaigns and begins mining streams on platforms
        /// such as Twitch and Kick if progress can be made. It periodically re-evaluates which streams to mine based
        /// on reward progress and campaign status. If no campaigns are eligible for progress, stream monitoring is
        /// stopped. The method is safe to call repeatedly; any previous monitoring timers are stopped and disposed
        /// before starting new ones.</remarks>
        /// <param name="restartedInternally">true when mining is being restarted by an internal state change rather than an external refresh.</param>
        /// <returns>A task that represents the asynchronous operation of starting and managing stream monitoring.</returns>
        public async Task StartMiningStreams(bool restartedInternally = false)
        {
            await _startMiningLock.WaitAsync();
            try
            {
                VerboseLog("StartMining",
                    $"ENTERING StartMiningStreams | restarted={restartedInternally} | " +
                    $"paused={_isPaused} | activeCampaigns={ActiveCampaigns.Count} | " +
                    $"twitchCurrent={_currentTwitchCampaign?.Id ?? "null"} | " +
                    $"kickCurrent={_currentKickCampaign?.Id ?? "null"} | " +
                    $"twitchSeconds={_twitchMinedSeconds} | twitchApplied={_twitchAppliedMinuteBucket}");

                if (_isPaused)
                    return;

                _startMiningCts?.Cancel();
                _startMiningCts = new CancellationTokenSource();
                CancellationToken token = _startMiningCts.Token;

                // Immediately stop the live progress timer to prevent ticks during unstable state
                _liveProgressTimer?.Stop();
                List<DropsCampaign> campaignSnapshot = Application.Current.Dispatcher.Invoke(() => ActiveCampaigns.ToList());

                // Reset current selections and progress
                TwitchChannelChanged?.Invoke(string.Empty);
                TwitchCampaignChanged?.Invoke(string.Empty, null);
                _lastTwitchDropId = null;
                TwitchDropChanged?.Invoke(string.Empty, null);
                TwitchProgressChanged?.Invoke(0, 0);
                KickChannelChanged?.Invoke(string.Empty);
                KickCampaignChanged?.Invoke(string.Empty, null);
                _lastKickDropId = null;
                KickDropChanged?.Invoke(string.Empty, null);
                KickProgressChanged?.Invoke(0, 0);
                _twitchAppliedMinuteBucket = _twitchMinedSeconds / 60;
                _kickAppliedMinuteBucket = _kickMinedSeconds / 60;

                VerboseLog("StartMining", $"AFTER reset | twitchApplied={_twitchAppliedMinuteBucket} | kickApplied={_kickAppliedMinuteBucket}");

                AppLogger.Debug("Miner", "[DropsInventoryManager] Starting stream mining process...");
                AppLogger.Info("Miner", $"StartMiningStreams invoked. restartedInternally={restartedInternally}, activeCampaigns={ActiveCampaigns.Count}, paused={_isPaused}");

                if (!restartedInternally)
                    MinerStatusChanged?.Invoke("Starting");
                else
                    MinerStatusChanged?.Invoke("Evaluating");

                // Stop any existing timer
                _recheckTimer?.Stop();
                _streamHealthTimer?.Stop();
                _recheckTimer?.Dispose();
                _streamHealthTimer?.Dispose();
                _recheckTimer = null;
                _streamHealthTimer = null;

                if (!campaignSnapshot.Any())
                {
                    AppLogger.Debug("Miner", "[DropsInventoryManager] No active campaigns with progress to make. Stopping stream mining.");
                    AppLogger.Info("Miner", "No active campaigns found during start; switching to Idle.");
                    MinerStatusChanged?.Invoke("Idle");
                    _currentTwitchCampaign = null;
                    _currentKickCampaign = null;
                    UpdateCurrentSelectionFlags();
                    return;
                }

                if (token.IsCancellationRequested)
                    return;

                DateTime nextCheckAt = DateTime.Now.AddHours(1); // Fallback: recheck in 1 hour

                // Get a list of ready to claim rewards
                List<DropsReward> readyToClaimRewards = [.. campaignSnapshot.SelectMany(c => c.Rewards.Where(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes))];

                if (UISettingsManager.Instance.AutoClaimRewards)
                {
                    foreach (DropsReward item in readyToClaimRewards)
                    {
                        DropsCampaign? parentCampaign = campaignSnapshot.FirstOrDefault(c => c.Rewards.Contains(item));
                        if (parentCampaign == null)
                            continue;

                        bool claimResult = false;
                        if (parentCampaign.Platform == Platform.Twitch && _twitchGqlService != null)
                            claimResult = await _twitchGqlService.ClaimDropAsync(parentCampaign.Id, item.Id);
                        else if (parentCampaign.Platform == Platform.Kick)
                            claimResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.ClaimKickDropAsync(parentCampaign.Id, item.Id));

                        if (claimResult)
                        {
                            bool inventoryUpdated = MarkRewardClaimedInActiveCampaigns(parentCampaign.Id, item.Id);
                            if (!inventoryUpdated)
                            {
                                AppLogger.Warn("Miner", $"Failed to apply immediate claimed-state update. campaignId={parentCampaign.Id}, rewardId={item.Id}");
                            }
                            else
                            {
                                AppLogger.Info("Miner", $"Applied immediate claimed-state update. campaignId={parentCampaign.Id}, rewardId={item.Id}");
                            }

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

                List<DropsCampaign> snapshot = campaignSnapshot;
                List<DropsCampaign> readyToClaimOnlyCampaigns = snapshot
                    .Where(c => c.HasReadyToClaimRewards() && !c.HasProgressToMake())
                    .ToList();

                if (!snapshot.Any(c => c.HasProgressToMake()))
                {
                    if (readyToClaimOnlyCampaigns.Any())
                    {
                        AppLogger.Info("Miner", $"No remaining mine progress remains; {readyToClaimOnlyCampaigns.Count} campaign(s) are waiting for manual claim.");
                    }

                    AppLogger.Debug("Miner", "[DropsInventoryManager] No campaigns with progress to make after claim. Stopping stream mining.");
                    AppLogger.Info("Miner", "No campaigns with progress after claim pass; switching to Idle.");
                    MinerStatusChanged?.Invoke("Idle");
                    _currentTwitchCampaign = null;
                    _currentKickCampaign = null;
                    UpdateCurrentSelectionFlags();
                    return;
                }

                if (token.IsCancellationRequested)
                    return;

                // Group campaigns by platform
                List<DropsCampaign> twitchCampaigns = snapshot.Where(c => c.Platform == Platform.Twitch && c.HasProgressToMake()).ToList();
                List<DropsCampaign> kickCampaigns = snapshot.Where(c => c.Platform == Platform.Kick && c.HasProgressToMake()).ToList();

                // ----------------------------------------------------------------
                // Twitch handling
                // ----------------------------------------------------------------
                if (twitchCampaigns.Count != 0 && TwitchWebView != null)
                {
                    if (token.IsCancellationRequested)
                        return;

                    List<DropsCampaign> remainingTwitchCampaigns = [.. twitchCampaigns];
                    while (remainingTwitchCampaigns.Count != 0)
                    {
                        DropsCampaign? bestTwitch = await SelectBestCampaign(remainingTwitchCampaigns);
                        if (bestTwitch == null)
                            break;

                        if (token.IsCancellationRequested)
                            return;

                        string twitchUrl = await SelectTwitchStreamerForCampaign(bestTwitch);
                        if (token.IsCancellationRequested)
                            return;

                        if (string.IsNullOrWhiteSpace(twitchUrl))
                        {
                            AppLogger.Warn("TwitchSelection", $"Twitch campaign '{bestTwitch.Name}' produced empty streamer URL; trying next candidate.");
                            remainingTwitchCampaigns.Remove(bestTwitch);
                            continue;
                        }

                        await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView!.NavigateAsync(twitchUrl));
                        await Task.Delay(1500);
                        await DismissTwitchMatureContentGateAsync();
                        await SetTwitchStreamToLowestQualityAsync();
                        await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView!.ForceRefreshAsync());
                        await Task.Delay(5000);

                        _currentTwitchCampaign = bestTwitch;

                        // Prefer the authoritative GQL check (live + correct category) over fragile DOM
                        // scraping; only fall back to DOM when GQL cannot determine the result.
                        string twitchLogin = GetStreamerNameFromUrl(twitchUrl);
                        bool? gqlEligible = await IsTwitchStreamEligibleViaGqlAsync(twitchLogin, bestTwitch.Slug);

                        bool twitchOnline;
                        bool twitchCorrectCategory;
                        if (gqlEligible.HasValue)
                        {
                            twitchOnline = gqlEligible.Value;
                            twitchCorrectCategory = gqlEligible.Value;
                        }
                        else
                        {
                            twitchOnline = await IsTwitchStreamOnline();
                            twitchCorrectCategory = await IsTwitchStreamCategoryCorrect();
                        }

                        if (!twitchOnline || !twitchCorrectCategory)
                        {
                            AppLogger.Warn("TwitchSelection", $"Twitch campaign '{bestTwitch.Name}' failed streamer eligibility. online={twitchOnline}, categoryOk={twitchCorrectCategory}");
                            _currentTwitchCampaign = null;
                            _currentTwitchLogin = null;
                            UpdateCurrentSelectionFlags();
                            remainingTwitchCampaigns.Remove(bestTwitch);
                            continue;
                        }

                        _currentTwitchLogin = twitchLogin;
                        _lastKnownTwitchOnlineState = true;
                        UpdateCurrentSelectionFlags();

                        // Sync baseline NOW - right after selection, before any further logic
                        _twitchMinedSeconds = bestTwitch.Rewards
                            .Sum(r => Math.Min(r.ProgressMinutes, r.RequiredMinutes) * 60);

                        DropsReward? nextTwitchReward = bestTwitch.Rewards
                            .Where(r => !r.IsClaimed)
                            .OrderBy(r => r.RequiredMinutes)
                            .FirstOrDefault();

                        int twitchMinutesBeforeNextReward = bestTwitch.Rewards
                            .Where(r => !r.IsClaimed && r.RequiredMinutes < nextTwitchReward!.RequiredMinutes)
                            .Sum(r => r.RequiredMinutes);
                        _twitchDropMinedSeconds = Math.Max(0, (nextTwitchReward?.ProgressMinutes ?? 0) - twitchMinutesBeforeNextReward) * 60;

                        _twitchAppliedMinuteBucket = _twitchMinedSeconds / 60;

                        VerboseLog("SelectionBaseline",
                            $"Twitch baseline SET | " +
                            $"campaignId={bestTwitch.Id} | " +
                            $"minedSeconds={_twitchMinedSeconds} | " +
                            $"dropMinedSeconds={_twitchDropMinedSeconds} | " +
                            $"appliedBucket={_twitchAppliedMinuteBucket}");

                        VerboseLog("SelectionBaseline", $"Twitch campaignId={bestTwitch.Id}, campaignMinedSecondsBaseline={_twitchMinedSeconds}, dropMinedSecondsBaseline={_twitchDropMinedSeconds}, nextRewardId={nextTwitchReward?.Id ?? "none"}, unclaimedRewards={bestTwitch.Rewards.Count(r => !r.IsClaimed)}");

                        byte initialTwitchPct = CalculateLiveCampaignProgress(bestTwitch);
                        byte initialTwitchDropPct = CalculateLiveDropProgress(bestTwitch, _twitchDropMinedSeconds);
                        TwitchProgressChanged?.Invoke(initialTwitchPct, initialTwitchDropPct);
                        RaiseTwitchDropChangedIfNeeded(nextTwitchReward);

                        AppLogger.Debug("TwitchSelection", $"[DropsInventoryManager] Mining Twitch stream: {twitchUrl}");
                        AppLogger.Info("TwitchSelection", $"Selected Twitch stream '{twitchUrl}' for campaign '{bestTwitch.Name}' ({bestTwitch.Id}).");
                        RememberLastStreamerUrl(Platform.Twitch, bestTwitch.Slug, twitchUrl);

                        DropsReward? soonestTwitch = bestTwitch.Rewards
                            .Where(r => !r.IsClaimed && r.ProgressMinutes < r.RequiredMinutes)
                            .OrderBy(r => r.RequiredMinutes - r.ProgressMinutes)
                            .FirstOrDefault();

                        if (soonestTwitch != null)
                        {
                            DateTime est = DateTime.Now.AddMinutes(soonestTwitch.RequiredMinutes - soonestTwitch.ProgressMinutes);
                            if (est < nextCheckAt)
                                nextCheckAt = est;
                        }

                        break;
                    }

                    if (_currentTwitchCampaign == null)
                    {
                        AppLogger.Warn("TwitchSelection", $"No Twitch campaign passed eligibility checks. candidates={twitchCampaigns.Count}");
                    }
                }

                // ----------------------------------------------------------------
                // Kick handling
                // ----------------------------------------------------------------
                if (kickCampaigns.Count != 0 && KickWebView != null)
                {
                    if (token.IsCancellationRequested)
                        return;

                    List<DropsCampaign> remainingKickCampaigns = [.. kickCampaigns];
                    while (remainingKickCampaigns.Count != 0)
                    {
                        DropsCampaign? bestKick = await SelectBestCampaign(remainingKickCampaigns);
                        if (bestKick == null)
                            break;

                        if (token.IsCancellationRequested)
                            return;

                        string kickUrl = await SelectKickStreamerForCampaign(bestKick);
                        if (token.IsCancellationRequested)
                            return;

                        if (string.IsNullOrWhiteSpace(kickUrl))
                        {
                            AppLogger.Warn("KickSelection", $"Kick campaign '{bestKick.Name}' produced empty streamer URL; trying next candidate.");
                            remainingKickCampaigns.Remove(bestKick);
                            continue;
                        }

                        await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.NavigateAsync(kickUrl));
                        await Task.Delay(1500);
                        await DismissKickMatureContentGateAsync();
                        await SetKickStreamToLowestQualityAsync();
                        await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.ForceRefreshAsync());
                        await Task.Delay(5000);

                        _currentKickCampaign = bestKick;
                        bool kickOnline = await IsKickStreamOnline();
                        bool kickCorrectCategory = await IsKickStreamCategoryCorrect();

                        if (!kickOnline || !kickCorrectCategory)
                        {
                            AppLogger.Warn("KickSelection", $"Kick campaign '{bestKick.Name}' failed streamer eligibility. online={kickOnline}, categoryOk={kickCorrectCategory}");
                            _currentKickCampaign = null;
                            UpdateCurrentSelectionFlags();
                            remainingKickCampaigns.Remove(bestKick);
                            continue;
                        }

                        _lastKnownKickOnlineState = true;
                        UpdateCurrentSelectionFlags();

                        _kickMinedSeconds = bestKick.Rewards
                            .Sum(r => Math.Min(r.ProgressMinutes, r.RequiredMinutes) * 60);

                        DropsReward? nextKickReward = bestKick.Rewards
                            .Where(r => !r.IsClaimed)
                            .OrderBy(r => r.RequiredMinutes)
                            .FirstOrDefault();

                        int kickMinutesBeforeNextReward = bestKick.Rewards
                            .Where(r => !r.IsClaimed && r.RequiredMinutes < nextKickReward!.RequiredMinutes)
                            .Sum(r => r.RequiredMinutes);
                        _kickDropMinedSeconds = Math.Max(0, (nextKickReward?.ProgressMinutes ?? 0) - kickMinutesBeforeNextReward) * 60;

                        _kickAppliedMinuteBucket = _kickMinedSeconds / 60;

                        VerboseLog("SelectionBaseline", $"Kick campaignId={bestKick.Id}, campaignMinedSecondsBaseline={_kickMinedSeconds}, dropMinedSecondsBaseline={_kickDropMinedSeconds}, nextRewardId={nextKickReward?.Id ?? "none"}, unclaimedRewards={bestKick.Rewards.Count(r => !r.IsClaimed)}");

                        byte initialKickPct = CalculateLiveCampaignProgress(bestKick);
                        byte initialKickDropPct = CalculateLiveDropProgress(bestKick, _kickDropMinedSeconds);
                        KickProgressChanged?.Invoke(initialKickPct, initialKickDropPct);
                        RaiseKickDropChangedIfNeeded(nextKickReward);

                        AppLogger.Debug("KickSelection", $"[DropsInventoryManager] Mining Kick stream: {kickUrl}");
                        AppLogger.Info("KickSelection", $"Selected Kick stream '{kickUrl}' for campaign '{bestKick.Name}' ({bestKick.Id}).");
                        RememberLastStreamerUrl(Platform.Kick, bestKick.Slug, kickUrl);

                        DropsReward? soonestKick = bestKick.Rewards
                            .Where(r => !r.IsClaimed && r.ProgressMinutes < r.RequiredMinutes)
                            .OrderBy(r => (r.RequiredMinutes - r.ProgressMinutes))
                            .FirstOrDefault();

                        if (soonestKick != null)
                        {
                            DateTime est = DateTime.Now.AddMinutes(soonestKick.RequiredMinutes - soonestKick.ProgressMinutes);
                            if (est < nextCheckAt)
                                nextCheckAt = est;
                        }

                        break;
                    }

                    if (_currentKickCampaign == null)
                    {
                        AppLogger.Warn("KickSelection", $"No Kick campaign passed eligibility checks. candidates={kickCampaigns.Count}");
                    }
                }

                if (_currentTwitchCampaign == null && _currentKickCampaign == null)
                {
                    AppLogger.Warn("Miner", "No stream selected after evaluation cycle; status may oscillate with health checks.");
                }

                // Start periodic health check
                StartStreamHealthMonitoring();

                // ONLY NOW restart the live progress timer - state is consistent
                _liveProgressTimer?.Start();

                // Set timer to re-evaluate when the next reward is expected to complete (or fallback)
                double delayMs = Math.Max((nextCheckAt - DateTime.Now).TotalMilliseconds, 60000);
                _recheckTimer = new System.Timers.Timer(delayMs);
                _recheckTimer.Elapsed += async (s, e) =>
                {
                    _recheckTimer?.Stop();
                    AppLogger.Debug("Miner", "[DropsInventoryManager] Re-evaluating streams for active campaigns.");
                    AppLogger.Info("Miner", "Scheduled re-evaluation triggered.");
                    await StartMiningStreams(true);
                };
                _recheckTimer.AutoReset = false;
                _recheckTimer.Start();

                AppLogger.Debug("Miner", $"[DropsInventoryManager] Next stream re-evaluation in ~{delayMs / 60000:F1} minutes at {nextCheckAt:u}");
                AppLogger.Info("Miner", $"Next re-evaluation in {delayMs / 1000:F0}s at {nextCheckAt:u}. twitchSelected={_currentTwitchCampaign != null}, kickSelected={_currentKickCampaign != null}");

                MinerStatusChanged?.Invoke(_currentTwitchCampaign != null || _currentKickCampaign != null ? "Mining" : "Idle");
            }
            finally
            {
                _startMiningLock.Release();
            }
        }
        /// <summary>
        /// Marks the specified reward as claimed in the active campaign with the given campaign identifier.
        /// </summary>
        /// <remarks>If the specified campaign or reward is not found in the active campaigns, no changes
        /// are made and the method returns false. The method updates the claimed status and progress of the reward, and
        /// synchronizes related campaign selections.</remarks>
        /// <param name="campaignId">The identifier of the campaign in which to mark the reward as claimed. Cannot be null or empty.</param>
        /// <param name="rewardId">The identifier of the reward to mark as claimed. Cannot be null or empty.</param>
        /// <returns>true if the reward was successfully marked as claimed; otherwise, false.</returns>
        private bool MarkRewardClaimedInActiveCampaigns(string campaignId, string rewardId)
        {
            bool updated = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                DropsCampaign? existingCampaign = ActiveCampaigns.FirstOrDefault(c => c.Id == campaignId);
                if (existingCampaign == null)
                    return;

                int campaignIndex = ActiveCampaigns.IndexOf(existingCampaign);
                if (campaignIndex < 0)
                    return;

                bool rewardFound = false;
                List<DropsReward> updatedRewards = new List<DropsReward>(existingCampaign.Rewards.Count);
                foreach (DropsReward reward in existingCampaign.Rewards)
                {
                    if (reward.Id == rewardId)
                    {
                        rewardFound = true;
                        updatedRewards.Add(reward with
                        {
                            IsClaimed = true,
                            ProgressMinutes = Math.Max(reward.ProgressMinutes, reward.RequiredMinutes)
                        });
                    }
                    else
                    {
                        updatedRewards.Add(reward);
                    }
                }

                if (!rewardFound)
                    return;

                DropsCampaign updatedCampaign = existingCampaign with { Rewards = updatedRewards };
                ActiveCampaigns[campaignIndex] = updatedCampaign;

                if (_currentTwitchCampaign?.Id == campaignId)
                    _currentTwitchCampaign = updatedCampaign;

                if (_currentKickCampaign?.Id == campaignId)
                    _currentKickCampaign = updatedCampaign;

                UpdateCurrentSelectionFlags();
                updated = true;
            });

            return updated;
        }
        /// <summary>
        /// Updates the selection flags for active campaigns and their rewards to reflect the current campaign and
        /// reward based on the active platform and progress.
        /// </summary>
        /// <remarks>This method must be called on the UI thread, as it updates observable collections
        /// bound to the user interface. It ensures that only one campaign and one reward per platform are marked as
        /// current at any time. If there are no active campaigns, the method exits without making changes.</remarks>
        private void UpdateCurrentSelectionFlags()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ActiveCampaigns.Count == 0)
                    return;

                // Materialize ActiveCampaigns to avoid "collection modified" during updates
                List<DropsCampaign> updatedCampaigns = new List<DropsCampaign>(ActiveCampaigns.Count);

                foreach (DropsCampaign campaign in ActiveCampaigns.ToList())
                {
                    bool isCurrentCampaign = (campaign.Platform == Platform.Twitch && campaign.Id == _currentTwitchCampaign?.Id) ||
                                             (campaign.Platform == Platform.Kick && campaign.Id == _currentKickCampaign?.Id);

                    DropsReward? currentReward = null;
                    if (isCurrentCampaign)
                    {
                        currentReward = campaign.Rewards
                            .Where(r => !r.IsClaimed)
                            .OrderBy(r => Math.Max(0, r.RequiredMinutes - r.ProgressMinutes))
                            .FirstOrDefault();
                    }

                    List<DropsReward> updatedRewards = new List<DropsReward>(campaign.Rewards.Count);
                    foreach (DropsReward reward in campaign.Rewards)
                    {
                        bool isCurrentReward = isCurrentCampaign && currentReward != null && reward.Id == currentReward.Id;
                        updatedRewards.Add(reward with { IsCurrentReward = isCurrentReward });
                    }

                    updatedCampaigns.Add(campaign with
                    {
                        IsCurrentCampaign = isCurrentCampaign,
                        Rewards = updatedRewards
                    });
                }

                ActiveCampaigns.Clear();
                foreach (DropsCampaign? c in updatedCampaigns.OrderBy(x => x.Platform).ThenBy(x => x.GameName))
                {
                    ActiveCampaigns.Add(c);
                }
            });
        }
        /// <summary>
        /// Begins periodic monitoring of the health status of the Twitch and Kick streams, triggering a re-evaluation
        /// if either stream is detected as unhealthy.
        /// </summary>
        /// <remarks>This method sets up a timer to check the online status of both streams every 30
        /// seconds. If either stream is offline, in the wrong category, or Twitch is showing an ad, monitoring is
        /// temporarily stopped and an immediate re-selection of streams is initiated. This helps ensure that the
        /// application responds promptly to changes in stream availability.</remarks>
        private void StartStreamHealthMonitoring()
        {
            _streamHealthTimer = new System.Timers.Timer(30 * 1000); // Every 30 seconds
            _streamHealthTimer.Elapsed += async (s, e) =>
            {
                // Run the entire check on the UI thread
                await await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    bool twitchOnline;
                    bool twitchCorrectCategory;
                    if (_currentTwitchCampaign == null)
                    {
                        twitchOnline = false;
                        twitchCorrectCategory = false;
                    }
                    else
                    {
                        // Prefer authoritative GQL (live + category); fall back to DOM if unavailable.
                        bool? gqlEligible = await IsTwitchStreamEligibleViaGqlAsync(_currentTwitchLogin, _currentTwitchCampaign.Slug);
                        if (gqlEligible.HasValue)
                        {
                            twitchOnline = gqlEligible.Value;
                            twitchCorrectCategory = gqlEligible.Value;
                        }
                        else
                        {
                            twitchOnline = await IsTwitchStreamOnline();
                            twitchCorrectCategory = await IsTwitchStreamCategoryCorrect();
                        }
                    }
                    bool twitchShowingAd = _currentTwitchCampaign != null && await IsTwitchShowingAd();
                    bool kickOnline = _currentKickCampaign != null && await IsKickStreamOnline();
                    bool kickCorrectCategory = _currentKickCampaign != null && await IsKickStreamCategoryCorrect();

                    AppLogger.Debug("HealthCheck", $"Twitch: {(twitchOnline ? "ONLINE" : "OFFLINE")} | Kick: {(kickOnline ? "ONLINE" : "OFFLINE")}");
                    AppLogger.Debug("HealthCheck", $"Twitch category correct: {twitchCorrectCategory} | Kick category correct: {kickCorrectCategory} | Twitch showing ad: {twitchShowingAd}");
                    AppLogger.Info("HealthCheck", $"Twitch online={twitchOnline}, categoryOk={twitchCorrectCategory}, showingAd={twitchShowingAd}; Kick online={kickOnline}, categoryOk={kickCorrectCategory}");

                    // Group campaigns by platform
                    List<DropsCampaign> twitchCampaigns = [.. ActiveCampaigns.Where(c => c.Platform == Platform.Twitch && c.HasProgressToMake())];
                    List<DropsCampaign> kickCampaigns = [.. ActiveCampaigns.Where(c => c.Platform == Platform.Kick && c.HasProgressToMake())];

                    bool twitchNeedsReevaluation = twitchCampaigns.Count != 0 && (!twitchOnline || !twitchCorrectCategory || twitchShowingAd) && _lastKnownTwitchOnlineState;
                    bool kickNeedsReevaluation = kickCampaigns.Count != 0 && (!kickOnline || !kickCorrectCategory) && _lastKnownKickOnlineState;

                    if (twitchNeedsReevaluation || kickNeedsReevaluation)
                    {
                        if (twitchShowingAd && !string.IsNullOrWhiteSpace(_currentTwitchCampaign?.Slug))
                        {
                            ForgetLastStreamerUrl(Platform.Twitch, _currentTwitchCampaign!.Slug);
                            AppLogger.Warn("HealthCheck", $"Twitch ad detected for campaign '{_currentTwitchCampaign.Name}'. Forgetting remembered streamer to force a switch.");
                        }

                        if (!twitchOnline)
                            _lastKnownTwitchOnlineState = false;

                        if (!kickOnline)
                            _lastKnownKickOnlineState = false;

                        AppLogger.Debug("HealthCheck", "Stream unhealthy -> forcing re-evaluation");
                        AppLogger.Warn("HealthCheck", $"Forcing re-evaluation. twitchOnline={twitchOnline}, twitchCategoryOk={twitchCorrectCategory}, twitchAd={twitchShowingAd}, kickOnline={kickOnline}, kickCategoryOk={kickCorrectCategory}");
                        _streamHealthTimer?.Stop();
                        await StartMiningStreams(true); // This will restart everything safely
                    }
                });
            };

            _streamHealthTimer.AutoReset = true;
            _streamHealthTimer.Start();
        }
        /// <summary>
        /// Selects the most optimal campaign from the provided list based on completion percentage and proximity to the
        /// next unclaimed reward.
        /// </summary>
        /// <remarks>This method prioritizes campaigns that are furthest along in completion. If there is
        /// a tie, it selects the campaign that requires the least additional time to claim its next reward. The method
        /// assumes that the input list contains at least one campaign; otherwise, an exception may be thrown.</remarks>
        /// <param name="campaigns">A list of available campaigns to evaluate. Cannot be null or empty.</param>
        /// <returns>The campaign that has the highest completion percentage. If multiple campaigns share the highest completion
        /// percentage, the campaign closest to earning its next unclaimed reward is selected.</returns>
        private Task<DropsCampaign?> SelectBestCampaign(List<DropsCampaign> campaigns)
        {
            // Honor manual override from SwitchCampaignCommand
            if (_pinnedCampaignId != null)
            {
                DropsCampaign? pinned = campaigns.FirstOrDefault(c => c.Id == _pinnedCampaignId);

                if (pinned != null)
                {
                    AppLogger.Info("Selection", $"Pinned campaign '{pinned.Name}' selected via manual override.");
                    return Task.FromResult<DropsCampaign?>(pinned);
                }

                // Campaign no longer in candidates - channels offline or all rewards claimed
                AppLogger.Info("Selection", $"Pinned campaign '{_pinnedCampaignId}' is no longer pursuable, releasing pin.");
                _pinnedCampaignId = null;
                SavePinnedCampaignToDisk();
            }

            MiningPriorityMode mode = UISettingsManager.Instance.MiningPriorityMode;
            AppLogger.Debug("Selection", $"Selecting best campaign with mode={mode}, candidates={campaigns.Count}");
            List<DropsCampaign> prioritizedCampaigns = mode switch
            {
                MiningPriorityMode.EndingSoonest => [.. campaigns
                        .OrderBy(c => c.IsGeneralDrop)
                        .ThenBy(c => c.EndsAt)
                        .ThenBy(c => c.Rewards
                            .Where(r => !r.IsClaimed)
                            .Min(r => r.RequiredMinutes - r.ProgressMinutes))],
                MiningPriorityMode.LeastTimeToNextReward => [.. campaigns
                        .OrderBy(c => c.IsGeneralDrop)
                        .ThenBy(c => c.Rewards
                            .Where(r => !r.IsClaimed)
                            .Min(r => r.RequiredMinutes - r.ProgressMinutes))
                        .ThenByDescending(c => c.CompletionPercentage())],
                MiningPriorityMode.HighestCompletion => [.. campaigns
                        .OrderBy(c => c.IsGeneralDrop)
                        .ThenByDescending(c => c.CompletionPercentage())
                        .ThenBy(c => c.EndsAt)
                        .ThenBy(c => c.Rewards
                            .Where(r => !r.IsClaimed)
                            .Min(r => r.RequiredMinutes - r.ProgressMinutes))],
                _ => [.. campaigns
                        .OrderBy(c => c.IsGeneralDrop)
                        .ThenByDescending(c => c.CompletionPercentage())
                        .ThenBy(c => c.Rewards
                            .Where(r => !r.IsClaimed)
                            .Min(r => r.RequiredMinutes - r.ProgressMinutes))],
            };

            DropsCampaign? selected = prioritizedCampaigns.FirstOrDefault();
            if (selected == null)
                AppLogger.Warn("Selection", "No campaign selected after priority sort.");
            else
                AppLogger.Info("Selection", $"Selected campaign '{selected.Name}' ({selected.Id}) with mode={mode}.");

            return Task.FromResult(selected);
        }
        /// <summary>
        /// Attempts to set the Kick stream playback quality to the lowest available option asynchronously.
        /// </summary>
        /// <remarks>This method performs a best-effort attempt to change the stream quality by executing
        /// JavaScript in the KickWebView. If KickWebView is null, the method returns immediately and no action is
        /// taken. Any errors encountered during script execution are silently ignored.</remarks>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task SetKickStreamToLowestQualityAsync()
        {
            if (KickWebView == null)
                return;

            // Open settings -> Quality -> Select lowest available (usually 160p or Audio Only)
            string js = @"
                (() => {
                    sessionStorage.setItem('stream_quality', '160');
                })();
            ";

            try
            {
                string result = await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView.ExecuteScriptAsync(js));
                AppLogger.Debug("KickSelection", "[Kick] Quality set to lowest: 160p 30");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("KickSelection", $"Failed setting Kick quality to lowest. {ex.Message}");
            }
        }
        /// <summary>
        /// Attempts to set the Twitch stream quality to the lowest available option using the embedded web view.
        /// </summary>
        /// <remarks>This method performs a best-effort attempt to change the stream quality by executing
        /// JavaScript in the Twitch web player. If the web view is not available or the required UI elements cannot be
        /// found, the operation is silently ignored. No exceptions are thrown for failures.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task completes when the quality selection attempt has
        /// finished.</returns>
        private async Task SetTwitchStreamToLowestQualityAsync()
        {
            if (TwitchWebView == null) return;

            // Open settings -> Quality -> Select lowest available (usually 160p or Audio Only)
            string js = @"
                (() => {
                    localStorage.setItem('video-quality', '{""default"":""160p30""}');
                })();
            ";

            try
            {
                string result = await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView.ExecuteScriptAsync(js));
                AppLogger.Debug("TwitchSelection", "[Twitch] Quality set to 160p 30");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TwitchSelection", $"Failed setting Twitch quality to lowest. {ex.Message}");
            }
        }
        /// <summary>
        /// Attempts to automatically dismiss the mature content gate overlay in the Kick web view, if present.
        /// </summary>
        /// <remarks>This method performs a scripted click on the mature content confirmation button
        /// within the Kick web view, if the overlay is detected. If the web view is not available or the overlay is not
        /// present, no action is taken. Exceptions during script execution are ignored, as the operation is
        /// non-critical.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task completes when the dismissal attempt has
        /// finished.</returns>
        private async Task DismissKickMatureContentGateAsync()
        {
            if (KickWebView == null)
                return;

            string js = @"
                (() => {
                    const button = document.querySelector('button[data-a-target=""player-overlay-mature-accept""]') ||
                                   document.querySelector('button:has-text(""Continue"")') ||
                                   document.querySelector('button:contains(""Continue"")');
                    if (button) {
                        button.click();
                        return true;
                    }
                    return false;
                })();
            ";

            try
            {
                string result = await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView.ExecuteScriptAsync(js));
                if (result?.Trim('"').Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                    AppLogger.Debug("KickSelection", "[Kick] Auto-accepted mature content gate.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("KickSelection", $"Failed dismissing Kick mature content gate. {ex.Message}");
            }
        }
        /// <summary>
        /// Attempts to automatically dismiss the mature content gate overlay in the Twitch web view by simulating a
        /// user acceptance action.
        /// </summary>
        /// <remarks>This method performs a script injection into the Twitch web view to locate and click
        /// the acceptance button for mature content. If the web view is not available or the gate is not present, no
        /// action is taken. The method is silent on failure and does not throw exceptions for script errors.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task completes when the attempt to dismiss the mature
        /// content gate has finished.</returns>
        private async Task DismissTwitchMatureContentGateAsync()
        {
            if (TwitchWebView == null) return;

            string js = @"
                (() => {
                    const button = document.querySelector('button[data-a-target=""content-classification-gate-overlay-start-watching-button""]');

                    if (button) {
                        button.click();
                        return true;
                    }

                    return false;
                })();
            ";

            try
            {
                string result = await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView.ExecuteScriptAsync(js));
                if (result?.Trim('"').Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                    AppLogger.Debug("TwitchSelection", "[Twitch] Auto-accepted mature content gate.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TwitchSelection", $"Failed dismissing Twitch mature content gate. {ex.Message}");
            }
        }
        /// <summary>
        /// Determines whether the Kick stream is currently online by evaluating the presence of a 'LIVE' indicator in
        /// the web view.
        /// </summary>
        /// <remarks>This method relies on the KickWebView instance to execute a script that checks for a
        /// 'LIVE' label in the page content. If KickWebView is null, the method returns <see
        /// langword="false"/>.</remarks>
        /// <returns>A <see langword="true"/> value if the Kick stream is online; otherwise, <see langword="false"/>.</returns>
        private async Task<bool> IsKickStreamOnline()
        {
            if (KickWebView == null)
                return false;

            string js = @"
                (() => {
                    let headings = document.evaluate(""//span[contains(., 'LIVE')]"", document, null, XPathResult.ANY_TYPE, null );
                    let thisHeading = headings.iterateNext();
                    return thisHeading != null;
                })();
            ";

            string rawResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView.ExecuteScriptAsync(js));
            bool isOnline = rawResult?
                .Trim()
                .Trim('"')
                .Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

            AppLogger.Debug("KickSelection", $"[DropsInventoryManager] Kick stream online status: {isOnline}");
            return isOnline;
        }
        /// <summary>
        /// Determines whether the current Kick stream category matches the expected category based on the active Kick
        /// campaign slug.
        /// </summary>
        /// <remarks>This method retrieves the category from the Kick web view and compares it to the slug
        /// of the current Kick campaign. Returns <see langword="false"/> if the web view is not initialized.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task result contains <see langword="true"/> if the
        /// Kick stream category matches the current campaign slug; otherwise, <see langword="false"/>.</returns>
        private async Task<bool> IsKickStreamCategoryCorrect()
        {
            if (KickWebView == null)
                return false;

            string js = @"
                (() => {
                    const categoryElement = document.querySelector("".text-primary-base"");
                    return categoryElement ? categoryElement.href.trim() : '';
                })();
                ";

            string rawResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView.ExecuteScriptAsync(js));
            bool isCorrect = KickCategoryHrefMatchesCampaign(rawResult, _currentKickCampaign?.Slug);

            AppLogger.Debug("KickSelection", $"[DropsInventoryManager] Kick stream category correct status: {isCorrect}");
            return isCorrect;
        }
        /// <summary>
        /// Determines whether the Twitch stream is currently live by evaluating the status indicator in the embedded
        /// web view.
        /// </summary>
        /// <remarks>This method relies on the presence of a specific status indicator element in the
        /// Twitch web view. If the web view is not initialized or the indicator cannot be found, the method returns
        /// <see langword="false"/>.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the Twitch
        /// stream is live; otherwise, <see langword="false"/>.</returns>
        /// <summary>
        /// Raises <see cref="TwitchDropChanged"/> only when the targeted reward changes, to avoid per-tick spam.
        /// </summary>
        private void RaiseTwitchDropChangedIfNeeded(DropsReward? reward)
        {
            if (reward?.Id == _lastTwitchDropId)
                return;

            _lastTwitchDropId = reward?.Id;
            TwitchDropChanged?.Invoke(reward?.Name ?? string.Empty, reward?.ImageUrl);
        }

        /// <summary>
        /// Raises <see cref="KickDropChanged"/> only when the targeted reward changes, to avoid per-tick spam.
        /// </summary>
        private void RaiseKickDropChangedIfNeeded(DropsReward? reward)
        {
            if (reward?.Id == _lastKickDropId)
                return;

            _lastKickDropId = reward?.Id;
            KickDropChanged?.Invoke(reward?.Name ?? string.Empty, reward?.ImageUrl);
        }

        /// <summary>
        /// Authoritatively checks whether a Twitch streamer is live AND streaming the expected category,
        /// using the GraphQL API instead of fragile DOM scraping. Returns <c>null</c> when the check could
        /// not be performed (no GQL service, no login, no slug, or a transient error), so the caller can
        /// fall back to the DOM-based checks.
        /// </summary>
        private async Task<bool?> IsTwitchStreamEligibleViaGqlAsync(string? login, string? slug)
        {
            if (_twitchGqlService == null || string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(slug))
                return null;

            try
            {
                List<string> liveMatches = await _twitchGqlService.QueryLiveChannelsBySlugAsync(new[] { login }, slug);
                bool eligible = liveMatches.Any(l => string.Equals(l, login, StringComparison.OrdinalIgnoreCase));
                AppLogger.Debug("TwitchSelection", $"[GQL eligibility] login={login}, slug={slug} -> {eligible}");
                return eligible;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TwitchSelection", $"GQL eligibility check failed for '{login}' (slug={slug}); falling back to DOM. {ex.Message}");
                return null;
            }
        }

        private async Task<bool> IsTwitchStreamOnline()
        {
            if (TwitchWebView == null)
                return false;

            string js = @"
                (() => {
                    const indicator = document.querySelector("".tw-channel-status-text-indicator"");
                    return indicator?.innerText?.trim() === ""LIVE"";
                })();
            ";

            string rawResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView.ExecuteScriptAsync(js));
            bool isOnline = rawResult?
                .Trim()
                .Trim('"')
                .Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

            AppLogger.Debug("TwitchSelection", $"[DropsInventoryManager] Twitch stream online status: {isOnline}");
            return isOnline;
        }
        /// <summary>
        /// Determines asynchronously whether a Twitch advertisement is currently being displayed in the embedded web
        /// view.
        /// </summary>
        /// <remarks>This method checks for the presence of known Twitch ad indicators in the web view's
        /// DOM. It returns <see langword="false"/> if the web view is not available.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if a Twitch ad
        /// is detected; otherwise, <see langword="false"/>.</returns>
        private async Task<bool> IsTwitchShowingAd()
        {
            if (TwitchWebView == null)
                return false;

            string js = @"
                (() => {
                    const adSelectors = [
                    '[data-a-target=""video-ad-countdown""]',
                    '[data-a-target=""video-ad-label""]',
                    '[data-test-selector=""ad-banner-default-text""]'
                  ];

                  // Check if ANY of these elements exist in the document
                  return adSelectors.some(selector => 
                    document.querySelector(selector) !== null
                  );
                })();
            ";

            string rawResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView.ExecuteScriptAsync(js));
            bool isAdShowing = rawResult?
                .Trim()
                .Trim('"')
                .Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

            AppLogger.Debug("TwitchSelection", $"[DropsInventoryManager] Twitch showing ad status: {isAdShowing}");
            return isAdShowing;
        }
        /// <summary>
        /// Determines whether the current Twitch stream category matches the expected category for the active campaign.
        /// </summary>
        /// <remarks>This method retrieves the current category from the Twitch stream by executing a
        /// JavaScript snippet in the TwitchWebView. The comparison is case-insensitive and ignores leading or trailing
        /// whitespace. Returns <see langword="false"/> if the TwitchWebView is not initialized.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task result contains <see langword="true"/> if the
        /// Twitch stream category matches the expected campaign category; otherwise, <see langword="false"/>.</returns>
        private async Task<bool> IsTwitchStreamCategoryCorrect()
        {
            if (TwitchWebView == null)
                return false;

            string js = @"
                (() => {
                    const links = Array.from(document.querySelectorAll('[data-a-target=stream-game-link]'));
                    return links
                        .map(link => link?.href?.trim())
                        .filter(Boolean)
                        .join('|');
                })();
                ";

            string rawResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView.ExecuteScriptAsync(js));
            bool isCorrect = TwitchCategoryHrefMatchesCampaign(rawResult, _currentTwitchCampaign?.Slug);

            AppLogger.Debug("TwitchSelection", $"[DropsInventoryManager] Twitch stream category correct status: {isCorrect}");
            return isCorrect;
        }
        /// <summary>
        /// Selects the appropriate Kick streamer URL for the specified drops campaign.
        /// </summary>
        /// <remarks>If the campaign is a general drop, the method attempts to locate a streamer whose
        /// campaign name matches the specified campaign. Otherwise, it returns the first URL in the campaign's
        /// connection list. The method relies on the KickWebView instance to navigate and execute JavaScript in order
        /// to extract the streamer URL.</remarks>
        /// <param name="campaign">The drops campaign for which to select a Kick streamer URL. Must not be null.</param>
        /// <returns>A string containing the URL of the selected Kick streamer for the campaign. Returns a category-matching
        /// connection URL for non-general campaigns; otherwise, the first streamer from the matching directory section,
        /// or an empty string if no suitable streamer is found.</returns>
        private async Task<string> SelectKickStreamerForCampaign(DropsCampaign campaign)
        {
            string streamerUrl = string.Empty;
            TryGetLastStreamerUrl(Platform.Kick, campaign.Slug, out string? rememberedKickUrl);

            string getStreamerCategoryJs = @"
                (() => {
                    const categoryElement = document.querySelector("".text-primary-base"");
                    return categoryElement ? categoryElement.href.trim() : '';
                })();
            ";
            string getFirstStreamerFromDirectoryJs;

            if (string.IsNullOrEmpty(campaign.Slug))
            {
                getFirstStreamerFromDirectoryJs = $@"
                    (() => {{
                        const link = document.querySelectorAll('section>div.group\\/card>a')[0].href
                        return link ? link.trim() : '';
                    }})();
                ";
            }
            else
            {
                getFirstStreamerFromDirectoryJs = $@"
                    (() => {{
                        const titles = document.querySelectorAll('h3.text-base.font-bold.leading-5');
                        if (titles.length === 0) return '';
                        let targetSection = null;
                        for (const h3 of titles) {{
                            if (h3.innerText.includes('{campaign.Name.Replace("'", "\\'")}')) {{
                                targetSection = h3.closest('section') || h3.parentElement.parentElement.parentElement.parentElement;
                                break;
                            }}
                        }}
                        if (!targetSection) return '';
                        const streamGrid = targetSection.querySelector(':scope > div:nth-child(2)') || targetSection.children[1];
                        if (!streamGrid || streamGrid.children.length === 0) return '';
                        const firstCard = streamGrid.children[0];
                        const link = firstCard.querySelector('a');
                        return link ? link.href.trim() : '';
                    }})();
                ";
            }

            if (!campaign.IsGeneralDrop)
            {
                // NON-GENERAL DROPS
                IEnumerable<string> orderedConnectUrls = campaign.ConnectUrls;
                if (!string.IsNullOrWhiteSpace(rememberedKickUrl) && campaign.ConnectUrls.Contains(rememberedKickUrl))
                {
                    orderedConnectUrls = new[] { rememberedKickUrl! }
                        .Concat(campaign.ConnectUrls)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                }

                foreach (string connectUrl in orderedConnectUrls)
                {
                    await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.NavigateAsync(connectUrl));
                    await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.WaitForNetworkIdleAsync(5000, 500));

                    string categoryResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.ExecuteScriptAsync(getStreamerCategoryJs));

                    if (KickCategoryHrefMatchesCampaign(categoryResult, campaign.Slug))
                    {
                        streamerUrl = connectUrl;

                        if (!string.IsNullOrWhiteSpace(rememberedKickUrl) &&
                            string.Equals(connectUrl, rememberedKickUrl, StringComparison.OrdinalIgnoreCase))
                        {
                            AppLogger.Info("KickSelection", $"Remembered Kick streamer accepted for campaign '{campaign.Name}': {connectUrl}");
                        }

                        break;
                    }

                    AppLogger.Warn("KickSelection", $"Kick URL category mismatch for campaign '{campaign.Name}'. url='{connectUrl}', category='{categoryResult.Trim('"')}', slug='{campaign.Slug}'");
                }
            }
            else
            {
                // Step 1: Try remembered URL if available (validate category!)
                if (!string.IsNullOrWhiteSpace(rememberedKickUrl))
                {
                    AppLogger.Info("KickSelection", $"Trying remembered Kick streamer for general campaign '{campaign.Name}': {rememberedKickUrl}");

                    await await Application.Current.Dispatcher.InvokeAsync(async () =>
                        await KickWebView!.NavigateAsync(rememberedKickUrl));

                    await Task.Delay(1500);  // Consider → WaitForNetworkIdleAsync(5000, 500) for better sync

                    string categoryResult = await await Application.Current.Dispatcher.InvokeAsync(async () =>
                        await KickWebView!.ExecuteScriptAsync(getStreamerCategoryJs));

                    if (KickCategoryHrefMatchesCampaign(categoryResult, campaign.Slug))
                    {
                        AppLogger.Info("KickSelection", $"Remembered streamer still matches category for general campaign '{campaign.Name}': {rememberedKickUrl}");
                        streamerUrl = rememberedKickUrl!;
                    }
                    else
                    {
                        AppLogger.Warn("KickSelection", $"Remembered URL no longer matches category for general '{campaign.Name}': {rememberedKickUrl} | found: '{categoryResult}'");
                        // fall through to directory fallback
                    }
                }

                // Step 2: If no valid remembered → navigate category directory → extract first streamer via JS
                if (string.IsNullOrWhiteSpace(streamerUrl) && campaign.ConnectUrls?.Any() == true)
                {
                    string directoryUrl = campaign.ConnectUrls[0];  // category page with live list

                    AppLogger.Info("KickSelection", $"Falling back to category directory for general campaign '{campaign.Name}': {directoryUrl}");

                    await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.NavigateAsync(directoryUrl));

                    await Task.Delay(1500);  // Again - consider WaitForNetworkIdleAsync if needed

                    string firstStreamerRawResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.ExecuteScriptAsync(getFirstStreamerFromDirectoryJs));

                    streamerUrl = firstStreamerRawResult?.Trim().Trim('"') ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(streamerUrl))
                    {
                        AppLogger.Info("KickSelection", $"Selected first live streamer from directory for '{campaign.Name}': {streamerUrl}");
                    }
                    else
                    {
                        AppLogger.Warn("KickSelection", $"Failed to extract any first streamer from directory '{directoryUrl}' for general campaign '{campaign.Name}'");
                    }
                }
            }

            // Final logging and event
            if (!string.IsNullOrWhiteSpace(streamerUrl))
            {
                KickChannelChanged?.Invoke(GetStreamerNameFromUrl(streamerUrl));
                KickCampaignChanged?.Invoke(campaign.Name, campaign.GameImageUrl);
                AppLogger.Debug("KickSelection", $"[DropsInventoryManager] Selected Kick streamer URL for general campaign '{campaign.Name}': {streamerUrl}");
            }
            else
            {
                AppLogger.Warn("KickSelection", $"No valid Kick streamer URL resolved for general campaign '{campaign.Name}'.");
            }

            return streamerUrl;
        }
        /// <summary>
        /// Selects the appropriate Twitch streamer URL for the specified drops campaign.
        /// </summary>
        /// <remarks>For general drop campaigns, this method navigates the Twitch web view to the
        /// campaign's first connection URL and attempts to extract the URL of the first streamer listed in the
        /// directory. The returned URL may be empty if no streamer is found.</remarks>
        /// <param name="campaign">The drops campaign for which to select a Twitch streamer. Must not be null.</param>
        /// <returns>A string containing the URL of the selected Twitch streamer for the campaign. Returns the first connection
        /// URL that matches category if the campaign is not a general drop; otherwise, returns the URL of the first streamer found in the
        /// Twitch directory, or an empty string if none is found.</returns>
        private async Task<string> SelectTwitchStreamerForCampaign(DropsCampaign campaign)
        {
            string streamerUrl = string.Empty;
            TryGetLastStreamerUrl(Platform.Twitch, campaign.Slug, out string? rememberedTwitchUrl);

            string getStreamerCategoryHrefJs = @"
                (() => {
                    const links = Array.from(document.querySelectorAll('[data-a-target=stream-game-link]'));
                    return links
                        .map(link => link?.href?.trim())
                        .filter(Boolean)
                        .join('|');
                })();
            ";

            string getFirstStreamerJs = @"
                (() => {
                    const firstItem = document.querySelector('div[data-target=""directory-first-item""]');
                    if (!firstItem) return '';
                    const link = firstItem.querySelector('a[href^=""\/""]');
                    return link ? 'https://www.twitch.tv' + link.getAttribute('href') : '';
                })();
            ";

            if (!campaign.IsGeneralDrop)
            {
                IEnumerable<string> orderedConnectUrls = campaign.ConnectUrls;
                if (!string.IsNullOrWhiteSpace(rememberedTwitchUrl) && campaign.ConnectUrls.Contains(rememberedTwitchUrl))
                {
                    orderedConnectUrls = new[] { rememberedTwitchUrl! }
                        .Concat(campaign.ConnectUrls)
                        .Distinct(StringComparer.OrdinalIgnoreCase);
                }

                // If there are many ConnectUrls, batch-check live status via GQL
                // instead of navigating the WebView one by one
                const int webViewThreshold = 10;
                if (campaign.ConnectUrls.Count > webViewThreshold)
                {
                    AppLogger.Info("TwitchSelection", $"Campaign '{campaign.Name}' has {campaign.ConnectUrls.Count} ConnectUrls - using batch GQL live check.");

                    List<string> loginNames = orderedConnectUrls
                        .Select(GetStreamerNameFromUrl)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();

                    List<string> liveLogins = await _twitchGqlService!.QueryLiveChannelsBySlugAsync(loginNames, campaign.Slug);

                    if (liveLogins.Count == 0)
                    {
                        AppLogger.Warn("TwitchSelection", $"No live streamers found for campaign '{campaign.Name}' via batch GQL.");
                    }
                    else
                    {
                        // QueryLiveChannelsBySlug already guarantees each login is live AND in the
                        // correct category (server-side), so accept the first one directly instead of
                        // navigating and re-checking via fragile DOM scraping (which rejected valid streamers).
                        string acceptedLogin = liveLogins[0];
                        streamerUrl = $"https://www.twitch.tv/{acceptedLogin}";
                        AppLogger.Info("TwitchSelection", $"Batch GQL streamer accepted for campaign '{campaign.Name}': {streamerUrl} (live+category confirmed via GQL; {liveLogins.Count} candidates).");
                    }
                }
                else
                {
                    // Original sequential WebView path for small ConnectUrl lists
                    foreach (string connectUrl in orderedConnectUrls)
                    {
                        await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView!.NavigateAsync(connectUrl));
                        await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView!.WaitForNetworkIdleAsync(5000, 500));

                        string categoryHrefResult = await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView!.ExecuteScriptAsync(getStreamerCategoryHrefJs));

                        if (TwitchCategoryHrefMatchesCampaign(categoryHrefResult, campaign.Slug))
                        {
                            streamerUrl = connectUrl;

                            if (!string.IsNullOrWhiteSpace(rememberedTwitchUrl) &&
                                string.Equals(connectUrl, rememberedTwitchUrl, StringComparison.OrdinalIgnoreCase))
                            {
                                AppLogger.Info("TwitchSelection", $"Remembered Twitch streamer accepted for campaign '{campaign.Name}': {connectUrl}");
                            }

                            break;
                        }

                        AppLogger.Warn("TwitchSelection", $"Twitch URL category mismatch for campaign '{campaign.Name}'. url='{connectUrl}', categoryHrefs='{categoryHrefResult.Trim().Trim('"')}', slug='{campaign.Slug}'");
                    }
                }
            }
            else
            {
                // Step 1: Try remembered URL if available (validate it!)
                if (!string.IsNullOrWhiteSpace(rememberedTwitchUrl))
                {
                    AppLogger.Info("TwitchSelection", $"Trying remembered Twitch streamer for general campaign '{campaign.Name}': {rememberedTwitchUrl}");

                    await await Application.Current.Dispatcher.InvokeAsync(async () =>
                        await TwitchWebView!.NavigateAsync(rememberedTwitchUrl));

                    await Task.Delay(1500);  // Consider replacing with WaitForNetworkIdleAsync(5000, 500) for consistency

                    string categoryHrefResult = await await Application.Current.Dispatcher.InvokeAsync(async () =>
                        await TwitchWebView!.ExecuteScriptAsync(getStreamerCategoryHrefJs));

                    if (TwitchCategoryHrefMatchesCampaign(categoryHrefResult, campaign.Slug))
                    {
                        AppLogger.Info("TwitchSelection", $"Remembered streamer still matches category for general campaign '{campaign.Name}': {rememberedTwitchUrl}");
                        streamerUrl = rememberedTwitchUrl!;
                    }
                    else
                    {
                        AppLogger.Warn("TwitchSelection", $"Remembered URL no longer matches category for general '{campaign.Name}': {rememberedTwitchUrl} | found: '{categoryHrefResult}'");
                        // → fall through to directory fallback
                    }
                }

                // Step 2: If no valid remembered → use category directory + pick first live streamer
                if (string.IsNullOrWhiteSpace(streamerUrl) && campaign.ConnectUrls?.Any() == true)
                {
                    string directoryUrl = campaign.ConnectUrls[0];  // category/game directory page

                    AppLogger.Info("TwitchSelection", $"Falling back to category directory for general campaign '{campaign.Name}': {directoryUrl}");

                    await await Application.Current.Dispatcher.InvokeAsync(async () =>
                        await TwitchWebView!.NavigateAsync(directoryUrl));

                    await Task.Delay(1500);  // Again - consider WaitForNetworkIdleAsync if timing issues occur

                    string firstStreamerRawResult = await await Application.Current.Dispatcher.InvokeAsync(async () =>
                        await TwitchWebView!.ExecuteScriptAsync(getFirstStreamerJs));

                    streamerUrl = firstStreamerRawResult?.Trim().Trim('"') ?? string.Empty;

                    if (!string.IsNullOrWhiteSpace(streamerUrl))
                    {
                        AppLogger.Info("TwitchSelection", $"Selected first live streamer from directory for '{campaign.Name}': {streamerUrl}");
                    }
                    else
                    {
                        AppLogger.Warn("TwitchSelection", $"Failed to extract any first streamer from directory '{directoryUrl}' for general campaign '{campaign.Name}'");
                    }
                }
            }

            // Final logging and event
            if (!string.IsNullOrWhiteSpace(streamerUrl))
            {
                TwitchChannelChanged?.Invoke(GetStreamerNameFromUrl(streamerUrl));
                TwitchCampaignChanged?.Invoke(campaign.Name, campaign.GameImageUrl);
                AppLogger.Debug("TwitchSelection", $"[DropsInventoryManager] Selected Twitch streamer URL for general campaign '{campaign.Name}': {streamerUrl}");
            }
            else
            {
                AppLogger.Warn("TwitchSelection", $"No valid Twitch streamer URL resolved for general campaign '{campaign.Name}'.");
            }

            return streamerUrl;
        }
        /// <summary>
        /// Determines whether the specified category hrefs contain a directory path matching the given campaign slug.
        /// </summary>
        /// <remarks>The comparison is case-insensitive and ignores leading or trailing whitespace and
        /// quotes in the hrefs. Returns false if either parameter is null or consists only of whitespace.</remarks>
        /// <param name="rawCategoryHrefs">A string containing one or more category hrefs to search, which may include surrounding whitespace or
        /// quotes. Can be null.</param>
        /// <param name="campaignSlug">The campaign slug to match within the category hrefs. Can be null.</param>
        /// <returns>true if the hrefs contain a directory path for the specified campaign slug; otherwise, false.</returns>
        private static bool TwitchCategoryHrefMatchesCampaign(string? rawCategoryHrefs, string? campaignSlug)
        {
            if (string.IsNullOrWhiteSpace(rawCategoryHrefs) || string.IsNullOrWhiteSpace(campaignSlug))
                return false;

            string expectedCategoryPath = $"/directory/category/{campaignSlug}";
            string hrefs = rawCategoryHrefs.Trim().Trim('"');
            return hrefs.Contains(expectedCategoryPath, StringComparison.OrdinalIgnoreCase);
        }
        private static bool KickCategoryHrefMatchesCampaign(string? rawCategoryHrefs, string? campaignSlug)
        {
            if (string.IsNullOrWhiteSpace(rawCategoryHrefs) || rawCategoryHrefs == "null")
                return false;
            else if (string.IsNullOrWhiteSpace(campaignSlug))
                return true;

            string expectedCategoryPath = $"/category/{campaignSlug}";
            string hrefs = rawCategoryHrefs.Trim().Trim('"');
            return hrefs.Contains(expectedCategoryPath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempts to retrieve the last known streamer URL for the specified platform and campaign slug.
        /// </summary>
        /// <remarks>This method is thread-safe. The returned URL may be null if no streamer has been
        /// recorded for the given campaign slug.</remarks>
        /// <param name="platform">The platform for which to retrieve the last streamer URL. Must be a valid value of the Platform enumeration.</param>
        /// <param name="campaignSlug">The campaign identifier used to look up the streamer URL. Cannot be null, empty, or whitespace.</param>
        /// <param name="url">When this method returns, contains the last known streamer URL if found; otherwise, null.</param>
        /// <returns>true if a valid streamer URL was found for the specified platform and campaign slug; otherwise, false.</returns>
        private bool TryGetLastStreamerUrl(Platform platform, string? campaignSlug, out string? url)
        {
            url = null;
            if (string.IsNullOrWhiteSpace(campaignSlug))
                return false;

            lock (_lastStreamerSync)
            {
                Dictionary<string, string> source = platform == Platform.Twitch ? _lastTwitchStreamers : _lastKickStreamers;
                if (!source.TryGetValue(campaignSlug, out string? remembered) || string.IsNullOrWhiteSpace(remembered))
                    return false;

                url = remembered;
                return true;
            }
        }
        /// <summary>
        /// Stores the last mined streamer URL for the specified platform and campaign if the provided values are
        /// valid.
        /// </summary>
        /// <remarks>If the campaign slug or streamer URL is invalid, the method does not update the
        /// stored value. Updates are persisted only if the value changes.</remarks>
        /// <param name="platform">The platform for which to record the last mined streamer URL. Determines whether Twitch or Kick is
        /// updated.</param>
        /// <param name="campaignSlug">The unique identifier for the campaign. Cannot be null, empty, or whitespace.</param>
        /// <param name="streamerUrl">The URL of the streamer to remember. Cannot be null, empty, or whitespace.</param>
        private void RememberLastStreamerUrl(Platform platform, string? campaignSlug, string? streamerUrl)
        {
            if (string.IsNullOrWhiteSpace(campaignSlug) || string.IsNullOrWhiteSpace(streamerUrl))
                return;

            bool changed = false;

            lock (_lastStreamerSync)
            {
                Dictionary<string, string> target = platform == Platform.Twitch ? _lastTwitchStreamers : _lastKickStreamers;
                if (!target.TryGetValue(campaignSlug, out string? existing) || !string.Equals(existing, streamerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    target[campaignSlug] = streamerUrl;
                    changed = true;
                }
            }

            if (changed)
                SaveLastMinedStreamers();
        }
        /// <summary>
        /// Removes the remembered streamer URL for the specified platform and campaign, if present.
        /// </summary>
        /// <param name="platform">The platform whose remembered streamer collection should be updated.</param>
        /// <param name="campaignSlug">The campaign slug key associated with the remembered streamer.</param>
        private void ForgetLastStreamerUrl(Platform platform, string? campaignSlug)
        {
            if (string.IsNullOrWhiteSpace(campaignSlug))
                return;

            bool removed;
            lock (_lastStreamerSync)
            {
                Dictionary<string, string> target = platform == Platform.Twitch ? _lastTwitchStreamers : _lastKickStreamers;
                removed = target.Remove(campaignSlug);
            }

            if (removed)
            {
                SaveLastMinedStreamers();
                AppLogger.Info("Selection", $"Forgot remembered streamer for platform={platform}, campaignSlug='{campaignSlug}'.");
            }
        }
        /// <summary>
        /// Loads the last mined streamers from persistent storage and updates the internal state.
        /// </summary>
        /// <remarks>This method reads streamer information from a file and updates the Twitch and Kick
        /// streamer lists. If the file does not exist or contains invalid data, the lists are not modified. The
        /// operation is thread-safe and logs informational or warning messages based on the outcome.</remarks>
        private void LoadLastMinedStreamers()
        {
            try
            {
                if (!File.Exists(_lastMinedStreamersFilePath))
                    return;

                string json = File.ReadAllText(_lastMinedStreamersFilePath);
                LastMinedStreamersState? state = JsonSerializer.Deserialize<LastMinedStreamersState>(json);
                if (state == null)
                    return;

                lock (_lastStreamerSync)
                {
                    _lastTwitchStreamers.Clear();
                    _lastKickStreamers.Clear();

                    foreach ((string key, string value) in state.TwitchBySlug)
                    {
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                            _lastTwitchStreamers[key] = value;
                    }

                    foreach ((string key, string value) in state.KickBySlug)
                    {
                        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                            _lastKickStreamers[key] = value;
                    }
                }

                AppLogger.Info("StreamSelection", $"Loaded remembered streamers. twitch={_lastTwitchStreamers.Count}, kick={_lastKickStreamers.Count}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("StreamSelection", $"Failed loading remembered streamers. {ex.Message}");
            }
        }
        /// <summary>
        /// Persists the current state of last mined streamers to disk in JSON format.
        /// </summary>
        /// <remarks>This method serializes the last mined Twitch and Kick streamers and writes them to
        /// the configured file path. If the target directory does not exist, it is created. Any errors during the save
        /// operation are logged as warnings. The method is thread-safe and should be called when the state needs to be
        /// updated on disk.</remarks>
        private void SaveLastMinedStreamers()
        {
            try
            {
                LastMinedStreamersState snapshot;

                lock (_lastStreamerSync)
                {
                    snapshot = new LastMinedStreamersState
                    {
                        TwitchBySlug = _lastTwitchStreamers.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase),
                        KickBySlug = _lastKickStreamers.ToDictionary(k => k.Key, v => v.Value, StringComparer.OrdinalIgnoreCase)
                    };
                }

                string? directory = Path.GetDirectoryName(_lastMinedStreamersFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_lastMinedStreamersFilePath, json);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("StreamSelection", $"Failed saving remembered streamers. {ex.Message}");
            }
        }

        private void LoadPinnedCampaignFromDisk()
        {
            try
            {
                if (!File.Exists(_pinnedCampaignCacheFilePath))
                    return;

                string json = File.ReadAllText(_pinnedCampaignCacheFilePath, Encoding.UTF8);
                PinnedCampaignCacheEntry? entry = JsonSerializer.Deserialize<PinnedCampaignCacheEntry>(json);

                if (entry != null && !string.IsNullOrWhiteSpace(entry.CampaignId))
                {
                    _pinnedCampaignId = entry.CampaignId;
                    AppLogger.Info("Inventory", $"[PinnedCampaign] Restored pinned campaign '{_pinnedCampaignId}' from disk.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Inventory", $"[PinnedCampaign] Failed to load cache. {ex.Message}");
            }
        }

        private void SavePinnedCampaignToDisk()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_pinnedCampaignCacheFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(
                    new PinnedCampaignCacheEntry { CampaignId = _pinnedCampaignId },
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(_pinnedCampaignCacheFilePath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Inventory", $"[PinnedCampaign] Failed to save cache. {ex.Message}");
            }
        }

        /// <summary>
        /// Serializable cache entry for the user-pinned mining campaign.
        /// </summary>
        private sealed class PinnedCampaignCacheEntry
        {
            /// <summary>
            /// Gets or sets the identifier of the pinned campaign, or null when no campaign is pinned.
            /// </summary>
            public string? CampaignId { get; set; }
        }

        /// <summary>
        /// Represents the state containing mappings of streamer slugs to their Twitch and Kick usernames.
        /// </summary>
        /// <remarks>This class is used to track the last mined streamers for each platform. The
        /// dictionaries are case-insensitive with respect to streamer slugs.</remarks>
        private sealed class LastMinedStreamersState
        {
            /// <summary>
            /// Gets or sets Twitch streamer logins keyed by campaign slug.
            /// </summary>
            public Dictionary<string, string> TwitchBySlug { get; set; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Gets or sets Kick streamer logins keyed by campaign slug.
            /// </summary>
            public Dictionary<string, string> KickBySlug { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Extracts the streamer name from the specified Twitch or Kick channel URL.
        /// </summary>
        /// <remarks>This method expects the URL path to be in the format "/{streamerName}". If the URL is
        /// not valid or does not match the expected format, the method returns an empty string.</remarks>
        /// <param name="url">The URL of the Twitch or Kick channel from which to extract the streamer name. Must be a valid absolute URL.</param>
        /// <returns>The streamer name extracted from the URL, or an empty string if the URL is invalid or does not contain a
        /// streamer name.</returns>
        private string GetStreamerNameFromUrl(string url)
        {
            try
            {
                Uri uri = new Uri(url);
                string path = uri.AbsolutePath.Trim('/');
                // Twitch URLs are typically in the format /{streamerName}
                // Kick URLs are typically in the format /{streamerName}
                return path.Split('/')[0];
            }
            catch (Exception ex)
            {
                AppLogger.Warn("StreamSelection", $"Failed extracting streamer name from url '{url}'. {ex.Message}");
                return string.Empty;
            }
        }
    }

    /// <summary>
    /// Provides extension methods for evaluating progress and completion metrics on DropsCampaign instances.
    /// </summary>
    /// <remarks>These methods assist in determining reward claim status and calculating aggregate progress
    /// for campaigns. All methods require a non-null DropsCampaign instance as input.</remarks>
    public static class DropsCampaignExtensions
    {
        /// <summary>
        /// Determines whether the specified campaign contains any rewards that have not yet been claimed and still
        /// require additional mine progress.
        /// </summary>
        /// <param name="campaign">The campaign to evaluate for unclaimed rewards with remaining progress requirements. Cannot be null.</param>
        /// <returns>true if at least one reward in the campaign is unclaimed and still has progress remaining;
        /// otherwise, false.</returns>
        public static bool HasProgressToMake(this DropsCampaign campaign)
        {
            if (UISettingsManager.Instance.AutoClaimRewards)
                return campaign.Rewards.Any(r => !r.IsClaimed);
            else
                return campaign.Rewards.Any(r => !r.IsClaimed && r.ProgressMinutes < r.RequiredMinutes);
        }

        /// <summary>
        /// Determines whether the specified campaign contains any rewards that are fully progressed and waiting to be claimed.
        /// </summary>
        /// <param name="campaign">The campaign to evaluate for ready-to-claim rewards. Cannot be null.</param>
        /// <returns>true if at least one unclaimed reward in the campaign has met or exceeded its required progress;
        /// otherwise, false.</returns>
        public static bool HasReadyToClaimRewards(this DropsCampaign campaign)
        {
            return campaign.Rewards.Any(r => !r.IsClaimed && r.ProgressMinutes >= r.RequiredMinutes);
        }

        /// <summary>
        /// Calculates the overall completion percentage of all rewards in the specified campaign that require progress.
        /// </summary>
        /// <remarks>Only rewards with a positive required minutes value are considered in the
        /// calculation. The percentage is based on the ratio of progress minutes to required minutes for each valid
        /// reward.</remarks>
        /// <param name="campaign">The campaign for which to calculate the completion percentage. Cannot be null.</param>
        /// <returns>A value between 0 and 100 representing the average completion percentage of all rewards with required
        /// progress. Returns 0 if there are no such rewards.</returns>
        public static double CompletionPercentage(this DropsCampaign campaign)
        {
            IEnumerable<DropsReward> valid = campaign.Rewards.Where(r => r.RequiredMinutes > 0);

            if (!valid.Any())
                return 0;

            return valid.Average(r => (double)r.ProgressMinutes / r.RequiredMinutes) * 100;
        }
    }
}