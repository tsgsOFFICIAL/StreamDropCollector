using System.Collections.ObjectModel;
using Core.Services.Mining.Kick;
using System.Windows.Input;
using Core.Services.Mining;
using Core.Services.Mining.Twitch;
using Core.Mining.Twitch;
using Core.Mining.Kick;
using Core.Interfaces;
using System.Windows;
using System.Timers;
using Core.Logging;
using Core.Stores;
using Core.Mining;
using Core.Models;
using Core.Enums;
using Core.Helpers;

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
        /// Occurs when Kick channel metadata (live state, profile images) is refreshed for eligible streamers.
        /// </summary>
        public event Action<IReadOnlyDictionary<string, LiveChannelSnapshot>>? KickStreamerMetadataChanged;

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

        private string? _currentTwitchLogin; // login of the Twitch streamer currently being mined
        private string? _currentKickLogin;   // login of the Kick streamer currently being mined
        private IGqlService? _twitchGqlService;

        private readonly ActiveCampaignSelectionContext _selection = new();
        private readonly ActiveCampaignUpdater _campaignUpdater = new();
        private readonly PlatformProgressState _twitchProgress = new();
        private readonly PlatformProgressState _kickProgress = new();
        private readonly PinnedCampaignStore _pinnedCampaignStore = new();
        private readonly LastMinedStreamersStore _lastMinedStreamers = new();
        private ITwitchLiveChannelApi _twitchLiveChannelApi = null!;
        private IKickLiveChannelApi _kickLiveChannelApi = null!;
        private readonly MiningOrchestrator _miningOrchestrator = new();
        private readonly StreamHealthMonitor _streamHealthMonitor = new();
        private KickStreamerSelector? _kickStreamerSelector;
        private TwitchStreamerSelector? _twitchStreamerSelector;

        private bool _lastKnownKickOnlineState;
        private bool _lastKnownTwitchOnlineState;

        // Timer for live ticking
        private readonly System.Timers.Timer _liveProgressTimer = new(1000);
        private System.Timers.Timer? _recheckTimer;

        private readonly SemaphoreSlim _startMiningLock = new(1, 1);
        private CancellationTokenSource? _startMiningCts;
        private bool _isPaused;
        private readonly object _campaignSnapshotSync = new();
        private List<DropsCampaign> _lastKnownCampaigns = new();

        private readonly SemaphoreSlim _kickMetadataLock = new(1, 1);
        private readonly System.Timers.Timer _kickMetadataTimer = new(TimeSpan.FromSeconds(45).TotalMilliseconds);
        private int _kickMetadataRefreshScheduled;
        private IReadOnlyDictionary<string, LiveChannelSnapshot> _kickStreamerMetadata =
            new Dictionary<string, LiveChannelSnapshot>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the most recently fetched Kick streamer metadata keyed by channel login.
        /// </summary>
        public IReadOnlyDictionary<string, LiveChannelSnapshot> KickStreamerMetadata => _kickStreamerMetadata;

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
            _pinnedCampaignStore.SetCampaignId(campaign.Id);
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
            UISettingsManager.Instance.MiningPriorityModeChanged += OnMiningPriorityModeChanged;
            UISettingsManager.Instance.GameWhitelistChanged += OnGameWhitelistChanged;

            _liveProgressTimer.Elapsed += OnLiveProgressTick;
            _liveProgressTimer.AutoReset = true;

            _kickMetadataTimer.Elapsed += (_, _) => ScheduleKickStreamerMetadataRefresh();
            _kickMetadataTimer.AutoReset = true;
            _kickMetadataTimer.Start();

            RefreshMiningServices();
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
                List<DropsCampaign> filteredCampaigns = ActiveCampaignFilter.FilterForDisplay(sourceCampaigns);

                ActiveCampaigns.Clear();
                foreach (DropsCampaign campaign in filteredCampaigns)
                    ActiveCampaigns.Add(campaign);

                _campaignUpdater.UpdateSelectionFlags(ActiveCampaigns, _selection);
            });
        }
        /// <summary>
        /// Handles the timer tick event to update live progress for active Twitch and Kick campaigns.
        /// </summary>
        private void OnLiveProgressTick(object? sender, ElapsedEventArgs e)
        {
            if (_selection.CurrentTwitchCampaign != null)
            {
                LiveProgressTracker.ProcessTick(
                    "Twitch",
                    Platform.Twitch,
                    _selection.CurrentTwitchCampaign,
                    _twitchProgress,
                    reward => TwitchDropChanged?.Invoke(reward?.Name ?? string.Empty, reward?.ImageUrl),
                    (platform, campaignId, minutes) => _campaignUpdater.ApplyMinuteProgress(ActiveCampaigns, _selection, platform, campaignId, minutes, VerboseLog),
                    (campPct, dropPct) => TwitchProgressChanged?.Invoke(campPct, dropPct),
                    VerboseLog);
            }

            if (_selection.CurrentKickCampaign != null)
            {
                LiveProgressTracker.ProcessTick(
                    "Kick",
                    Platform.Kick,
                    _selection.CurrentKickCampaign,
                    _kickProgress,
                    reward => KickDropChanged?.Invoke(reward?.Name ?? string.Empty, reward?.ImageUrl),
                    (platform, campaignId, minutes) => _campaignUpdater.ApplyMinuteProgress(ActiveCampaigns, _selection, platform, campaignId, minutes, VerboseLog),
                    (campPct, dropPct) => KickProgressChanged?.Invoke(campPct, dropPct),
                    VerboseLog);
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
            RefreshMiningServices();
            ScheduleKickStreamerMetadataRefresh();
        }

        /// <summary>
        /// Debounces a background refresh of Kick streamer live state and profile images for inventory UI chips.
        /// </summary>
        public void ScheduleKickStreamerMetadataRefresh()
        {
            if (KickWebView == null)
                return;

            if (Interlocked.CompareExchange(ref _kickMetadataRefreshScheduled, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(400);
                    await RefreshKickStreamerMetadataAsync();
                }
                catch (OperationCanceledException)
                {
                    // Expected when superseded.
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("KickMetadata", $"Streamer metadata refresh failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _kickMetadataRefreshScheduled, 0);
                }
            });
        }

        /// <summary>
        /// Fetches Kick channel snapshots for all eligible streamers across active Kick campaigns.
        /// </summary>
        public async Task RefreshKickStreamerMetadataAsync(CancellationToken ct = default)
        {
            if (KickWebView == null)
                return;

            List<DropsCampaign> kickCampaigns = await Application.Current.Dispatcher.InvokeAsync(() =>
                ActiveCampaigns.Where(c => c.Platform == Platform.Kick).ToList());

            HashSet<string> logins = new(StringComparer.OrdinalIgnoreCase);
            foreach (DropsCampaign campaign in kickCampaigns)
            {
                foreach (string login in EligibleStreamerParser.ParseChannelLogins(campaign))
                    logins.Add(login);
            }

            if (logins.Count == 0)
            {
                _kickStreamerMetadata = new Dictionary<string, LiveChannelSnapshot>(StringComparer.OrdinalIgnoreCase);
                Application.Current.Dispatcher.Invoke(() =>
                    KickStreamerMetadataChanged?.Invoke(_kickStreamerMetadata));
                return;
            }

            await _kickMetadataLock.WaitAsync(ct);
            try
            {
                Dictionary<string, LiveChannelSnapshot> snapshots = new(StringComparer.OrdinalIgnoreCase);

                foreach (string login in logins)
                {
                    ct.ThrowIfCancellationRequested();

                    LiveChannelSnapshot? snapshot = await _kickLiveChannelApi.GetChannelAsync(login, ct);
                    if (snapshot != null)
                        snapshots[login] = snapshot;
                }

                _kickStreamerMetadata = snapshots;

                AppLogger.Debug(
                    "KickMetadata",
                    $"Refreshed {snapshots.Count}/{logins.Count} Kick streamer snapshots ({snapshots.Values.Count(s => s.IsLive)} live).");

                IReadOnlyDictionary<string, LiveChannelSnapshot> published = _kickStreamerMetadata;
                Application.Current.Dispatcher.Invoke(() =>
                    KickStreamerMetadataChanged?.Invoke(published));
            }
            finally
            {
                _kickMetadataLock.Release();
            }
        }

        private void RefreshMiningServices()
        {
            _kickLiveChannelApi = new KickLiveChannelApi(() => KickWebView);
            _twitchLiveChannelApi = new TwitchLiveChannelApi(() => TwitchWebView);
            _kickStreamerSelector = new KickStreamerSelector(_kickLiveChannelApi, _lastMinedStreamers);
            _twitchStreamerSelector = new TwitchStreamerSelector(_twitchLiveChannelApi, _lastMinedStreamers);
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
            RefreshMiningServices();
            List<DropsCampaign> allCampaigns = campaigns.ToList();

            lock (_campaignSnapshotSync)
            {
                _lastKnownCampaigns = [.. allCampaigns];
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                UISettingsManager.Instance.UpdateAvailableGameFilterOptions(allCampaigns);

                List<DropsCampaign> activeCampaignsList = ActiveCampaignFilter.FilterForDisplay(allCampaigns);

                ActiveCampaigns.Clear();
                foreach (DropsCampaign? c in activeCampaignsList)
                    ActiveCampaigns.Add(c);

                _campaignUpdater.UpdateSelectionFlags(ActiveCampaigns, _selection);
            });

            ScheduleKickStreamerMetadataRefresh();

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
            _streamHealthMonitor.Stop();
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
                    $"twitchCurrent={_selection.CurrentTwitchCampaign?.Id ?? "null"} | " +
                    $"kickCurrent={_selection.CurrentKickCampaign?.Id ?? "null"} | " +
                    $"twitchSeconds={_twitchProgress.MinedSeconds} | twitchApplied={_twitchProgress.AppliedMinuteBucket}");

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
                _twitchProgress.LastReportedDropId = null;
                TwitchDropChanged?.Invoke(string.Empty, null);
                TwitchProgressChanged?.Invoke(0, 0);
                KickChannelChanged?.Invoke(string.Empty);
                KickCampaignChanged?.Invoke(string.Empty, null);
                _kickProgress.LastReportedDropId = null;
                KickDropChanged?.Invoke(string.Empty, null);
                KickProgressChanged?.Invoke(0, 0);
                _twitchProgress.SyncAppliedBucketFromMinedSeconds();
                _kickProgress.SyncAppliedBucketFromMinedSeconds();

                VerboseLog("StartMining", $"AFTER reset | twitchApplied={_twitchProgress.AppliedMinuteBucket} | kickApplied={_kickProgress.AppliedMinuteBucket}");

                AppLogger.Debug("Miner", "[DropsInventoryManager] Starting stream mining process...");
                AppLogger.Info("Miner", $"StartMiningStreams invoked. restartedInternally={restartedInternally}, activeCampaigns={ActiveCampaigns.Count}, paused={_isPaused}");

                if (!restartedInternally)
                    MinerStatusChanged?.Invoke("Starting");
                else
                    MinerStatusChanged?.Invoke("Evaluating");

                _recheckTimer?.Stop();
                _streamHealthMonitor.Stop();
                _recheckTimer?.Dispose();
                _recheckTimer = null;

                _selection.CurrentTwitchCampaign = null;
                _selection.CurrentKickCampaign = null;
                _currentTwitchLogin = null;
                _currentKickLogin = null;

                MiningOrchestratorResult result = await _miningOrchestrator.RunAsync(
                    campaignSnapshot,
                    _twitchGqlService,
                    TwitchWebView,
                    KickWebView,
                    SelectBestCampaign,
                    campaign => _twitchStreamerSelector!.SelectUrlAsync(campaign),
                    campaign => _kickStreamerSelector!.SelectUrlAsync(campaign),
                    (login, slug) => _twitchLiveChannelApi.IsChannelEligibleAsync(login, slug),
                    (login, slug) => _kickLiveChannelApi.IsChannelEligibleAsync(login, slug),
                    async url => await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView!.NavigateAsync(url)),
                    async url => await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.NavigateAsync(url)),
                    _lastMinedStreamers,
                    (campaign, login) =>
                    {
                        TwitchChannelChanged?.Invoke(login);
                        TwitchCampaignChanged?.Invoke(campaign.Name, campaign.GameImageUrl);
                    },
                    (campaign, login) =>
                    {
                        KickChannelChanged?.Invoke(login);
                        KickCampaignChanged?.Invoke(campaign.Name, campaign.GameImageUrl);
                    },
                    (campaignId, rewardId) => _campaignUpdater.MarkRewardClaimed(ActiveCampaigns, _selection, campaignId, rewardId),
                    () => _campaignUpdater.UpdateSelectionFlags(ActiveCampaigns, _selection),
                    token);

                if (!result.CompletedSelectionCycle)
                {
                    MinerStatusChanged?.Invoke(result.MinerStatus);
                    _campaignUpdater.UpdateSelectionFlags(ActiveCampaigns, _selection);
                    return;
                }

                if (token.IsCancellationRequested)
                    return;

                ApplyPlatformMiningResult(result.Twitch, Platform.Twitch);
                ApplyPlatformMiningResult(result.Kick, Platform.Kick);

                StartStreamHealthMonitoring();

                _liveProgressTimer?.Start();

                DateTime nextCheckAt = result.NextCheckAt;
                double delayMs = Math.Max((nextCheckAt - DateTime.Now).TotalMilliseconds, 60000);
                _recheckTimer = new System.Timers.Timer(delayMs);
                _recheckTimer.Elapsed += async (_, _) =>
                {
                    _recheckTimer?.Stop();
                    AppLogger.Debug("Miner", "[DropsInventoryManager] Re-evaluating streams for active campaigns.");
                    AppLogger.Info("Miner", "Scheduled re-evaluation triggered.");
                    await StartMiningStreams(true);
                };
                _recheckTimer.AutoReset = false;
                _recheckTimer.Start();

                AppLogger.Debug("Miner", $"[DropsInventoryManager] Next stream re-evaluation in ~{delayMs / 60000:F1} minutes at {nextCheckAt:u}");
                AppLogger.Info("Miner", $"Next re-evaluation in {delayMs / 1000:F0}s at {nextCheckAt:u}. twitchSelected={_selection.CurrentTwitchCampaign != null}, kickSelected={_selection.CurrentKickCampaign != null}");

                MinerStatusChanged?.Invoke(result.MinerStatus);
            }
            finally
            {
                _startMiningLock.Release();
            }
        }
        /// <summary>
        /// Applies a platform mining result to current selection state and raises initial progress events.
        /// </summary>
        private void ApplyPlatformMiningResult(PlatformMiningResult? result, Platform platform)
        {
            if (result == null)
                return;

            MiningBaseline baseline = result.Baseline;
            PlatformProgressState progress = platform == Platform.Twitch ? _twitchProgress : _kickProgress;

            switch (platform)
            {
                case Platform.Twitch:
                    _selection.CurrentTwitchCampaign = result.Campaign;
                    _currentTwitchLogin = result.Login;
                    _lastKnownTwitchOnlineState = true;
                    progress.ApplyBaseline(baseline);

                    VerboseLog("SelectionBaseline",
                        $"Twitch baseline SET | campaignId={result.Campaign.Id} | minedSeconds={progress.MinedSeconds} | dropMinedSeconds={progress.DropMinedSeconds} | appliedBucket={progress.AppliedMinuteBucket}");
                    VerboseLog("SelectionBaseline",
                        $"Twitch campaignId={result.Campaign.Id}, campaignMinedSecondsBaseline={progress.MinedSeconds}, dropMinedSecondsBaseline={progress.DropMinedSeconds}, nextRewardId={baseline.NextReward?.Id ?? "none"}, unclaimedRewards={result.Campaign.Rewards.Count(r => !r.IsClaimed)}");

                    byte twitchCampPct = MiningProgressCalculator.CalculateLiveCampaignProgress(result.Campaign);
                    byte twitchDropPct = MiningProgressCalculator.CalculateLiveDropProgress(result.Campaign, progress.DropMinedSeconds);
                    TwitchProgressChanged?.Invoke(twitchCampPct, twitchDropPct);
                    LiveProgressTracker.RaiseDropChangedIfNeeded(baseline.NextReward, progress, reward =>
                        TwitchDropChanged?.Invoke(reward?.Name ?? string.Empty, reward?.ImageUrl));
                    break;

                case Platform.Kick:
                    _selection.CurrentKickCampaign = result.Campaign;
                    _currentKickLogin = result.Login;
                    _lastKnownKickOnlineState = true;
                    progress.ApplyBaseline(baseline);

                    VerboseLog("SelectionBaseline",
                        $"Kick campaignId={result.Campaign.Id}, campaignMinedSecondsBaseline={progress.MinedSeconds}, dropMinedSecondsBaseline={progress.DropMinedSeconds}, nextRewardId={baseline.NextReward?.Id ?? "none"}, unclaimedRewards={result.Campaign.Rewards.Count(r => !r.IsClaimed)}");

                    byte kickCampPct = MiningProgressCalculator.CalculateLiveCampaignProgress(result.Campaign);
                    byte kickDropPct = MiningProgressCalculator.CalculateLiveDropProgress(result.Campaign, progress.DropMinedSeconds);
                    KickProgressChanged?.Invoke(kickCampPct, kickDropPct);
                    LiveProgressTracker.RaiseDropChangedIfNeeded(baseline.NextReward, progress, reward =>
                        KickDropChanged?.Invoke(reward?.Name ?? string.Empty, reward?.ImageUrl));
                    break;
            }

            _campaignUpdater.UpdateSelectionFlags(ActiveCampaigns, _selection);
        }

        /// <summary>
        /// Begins periodic stream health monitoring via the live-channel APIs.
        /// </summary>
        private void StartStreamHealthMonitoring()
        {
            _streamHealthMonitor.Start(new StreamHealthMonitor.Host
            {
                IsTwitchEligibleAsync = async () => _selection.CurrentTwitchCampaign != null
                    && !string.IsNullOrWhiteSpace(_currentTwitchLogin)
                    && await _twitchLiveChannelApi.IsChannelEligibleAsync(_currentTwitchLogin, _selection.CurrentTwitchCampaign.Slug),
                IsKickEligibleAsync = async () => _selection.CurrentKickCampaign != null
                    && !string.IsNullOrWhiteSpace(_currentKickLogin)
                    && await _kickLiveChannelApi.IsChannelEligibleAsync(_currentKickLogin, _selection.CurrentKickCampaign.Slug),
                HasTwitchCampaignsWithProgress = () => ActiveCampaigns.Any(c => c.Platform == Platform.Twitch && c.HasProgressToMake()),
                HasKickCampaignsWithProgress = () => ActiveCampaigns.Any(c => c.Platform == Platform.Kick && c.HasProgressToMake()),
                GetLastKnownTwitchOnline = () => _lastKnownTwitchOnlineState,
                GetLastKnownKickOnline = () => _lastKnownKickOnlineState,
                SetLastKnownTwitchOnline = value => _lastKnownTwitchOnlineState = value,
                SetLastKnownKickOnline = value => _lastKnownKickOnlineState = value,
                RequestReevaluationAsync = () => StartMiningStreams(true)
            });
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
            CampaignSelectionResult result = CampaignPrioritizer.SelectBest(
                campaigns,
                _pinnedCampaignStore.CampaignId,
                UISettingsManager.Instance.MiningPriorityMode);

            if (result.PinReleased)
                _pinnedCampaignStore.Clear();

            return Task.FromResult(result.Campaign);
        }

    }
}