using UserControl = System.Windows.Controls.UserControl;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using Core.Helpers;
using Core.Managers;
using Core.Models;
using Core.Enums;
using UI.Models;

namespace UI.Controls
{
    /// <summary>
    /// Expandable eligible-streamer list for a drops campaign with live filtering and search.
    /// </summary>
    public partial class CampaignStreamersSection : UserControl, INotifyPropertyChanged
    {
        private const int InlineLimit = 8;
        private const int CompactChipCount = 5;

        private readonly List<EligibleStreamer> _allStreamers = [];
        private DropsCampaign? _campaign;
        private bool _isExpanded;
        private bool _filterLiveOnly = true;
        private string _searchQuery = string.Empty;
        private int _liveCount;
        private string _footerText = string.Empty;

        /// <summary>Preview chips shown when the list is collapsed or partially expanded.</summary>
        public ObservableCollection<EligibleStreamer> PreviewChips { get; } = [];

        /// <summary>Streamers matching the current filter and search query.</summary>
        public ObservableCollection<EligibleStreamer> FilteredStreamers { get; } = [];

        /// <summary>Whether the campaign has at least one eligible streamer.</summary>
        public bool HasStreamers => _allStreamers.Count > 0;

        private bool _discoveryFinished;

        /// <summary>Whether a general-drop campaign is still waiting for its streamer list to be discovered.</summary>
        public bool IsDiscovering =>
            !_discoveryFinished
            && _allStreamers.Count == 0
            && _campaign is { IsGeneralDrop: true, Platform: Platform.Kick or Platform.Twitch };

        /// <summary>Whether the section is shown at all (streamers found, or still looking for them).</summary>
        public bool ShowSection => HasStreamers || IsDiscovering;

        private void NotifyDiscoveryChanged()
        {
            OnPropertyChanged(nameof(IsDiscovering));
            OnPropertyChanged(nameof(ShowSection));
        }

        /// <summary>Whether the streamer count exceeds the inline display limit.</summary>
        public bool NeedsCollapse => _allStreamers.Count > InlineLimit;

        /// <summary>Whether the full expandable panel should be shown.</summary>
        public bool ShowExpandedPanel => NeedsCollapse;

        /// <summary>Whether live-state UI elements should be visible.</summary>
        public bool ShowsLiveUi => HasStreamers;

        /// <summary>Total number of eligible streamers for the campaign.</summary>
        public int TotalCount => _allStreamers.Count;

