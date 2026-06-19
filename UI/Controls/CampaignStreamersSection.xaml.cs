using UserControl = System.Windows.Controls.UserControl;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using Core.Helpers;
using Core.Models;
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
            }
        }

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
            DataContextChanged += OnSectionDataContextChanged;
        }

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
        /// Applies profile image URLs from platform channel metadata once the live/channel API layer is wired up.
        /// </summary>
        /// <param name="profileImagesByLogin">Profile image URLs keyed by channel login.</param>
        public void ApplyProfileImages(IReadOnlyDictionary<string, string?> profileImagesByLogin)
        {
            foreach (EligibleStreamer streamer in _allStreamers)
            {
                if (profileImagesByLogin.TryGetValue(streamer.Login, out string? profileImageUrl))
                    streamer.ProfileImageUrl = profileImageUrl;
            }
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

            if (campaign != null)
            {
                foreach (string login in EligibleStreamerParser.ParseChannelLogins(campaign))
                    _allStreamers.Add(new EligibleStreamer(login));
            }

            OnPropertyChanged(nameof(HasStreamers));
            OnPropertyChanged(nameof(NeedsCollapse));
            OnPropertyChanged(nameof(ShowExpandedPanel));
            OnPropertyChanged(nameof(ShowsLiveUi));
            OnPropertyChanged(nameof(TotalCount));
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

        private void UpdateClosedFooter()
        {
            if (!ShowExpandedPanel)
            {
                FooterText = string.Empty;
                return;
            }

            FooterText = $"{TotalCount} eligible · {LiveCount} live - expand to browse";
        }

        private void UpdateExpandedFooter(string query)
        {
            string suffix = string.IsNullOrEmpty(query) ? string.Empty : $" matching \"{query}\"";

            if (FilterLiveOnly)
            {
                int totalLive = _allStreamers.Count(s => s.IsLive);
                FooterText =
                    $"Showing {FilteredStreamers.Count} of {totalLive} live{suffix} · {TotalCount} total eligible";
            }
            else
            {
                FooterText =
                    $"Showing {FilteredStreamers.Count} of {TotalCount} eligible{suffix} ({LiveCount} live)";
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