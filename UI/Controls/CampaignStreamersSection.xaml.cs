using UserControl = System.Windows.Controls.UserControl;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using Core.Models;
using UI.Helpers;
using UI.Models;

namespace UI.Controls
{
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

        public ObservableCollection<EligibleStreamer> PreviewChips { get; } = [];
        public ObservableCollection<EligibleStreamer> FilteredStreamers { get; } = [];

        public bool HasStreamers => _allStreamers.Count > 0;
        public bool NeedsCollapse => _allStreamers.Count > InlineLimit;
        public bool ShowExpandedPanel => NeedsCollapse;

        public bool ShowsLiveUi => HasStreamers;

        public int TotalCount => _allStreamers.Count;

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

        public bool FilterAllActive
        {
            get => !_filterLiveOnly;
            set => FilterLiveOnly = !value;
        }

        public int HiddenCount => Math.Max(0, TotalCount - CompactChipCount);

        public string MoreButtonText => IsExpanded ? "Show less" : $"+{HiddenCount} more";

        public string MoreButtonTooltip => IsExpanded
            ? "Collapse streamer list"
            : $"Show all {TotalCount} streamers";

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

        public bool ShowEmptyFilteredMessage => IsExpanded && FilteredStreamers.Count == 0;

        public event PropertyChangedEventHandler? PropertyChanged;

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
        /// Apply channel metadata from Kick's <c>/api/v2/channels/{slug}</c> response
        /// (<c>user.profile_pic</c>) once the live/channel API layer is wired up.
        /// </summary>
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