        /// <summary>Number of streamers currently marked as live.</summary>
        public int LiveCount
        {
            get => _liveCount;
            private set
            {
                if (_liveCount == value)
                    return;

                _liveCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LiveCountText));
            }
        }

        private bool _liveStatusKnown;

        /// <summary>The live count, or "?" while the first live-status scan for this campaign is still running.</summary>
        public string LiveCountText => _liveStatusKnown ? LiveCount.ToString() : "?";

        /// <summary>Whether the full streamer list panel is expanded.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value)
                    return;

                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MoreButtonText));
                OnPropertyChanged(nameof(MoreButtonTooltip));
                RefreshPreview();
                if (_isExpanded)
                    RefreshFiltered();
                else
                    SearchQuery = string.Empty;
            }
        }

        /// <summary>Search text used to filter streamers by name.</summary>
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery == value)
                    return;

                _searchQuery = value;
                OnPropertyChanged();
                RefreshFiltered();
            }
        }

        /// <summary>When <see langword="true"/>, only live streamers are shown in the expanded list.</summary>
        public bool FilterLiveOnly
        {
            get => _filterLiveOnly;
            set
            {
                if (_filterLiveOnly == value)
                    return;

                _filterLiveOnly = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FilterAllActive));
                RefreshFiltered();
            }
        }

        /// <summary>Binding-friendly inverse of <see cref="FilterLiveOnly"/> for the "All" filter toggle.</summary>
        public bool FilterAllActive
        {
            get => !_filterLiveOnly;
            set => FilterLiveOnly = !value;
        }

        /// <summary>Number of streamers hidden behind the collapsed "+N more" affordance.</summary>
        public int HiddenCount => Math.Max(0, TotalCount - CompactChipCount);

        /// <summary>Label for the expand/collapse toggle button.</summary>
        public string MoreButtonText => IsExpanded ? "Show less" : $"+{HiddenCount} more";

        /// <summary>Tooltip describing the expand/collapse action.</summary>
        public string MoreButtonTooltip => IsExpanded
            ? "Collapse streamer list"
            : $"Show all {TotalCount} streamers";

        /// <summary>Footer summary text for collapsed or expanded list states.</summary>
        public string FooterText
        {
            get => _footerText;
            private set
            {
                if (_footerText == value)
                    return;

                _footerText = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Whether to show the empty-state message when filters match no streamers.</summary>
        public bool ShowEmptyFilteredMessage => IsExpanded && FilteredStreamers.Count == 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Initializes the campaign streamers section and watches for campaign data context changes.</summary>
        public CampaignStreamersSection()
        {
            InitializeComponent();
            _engine = UI.Accounts.AccountManager.Instance.SelectedSession?.Engine;
            DataContextChanged += OnSectionDataContextChanged;

            if (_engine != null)
            {
                _engine.KickStreamerMetadataChanged += OnKickStreamerMetadataChanged;
                _engine.TwitchStreamerMetadataChanged += OnTwitchStreamerMetadataChanged;
                _engine.KickGeneralDropDiscoveryCompletedEvent += OnKickGeneralDropDiscoveryCompleted;
                _engine.TwitchGeneralDropDiscoveryCompletedEvent += OnTwitchGeneralDropDiscoveryCompleted;
            }

            Unloaded += (_, _) =>
            {
                if (_engine == null)
                    return;

                _engine.KickStreamerMetadataChanged -= OnKickStreamerMetadataChanged;
                _engine.TwitchStreamerMetadataChanged -= OnTwitchStreamerMetadataChanged;
                _engine.KickGeneralDropDiscoveryCompletedEvent -= OnKickGeneralDropDiscoveryCompleted;
                _engine.TwitchGeneralDropDiscoveryCompletedEvent -= OnTwitchGeneralDropDiscoveryCompleted;
            };
        }

        /// <summary>Mining engine of the account the inventory is showing when this section was created.</summary>
        private readonly DropsInventoryManager? _engine;

        private void OnKickStreamerMetadataChanged(IReadOnlyDictionary<string, LiveChannelSnapshot> snapshots) =>
            OnPlatformStreamerMetadataChanged(Platform.Kick, snapshots);

        private void OnTwitchStreamerMetadataChanged(IReadOnlyDictionary<string, LiveChannelSnapshot> snapshots) =>
            OnPlatformStreamerMetadataChanged(Platform.Twitch, snapshots);

        private void OnPlatformStreamerMetadataChanged(
            Platform platform,
            IReadOnlyDictionary<string, LiveChannelSnapshot> snapshots)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (_campaign?.Platform != platform)
                    return;

                EnsureGeneralDropStreamersLoaded();
                ApplyChannelMetadata(snapshots);
            });
        }

        private void OnKickGeneralDropDiscoveryCompleted() => OnGeneralDropDiscoveryCompleted(Platform.Kick);

        private void OnTwitchGeneralDropDiscoveryCompleted() => OnGeneralDropDiscoveryCompleted(Platform.Twitch);

        private void OnGeneralDropDiscoveryCompleted(Platform platform) =>
            Dispatcher.InvokeAsync(() =>
            {
                if (_campaign?.Platform != platform)
                    return;

                EnsureGeneralDropStreamersLoaded();
                _discoveryFinished = true;
                NotifyDiscoveryChanged();
            });

        private void OnSectionDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is DropsCampaign campaign)
                LoadCampaign(campaign);
        }

        private void OnMoreButtonClick(object sender, RoutedEventArgs e) =>
            IsExpanded = !IsExpanded;

        private void OnPreviewChipsMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (FindDataContext<EligibleStreamer>(e.OriginalSource as DependencyObject) is not { IsClickable: true } streamer)
                return;

            OpenFilteredToStreamer(streamer);
            e.Handled = true;
        }

        private void OnLiveFilterClick(object sender, RoutedEventArgs e) =>
            FilterLiveOnly = true;

        private void OnAllFilterClick(object sender, RoutedEventArgs e) =>
            FilterAllActive = true;

        private void OpenFilteredToStreamer(EligibleStreamer streamer)
        {
            SearchQuery = streamer.Name;
            if (!IsExpanded)
                IsExpanded = true;
            else
                RefreshFiltered();
        }

        /// <summary>
        /// Applies channel metadata (profile image, live state, display name) to eligible streamer chips.
        /// </summary>
        /// <param name="snapshotsByLogin">Channel snapshots keyed by login slug.</param>
        public void ApplyChannelMetadata(IReadOnlyDictionary<string, LiveChannelSnapshot> snapshotsByLogin)
        {
            if (snapshotsByLogin.Count == 0 || _allStreamers.Count == 0)
                return;

            bool anyChanged = false;

            foreach (EligibleStreamer streamer in _allStreamers)
            {
                if (!TryGetSnapshot(snapshotsByLogin, streamer.Login, out LiveChannelSnapshot? snapshot)
                    || snapshot is null)
                    continue;

                streamer.ProfileImageUrl = snapshot.ProfileImageUrl;
                streamer.IsLive = snapshot.IsLive;
                streamer.SetDisplayName(snapshot.DisplayName);
                anyChanged = true;
            }

            if (!anyChanged)
                return;

            _liveStatusKnown = true;
            OnPropertyChanged(nameof(LiveCountText));
            UpdateLiveCount();
            RefreshPreview();
            if (IsExpanded)
                RefreshFiltered();
            else
                UpdateClosedFooter();
        }

        private static bool TryGetSnapshot(
            IReadOnlyDictionary<string, LiveChannelSnapshot> snapshotsByLogin,
            string login,
            out LiveChannelSnapshot? snapshot)
        {
            if (snapshotsByLogin.TryGetValue(login, out snapshot))
                return true;

            return snapshotsByLogin.TryGetValue(login.ToLowerInvariant(), out snapshot);
        }

        private void LoadCampaign(DropsCampaign? campaign)
        {
            _campaign = campaign;
            _allStreamers.Clear();
            PreviewChips.Clear();
            FilteredStreamers.Clear();
            _isExpanded = false;
            _filterLiveOnly = true;
            _searchQuery = string.Empty;
            _liveCount = 0;
            _liveStatusKnown = false;
            // A section created after discovery already finished must not wait for an event that won't come again.
            _discoveryFinished = campaign?.Platform switch
            {
                Platform.Kick => _engine?.KickGeneralDropDiscoveryCompleted ?? false,
                Platform.Twitch => _engine?.TwitchGeneralDropDiscoveryCompleted ?? false,
                _ => false
            };

            if (campaign != null)
            {
                foreach (string login in ResolveEligibleLogins(campaign))
                    _allStreamers.Add(new EligibleStreamer(login));
            }

            if (campaign?.Platform == Platform.Kick && _engine != null)
                ApplyChannelMetadata(_engine.KickStreamerMetadata);
            else if (campaign?.Platform == Platform.Twitch && _engine != null)
                ApplyChannelMetadata(_engine.TwitchStreamerMetadata);

            OnPropertyChanged(nameof(HasStreamers));
            OnPropertyChanged(nameof(NeedsCollapse));
            OnPropertyChanged(nameof(ShowExpandedPanel));
            OnPropertyChanged(nameof(ShowsLiveUi));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(LiveCountText));
            NotifyDiscoveryChanged();
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(SearchQuery));
            OnPropertyChanged(nameof(FilterLiveOnly));
            OnPropertyChanged(nameof(FilterAllActive));
            OnPropertyChanged(nameof(MoreButtonText));
            OnPropertyChanged(nameof(MoreButtonTooltip));
            OnPropertyChanged(nameof(HiddenCount));

            RefreshPreview();
            UpdateClosedFooter();
        }

        private IReadOnlyList<string> ResolveEligibleLogins(DropsCampaign campaign) => campaign.Platform switch
        {
            Platform.Twitch when _engine != null => _engine.GetTwitchEligibleLoginsForCampaign(campaign),
            Platform.Kick when _engine != null => _engine.GetKickEligibleLoginsForCampaign(campaign),
            _ => EligibleStreamerParser.ParseChannelLogins(campaign)
        };

        private void EnsureGeneralDropStreamersLoaded()
        {
            if (_campaign is not { IsGeneralDrop: true, Platform: Platform.Twitch or Platform.Kick })
                return;

            if (_allStreamers.Count > 0)
                return;

            IReadOnlyList<string> logins = ResolveEligibleLogins(_campaign);
            if (logins.Count == 0)
                return;

            foreach (string login in logins)
                _allStreamers.Add(new EligibleStreamer(login));

            OnPropertyChanged(nameof(HasStreamers));
            NotifyDiscoveryChanged();
            OnPropertyChanged(nameof(NeedsCollapse));
            OnPropertyChanged(nameof(ShowExpandedPanel));
            OnPropertyChanged(nameof(ShowsLiveUi));
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(HiddenCount));
            RefreshPreview();
            UpdateClosedFooter();
        }

        private IEnumerable<EligibleStreamer> GetSortedStreamers() =>
            _allStreamers
                .OrderByDescending(s => s.IsLive)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

        private void RefreshPreview()
        {
            PreviewChips.Clear();

            foreach (EligibleStreamer streamer in GetSortedStreamers().Take(NeedsCollapse ? CompactChipCount : TotalCount))
            {
                streamer.IsClickable = NeedsCollapse && !IsExpanded;
                PreviewChips.Add(streamer);
            }

            OnPropertyChanged(nameof(MoreButtonText));
            OnPropertyChanged(nameof(MoreButtonTooltip));
            OnPropertyChanged(nameof(HiddenCount));
        }

        private void RefreshFiltered()
        {
            IEnumerable<EligibleStreamer> list = GetSortedStreamers();

            if (FilterLiveOnly)
                list = list.Where(s => s.IsLive);

            string query = SearchQuery.Trim();
            if (!string.IsNullOrEmpty(query))
                list = list.Where(s => s.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

            FilteredStreamers.Clear();
            foreach (EligibleStreamer streamer in list)
                FilteredStreamers.Add(streamer);

            OnPropertyChanged(nameof(ShowEmptyFilteredMessage));
            UpdateExpandedFooter(query);
        }

        private void UpdateLiveCount() =>
            LiveCount = _allStreamers.Count(s => s.IsLive);

        private void UpdateClosedFooter()
        {
            if (!ShowExpandedPanel)
            {
                FooterText = string.Empty;
                return;
            }

            FooterText = $"{TotalCount} eligible · {LiveCountText} live - expand to browse";
        }

        private void UpdateExpandedFooter(string query)
        {
            string suffix = string.IsNullOrEmpty(query) ? string.Empty : $" matching \"{query}\"";

            if (FilterLiveOnly)
            {
                string totalLive = _liveStatusKnown ? _allStreamers.Count(s => s.IsLive).ToString() : "?";
                FooterText =
                    $"Showing {FilteredStreamers.Count} of {totalLive} live{suffix} · {TotalCount} total eligible";
            }
            else
            {
                FooterText =
                    $"Showing {FilteredStreamers.Count} of {TotalCount} eligible{suffix} ({LiveCountText} live)";
            }
        }

        private static T? FindDataContext<T>(DependencyObject? source) where T : class
        {
            while (source != null)
            {
                if (source is FrameworkElement { DataContext: T match })
                    return match;

                source = VisualTreeHelper.GetParent(source);
            }

            return null;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}