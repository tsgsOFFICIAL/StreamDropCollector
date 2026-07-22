using System.Collections.ObjectModel;
using Core.Services.Mining.Kick;
using System.Windows.Input;
using Core.Services.Mining;
using Core.Services.Mining.Twitch;
using Core.Services.Twitch.Helix;
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
        /// Occurs when Twitch channel metadata (live state, profile images) is refreshed for eligible streamers.
        /// </summary>
        /// <remarks>
        /// Snapshots are keyed by login slug. Also raised when EventSub updates the mined channel cache.
        /// </remarks>
        public event Action<IReadOnlyDictionary<string, LiveChannelSnapshot>>? TwitchStreamerMetadataChanged;

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
        private readonly ITwitchHelixService _twitchHelixService = TwitchHelixService.Instance;
        private readonly GeneralDropDiscoveryCache _twitchGeneralDropCache = new();
        private readonly GeneralDropDiscoveryCache _kickGeneralDropCache = new();
        private ITwitchLiveChannelApi _twitchLiveChannelApi = null!;
        private IKickLiveChannelApi _kickLiveChannelApi = null!;
        private readonly MiningOrchestrator _miningOrchestrator = new();
        private readonly StreamHealthMonitor _streamHealthMonitor = new();
        private readonly WebViewUiRunner _webViewUiRunner = new();
        private KickStreamerSelector? _kickStreamerSelector;
        private TwitchStreamerSelector? _twitchStreamerSelector;
        private TwitchStreamPageReader? _twitchPageReader;
        private KickStreamPageReader? _kickPageReader;

        private bool _lastKnownKickOnlineState;
        private bool _lastKnownTwitchOnlineState;

        // Timer for live ticking
        private readonly System.Timers.Timer _liveProgressTimer = new(1000);
        private readonly System.Timers.Timer _progressVerifyTimer = new(TimeSpan.FromMinutes(2).TotalMilliseconds);
        private System.Timers.Timer? _recheckTimer;
        private readonly SemaphoreSlim _progressVerifyLock = new(1, 1);
        private int _progressVerifyScheduled;

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

        private readonly SemaphoreSlim _twitchMetadataLock = new(1, 1);
        private readonly System.Timers.Timer _twitchMetadataTimer = new(TimeSpan.FromSeconds(45).TotalMilliseconds);
        private int _twitchMetadataRefreshScheduled;
        private IReadOnlyDictionary<string, LiveChannelSnapshot> _twitchStreamerMetadata =
            new Dictionary<string, LiveChannelSnapshot>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gets the most recently fetched Twitch streamer metadata keyed by channel login.
        /// </summary>
        public IReadOnlyDictionary<string, LiveChannelSnapshot> TwitchStreamerMetadata => _twitchStreamerMetadata;

        private static bool IsVerboseDebugEnabled => UISettingsManager.Instance.VerboseDebugLogging;

        /// <summary>
        /// Logs a message at the informational level if verbose debug logging is enabled.
        /// </summary>
        /// <param name="scope">The logical scope or category associated with the log message. Used to group related log entries.</param>
        /// <param name="message">The message to log. Should provide relevant information about the operation or event.</param>
        private static void VerboseLog(string scope, string message) =>
            AppLogger.Debug(scope, message);

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

            _progressVerifyTimer.Elapsed += (_, _) => ScheduleServerProgressVerification();
            _progressVerifyTimer.AutoReset = true;

            _kickMetadataTimer.Elapsed += (_, _) => ScheduleKickStreamerMetadataRefresh();
            _kickMetadataTimer.AutoReset = true;
            _kickMetadataTimer.Start();

            _twitchMetadataTimer.Elapsed += (_, _) => ScheduleTwitchStreamerMetadataRefresh();
            _twitchMetadataTimer.AutoReset = true;
            _twitchMetadataTimer.Start();

            _twitchHelixService.SnapshotsChanged += OnTwitchHelixSnapshotsChanged;

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
                AppLogger.Debug("Miner", "Immediate re-evaluation completed after priority mode change.");
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
                AppLogger.Debug("Miner", "Immediate re-evaluation completed after whitelist change.");
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
            ScheduleTwitchStreamerMetadataRefresh();
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
                IReadOnlyList<string> campaignLogins = await _kickLiveChannelApi
                    .GetEligibleLoginsAsync(campaign, allowDirectoryDiscovery: false, ct)
                    .ConfigureAwait(false);

                foreach (string login in campaignLogins)
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

        /// <summary>
        /// Debounces a background refresh of Twitch streamer live state and profile images for inventory UI chips.
        /// </summary>
        /// <remarks>No-op when Helix is not authenticated. Uses Helix REST only (no EventSub subscriptions).</remarks>
        public void ScheduleTwitchStreamerMetadataRefresh()
        {
            if (!_twitchHelixService.IsAuthenticated)
                return;

            if (Interlocked.CompareExchange(ref _twitchMetadataRefreshScheduled, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(400);
                    await RefreshTwitchStreamerMetadataAsync();
                }
                catch (OperationCanceledException)
                {
                    // Expected when superseded.
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TwitchMetadata", $"Streamer metadata refresh failed: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _twitchMetadataRefreshScheduled, 0);
                }
            });
        }

        /// <summary>
        /// Fetches Twitch channel snapshots for all eligible streamers across active Twitch campaigns.
        /// </summary>
        /// <remarks>
        /// Raises <see cref="TwitchStreamerMetadataChanged"/> on the UI thread when complete.
        /// EventSub is not used for this pass; see <see cref="ITwitchHelixService.SetMinedChannelWatcherAsync"/>.
        /// </remarks>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that represents the asynchronous metadata refresh.</returns>
        public async Task RefreshTwitchStreamerMetadataAsync(CancellationToken ct = default)
        {
            if (!_twitchHelixService.IsAuthenticated)
                return;

            List<DropsCampaign> twitchCampaigns = await Application.Current.Dispatcher.InvokeAsync(() =>
                ActiveCampaigns.Where(c => c.Platform == Platform.Twitch).ToList());

            HashSet<string> logins = new(StringComparer.OrdinalIgnoreCase);
            foreach (DropsCampaign campaign in twitchCampaigns)
            {
                IReadOnlyList<string> campaignLogins = await _twitchLiveChannelApi
                    .GetEligibleLoginsAsync(campaign, allowDirectoryDiscovery: false, ct)
                    .ConfigureAwait(false);

                foreach (string login in campaignLogins)
                    logins.Add(login);
            }

            if (logins.Count == 0)
            {
                _twitchStreamerMetadata = new Dictionary<string, LiveChannelSnapshot>(StringComparer.OrdinalIgnoreCase);
                Application.Current.Dispatcher.Invoke(() =>
                    TwitchStreamerMetadataChanged?.Invoke(_twitchStreamerMetadata));
                return;
            }

            await _twitchMetadataLock.WaitAsync(ct);
            try
            {
                await _twitchHelixService.RefreshChannelsAsync(logins, ct);
                _twitchStreamerMetadata = _twitchHelixService.Snapshots;

                int liveCount = _twitchStreamerMetadata.Values.Count(s => s.IsLive);
                AppLogger.Debug(
                    "TwitchMetadata",
                    $"Refreshed {_twitchStreamerMetadata.Count}/{logins.Count} Twitch streamer snapshots ({liveCount} live).");
                AppLogger.Debug(
                    "TwitchMining",
                    $"Metadata refresh live={liveCount}/{logins.Count} - metadata counts live across ALL campaign streamers, not category-filtered.");

                IReadOnlyDictionary<string, LiveChannelSnapshot> published = _twitchStreamerMetadata;
                Application.Current.Dispatcher.Invoke(() =>
                    TwitchStreamerMetadataChanged?.Invoke(published));
            }
            finally
            {
                _twitchMetadataLock.Release();
            }
        }

        private void OnTwitchHelixSnapshotsChanged(IReadOnlyDictionary<string, LiveChannelSnapshot> snapshots)
        {
            _twitchStreamerMetadata = snapshots;
            Application.Current.Dispatcher.Invoke(() =>
                TwitchStreamerMetadataChanged?.Invoke(snapshots));
        }

        private void UpdateTwitchMinedChannelWatcher(string? login)
        {
            AppLogger.Debug("TwitchMining", $"UpdateTwitchMinedChannelWatcher login={login ?? "(null)"} helixAuth={_twitchHelixService.IsAuthenticated}");

            if (!_twitchHelixService.IsAuthenticated)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await _twitchHelixService.SetMinedChannelWatcherAsync(login);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TwitchEventSub", $"Failed to update mined-channel watcher: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Gets eligible Twitch streamer logins for UI display (connect URLs or cached general-drop directory results).
        /// </summary>
        public IReadOnlyList<string> GetTwitchEligibleLoginsForCampaign(DropsCampaign campaign)
        {
            if (campaign.Platform != Platform.Twitch)
                return Array.Empty<string>();

            if (!campaign.IsGeneralDrop)
                return EligibleStreamerParser.ParseChannelLogins(campaign);

            if (_twitchLiveChannelApi is TwitchLiveChannelApi concrete)
                return concrete.GetCachedGeneralDropLogins(campaign.Id);

            return _twitchGeneralDropCache.Get(campaign.Id);
        }

        /// <summary>
        /// Gets eligible Kick streamer logins for UI display (connect URLs or cached general-drop directory results).
        /// </summary>
        public IReadOnlyList<string> GetKickEligibleLoginsForCampaign(DropsCampaign campaign)
        {
            if (campaign.Platform != Platform.Kick)
                return Array.Empty<string>();

            if (!campaign.IsGeneralDrop)
                return EligibleStreamerParser.ParseChannelLogins(campaign);

            if (_kickLiveChannelApi is KickLiveChannelApi concrete)
                return concrete.GetCachedGeneralDropLogins(campaign.Id);

            return _kickGeneralDropCache.Get(campaign.Id);
        }

        private void RefreshMiningServices()
        {
            _kickLiveChannelApi = new KickLiveChannelApi(() => KickWebView, _kickGeneralDropCache);
            _twitchLiveChannelApi = new TwitchLiveChannelApi(_twitchHelixService, () => TwitchWebView, _twitchGeneralDropCache);
            _kickStreamerSelector = new KickStreamerSelector(_kickLiveChannelApi, _lastMinedStreamers);
            _twitchStreamerSelector = new TwitchStreamerSelector(_twitchLiveChannelApi, _lastMinedStreamers);
            _twitchPageReader = new TwitchStreamPageReader(TwitchWebView, _webViewUiRunner);
            _kickPageReader = new KickStreamPageReader(KickWebView, _webViewUiRunner);
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

            SchedulePreloadTwitchGeneralDropDirectories();
            SchedulePreloadKickGeneralDropDirectories();
            ScheduleKickStreamerMetadataRefresh();
            ScheduleTwitchStreamerMetadataRefresh();

            if (startMining && !_isPaused)
                _ = StartMiningStreams(); // Fire and forget - will handle its own loop
        }

        /// <summary>
        /// Preloads directory logins for all general-drop Twitch campaigns (UI chips). Does not run during active mining re-evaluations.
        /// </summary>
        public void SchedulePreloadTwitchGeneralDropDirectories()
        {
            if (TwitchWebView is null)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    List<DropsCampaign> snapshot = await Application.Current.Dispatcher.InvokeAsync(() =>
                        ActiveCampaigns.Where(c => c.Platform == Platform.Twitch).ToList());

                    await _twitchLiveChannelApi.PreloadGeneralDropDirectoriesAsync(snapshot);
                    ScheduleTwitchStreamerMetadataRefresh();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TwitchGeneralDrop", $"General-drop preload failed: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Preloads directory logins for all general-drop Kick campaigns (UI chips). Does not run during active mining re-evaluations.
        /// </summary>
        public void SchedulePreloadKickGeneralDropDirectories()
        {
            if (KickWebView is null)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    List<DropsCampaign> snapshot = await Application.Current.Dispatcher.InvokeAsync(() =>
                        ActiveCampaigns.Where(c => c.Platform == Platform.Kick).ToList());

                    await _kickLiveChannelApi.PreloadGeneralDropDirectoriesAsync(snapshot);
                    ScheduleKickStreamerMetadataRefresh();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("KickGeneralDrop", $"General-drop preload failed: {ex.Message}");
                }
            });
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
            _progressVerifyTimer.Stop();
            UpdateTwitchMinedChannelWatcher(null);

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
                {
                    AppLogger.Warn("TwitchMining", "StartMiningStreams ABORT - miner is paused.");
                    return;
                }

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

                int twitchActive = campaignSnapshot.Count(c => c.Platform == Platform.Twitch);
                int twitchWithProgress = campaignSnapshot.Count(c => c.Platform == Platform.Twitch && c.HasProgressToMake());
                int kickWithProgress = campaignSnapshot.Count(c => c.Platform == Platform.Kick && c.HasProgressToMake());
                AppLogger.Debug(
                    "TwitchMining",
                    $"StartMiningStreams gates helixAuth={_twitchHelixService.IsAuthenticated} " +
                    $"twitchWebView={(TwitchWebView is null ? "null" : "ok")} kickWebView={(KickWebView is null ? "null" : "ok")} " +
                    $"twitchActive={twitchActive} twitchWithProgress={twitchWithProgress} kickWithProgress={kickWithProgress}");

                AppLogger.Debug("Miner", $"StartMiningStreams invoked. restartedInternally={restartedInternally}, activeCampaigns={ActiveCampaigns.Count}, paused={_isPaused}");

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
                UpdateTwitchMinedChannelWatcher(null);

                if (!restartedInternally && TwitchWebView is not null)
                {
                    AppLogger.Debug("TwitchMining", "StartMiningStreams preloading general-drop directories before campaign selection.");
                    await _twitchLiveChannelApi.PreloadGeneralDropDirectoriesAsync(campaignSnapshot, token);
                }

                if (!restartedInternally && KickWebView is not null)
                {
                    AppLogger.Debug("KickMining", "StartMiningStreams preloading general-drop directories before campaign selection.");
                    await _kickLiveChannelApi.PreloadGeneralDropDirectoriesAsync(campaignSnapshot, token);
                }

                MiningOrchestratorResult result = await _miningOrchestrator.RunAsync(
                    campaignSnapshot,
                    _twitchGqlService,
                    TwitchWebView,
                    KickWebView,
                    SelectBestCampaign,
                    campaign => _twitchStreamerSelector!.SelectUrlAsync(campaign),
                    campaign => _kickStreamerSelector!.SelectUrlAsync(campaign),
                    (login, campaign) => _twitchLiveChannelApi.IsChannelEligibleAsync(login, campaign),
                    (login, campaign) => _kickLiveChannelApi.IsChannelEligibleAsync(login, campaign),
                    async url => await await Application.Current.Dispatcher.InvokeAsync(async () => await TwitchWebView!.NavigateAsync(url)),
                    async url => await await Application.Current.Dispatcher.InvokeAsync(async () => await KickWebView!.NavigateAsync(url)),
                    () => _twitchPageReader!.PrepareForMiningAsync(token),
                    () => _kickPageReader!.PrepareForMiningAsync(token),
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
                    AppLogger.Warn(
                        "TwitchMining",
                        $"StartMiningStreams incomplete cycle minerStatus={result.MinerStatus} - no stream selected.");
                    MinerStatusChanged?.Invoke(result.MinerStatus);
                    _campaignUpdater.UpdateSelectionFlags(ActiveCampaigns, _selection);
                    return;
                }

                if (token.IsCancellationRequested)
                {
                    AppLogger.Warn("TwitchMining", "StartMiningStreams ABORT - cancellation requested after orchestrator.");
                    return;
                }

                AppLogger.Debug(
                    "TwitchMining",
                    $"StartMiningStreams applying results twitch={(result.Twitch is null ? "null" : $"{result.Twitch.Login}@{result.Twitch.Campaign.Name}")} " +
                    $"kick={(result.Kick is null ? "null" : $"{result.Kick.Login}@{result.Kick.Campaign.Name}")} minerStatus={result.MinerStatus}");

                ApplyPlatformMiningResult(result.Twitch, Platform.Twitch);
                ApplyPlatformMiningResult(result.Kick, Platform.Kick);

                StartStreamHealthMonitoring();

                _liveProgressTimer?.Start();
                _progressVerifyTimer.Start();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(45));
                    ScheduleServerProgressVerification();
                });

                DateTime nextCheckAt = result.NextCheckAt;
                double delayMs = Math.Max((nextCheckAt - DateTime.Now).TotalMilliseconds, 60000);
                _recheckTimer = new System.Timers.Timer(delayMs);
                _recheckTimer.Elapsed += async (_, _) =>
                {
                    _recheckTimer?.Stop();
                    AppLogger.Debug("Miner", "[DropsInventoryManager] Re-evaluating streams for active campaigns.");
                    AppLogger.Debug("Miner", "Scheduled re-evaluation triggered.");
                    await StartMiningStreams(true);
                };
                _recheckTimer.AutoReset = false;
                _recheckTimer.Start();

                AppLogger.Debug("Miner", $"[DropsInventoryManager] Next stream re-evaluation in ~{delayMs / 60000:F1} minutes at {nextCheckAt:u}");
                AppLogger.Debug("Miner", $"Next re-evaluation in {delayMs / 1000:F0}s at {nextCheckAt:u}. twitchSelected={_selection.CurrentTwitchCampaign != null}, kickSelected={_selection.CurrentKickCampaign != null}");

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
            {
                if (platform == Platform.Twitch)
                    AppLogger.Warn("TwitchMining", "ApplyPlatformMiningResult Twitch=null - no stream will be watched.");
                return;
            }

            MiningBaseline baseline = result.Baseline;
            PlatformProgressState progress = platform == Platform.Twitch ? _twitchProgress : _kickProgress;

            switch (platform)
            {
                case Platform.Twitch:
                    _selection.CurrentTwitchCampaign = result.Campaign;
                    _currentTwitchLogin = result.Login;
                    _lastKnownTwitchOnlineState = true;
                    UpdateTwitchMinedChannelWatcher(result.Login);
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
        /// Debounces a background server progress verification pass for currently mined campaigns.
        /// </summary>
        private void ScheduleServerProgressVerification()
        {
            if (_isPaused || !_liveProgressTimer.Enabled)
            {
                AppLogger.Debug("ProgressSync", $"Schedule SKIP - paused={_isPaused}, liveProgressTimerEnabled={_liveProgressTimer.Enabled}.");
                return;
            }

            if (Interlocked.CompareExchange(ref _progressVerifyScheduled, 1, 0) != 0)
            {
                AppLogger.Debug("ProgressSync", "Schedule SKIP - a verification pass is already in flight.");
                return;
            }

            AppLogger.Debug("ProgressSync", "Schedule OK - dispatching VerifyServerProgressAsync.");
            _ = Application.Current.Dispatcher.InvokeAsync(VerifyServerProgressAsync);
        }

        /// <summary>
        /// Fetches server progress for the currently mined campaigns and realigns local counters when they differ.
        /// </summary>
        /// <remarks>Runs on the UI dispatcher. Uses non-navigating fetch paths so mining WebViews stay on stream.</remarks>
        private async Task VerifyServerProgressAsync()
        {
            try
            {
                await _progressVerifyLock.WaitAsync();
                if (_isPaused || !_liveProgressTimer.Enabled)
                {
                    AppLogger.Debug("ProgressSync", $"Verify SKIP - paused={_isPaused}, liveProgressTimerEnabled={_liveProgressTimer.Enabled}.");
                    return;
                }

                DropsCampaign? twitchCampaign = _selection.CurrentTwitchCampaign;
                DropsCampaign? kickCampaign = _selection.CurrentKickCampaign;
                if (twitchCampaign == null && kickCampaign == null)
                {
                    AppLogger.Debug("ProgressSync", "Verify SKIP - no campaign currently selected on either platform.");
                    return;
                }

                AppLogger.Debug(
                    "ProgressSync",
                    $"Verify START twitchCampaignId={twitchCampaign?.Id ?? "null"} kickCampaignId={kickCampaign?.Id ?? "null"} " +
                    $"twitchGqlAvailable={_twitchGqlService != null} kickWebViewAvailable={KickWebView != null}");

                IReadOnlyDictionary<string, IReadOnlyList<DropsReward>> twitchProgress = new Dictionary<string, IReadOnlyList<DropsReward>>();
                IReadOnlyDictionary<string, IReadOnlyList<DropsReward>> kickProgress = new Dictionary<string, IReadOnlyList<DropsReward>>();

                if (twitchCampaign != null && _twitchGqlService != null)
                    twitchProgress = await ServerProgressFetcher.FetchTwitchRewardProgressAsync(_twitchGqlService, [twitchCampaign]);
                else if (twitchCampaign != null)
                    AppLogger.Debug("ProgressSync", "Twitch fetch SKIP - no Twitch GQL service available.");

                if (kickCampaign != null && KickWebView != null)
                    kickProgress = await ServerProgressFetcher.FetchKickRewardProgressAsync(KickWebView, [kickCampaign]);
                else if (kickCampaign != null)
                    AppLogger.Debug("ProgressSync", "Kick fetch SKIP - KickWebView is null.");

                AppLogger.Debug(
                    "ProgressSync",
                    $"Fetch results twitchEntries={twitchProgress.Count} kickEntries={kickProgress.Count}");

                bool twitchChanged = false;
                bool kickChanged = false;

                if (twitchCampaign != null)
                {
                    if (twitchProgress.TryGetValue(twitchCampaign.Id, out IReadOnlyList<DropsReward>? twitchRewards))
                    {
                        if (MiningProgressReconciler.TryReconcile(twitchCampaign, twitchRewards, _twitchProgress, out DropsCampaign updatedTwitch))
                            twitchChanged = ApplyReconciledCampaign(updatedTwitch, Platform.Twitch);
                        else
                            AppLogger.Debug("ProgressSync", $"Twitch reconcile found no drift for campaignId={twitchCampaign.Id}.");
                    }
                    else
                    {
                        AppLogger.Debug("ProgressSync", $"Twitch campaignId={twitchCampaign.Id} not present in server response (entries={twitchProgress.Count}).");
                    }
                }

                if (kickCampaign != null)
                {
                    if (kickProgress.TryGetValue(kickCampaign.Id, out IReadOnlyList<DropsReward>? kickRewards))
                    {
                        if (MiningProgressReconciler.TryReconcile(kickCampaign, kickRewards, _kickProgress, out DropsCampaign updatedKick))
                            kickChanged = ApplyReconciledCampaign(updatedKick, Platform.Kick);
                        else
                            AppLogger.Debug("ProgressSync", $"Kick reconcile found no drift for campaignId={kickCampaign.Id}.");
                    }
                    else
                    {
                        AppLogger.Debug("ProgressSync", $"Kick campaignId={kickCampaign.Id} not present in server response (entries={kickProgress.Count}).");
                    }
                }

                if (twitchChanged || kickChanged)
                    AppLogger.Info("ProgressSync", "Progress synced with server.");
                else
                    AppLogger.Debug("ProgressSync", "Verify DONE - no changes applied.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ProgressSync", $"Server progress verification failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _progressVerifyScheduled, 0);
                _progressVerifyLock.Release();
            }
        }

        /// <summary>
        /// Writes a reconciled campaign into <see cref="ActiveCampaigns"/> and raises updated progress events.
        /// </summary>
        /// <param name="updatedCampaign">Campaign with server-aligned reward progress.</param>
        /// <param name="platform">Platform whose progress events should be raised.</param>
        /// <returns><see langword="true"/> when the campaign was found and applied to the active inventory.</returns>
        private bool ApplyReconciledCampaign(DropsCampaign updatedCampaign, Platform platform)
        {
            bool applied = false;

            Application.Current.Dispatcher.Invoke(() =>
            {
                DropsCampaign? existing = ActiveCampaigns.FirstOrDefault(c => c.Platform == platform && c.Id == updatedCampaign.Id);
                if (existing == null)
                    return;

                int index = ActiveCampaigns.IndexOf(existing);
                if (index < 0)
                    return;

                ActiveCampaigns[index] = updatedCampaign;

                if (platform == Platform.Twitch && _selection.CurrentTwitchCampaign?.Id == updatedCampaign.Id)
                    _selection.CurrentTwitchCampaign = updatedCampaign;
                else if (platform == Platform.Kick && _selection.CurrentKickCampaign?.Id == updatedCampaign.Id)
                    _selection.CurrentKickCampaign = updatedCampaign;

                _campaignUpdater.UpdateSelectionFlags(ActiveCampaigns, _selection);
                applied = true;
            });

            if (!applied)
                return false;

            PlatformProgressState progress = platform == Platform.Twitch ? _twitchProgress : _kickProgress;
            byte campPct = MiningProgressCalculator.CalculateLiveCampaignProgress(updatedCampaign);
            byte dropPct = MiningProgressCalculator.CalculateLiveDropProgress(updatedCampaign, progress.DropMinedSeconds);

            if (platform == Platform.Twitch)
                TwitchProgressChanged?.Invoke(campPct, dropPct);
            else
                KickProgressChanged?.Invoke(campPct, dropPct);

            return true;
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
                    && await _twitchLiveChannelApi.IsChannelEligibleAsync(_currentTwitchLogin, _selection.CurrentTwitchCampaign),
                IsKickEligibleAsync = async () => _selection.CurrentKickCampaign != null
                    && !string.IsNullOrWhiteSpace(_currentKickLogin)
                    && await _kickLiveChannelApi.IsChannelEligibleAsync(_currentKickLogin, _selection.CurrentKickCampaign),
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

            if (result.Campaign is DropsCampaign picked)
            {
                AppLogger.Debug(
                    "TwitchMining",
                    $"SelectBestCampaign picked='{picked.Name}' platform={picked.Platform} slug='{picked.Slug}' " +
                    $"from {campaigns.Count} candidates mode={UISettingsManager.Instance.MiningPriorityMode}");
            }
            else
            {
                AppLogger.Warn("TwitchMining", $"SelectBestCampaign returned null from {campaigns.Count} candidates.");
            }

            return Task.FromResult(result.Campaign);
        }

    }
}