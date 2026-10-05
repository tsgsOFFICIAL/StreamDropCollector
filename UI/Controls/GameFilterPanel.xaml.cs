using UserControl = System.Windows.Controls.UserControl;
using TextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;
using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Core.Models;

namespace UI.Controls
{
    /// <summary>
    /// Panel for filtering campaigns by game whitelist or blacklist per platform.
    /// </summary>
    public partial class GameFilterPanel : UserControl
    {
        /// <summary>Identifies the <see cref="ClearClick"/> routed event.</summary>
        public static readonly RoutedEvent ClearClickEvent =
            EventManager.RegisterRoutedEvent(nameof(ClearClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GameFilterPanel));

        /// <summary>Identifies the <see cref="PlatformTitle"/> dependency property.</summary>
        public static readonly DependencyProperty PlatformTitleProperty =
            DependencyProperty.Register(nameof(PlatformTitle), typeof(string), typeof(GameFilterPanel), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="WhitelistSummary"/> dependency property.</summary>
        public static readonly DependencyProperty WhitelistSummaryProperty =
            DependencyProperty.Register(nameof(WhitelistSummary), typeof(string), typeof(GameFilterPanel), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="GameOptions"/> dependency property.</summary>
        public static readonly DependencyProperty GameOptionsProperty =
            DependencyProperty.Register(
                nameof(GameOptions), typeof(IEnumerable), typeof(GameFilterPanel),
                new PropertyMetadata(null, (d, _) => ((GameFilterPanel)d).RebuildView()));

        /// <summary>Identifies the <see cref="IsBlacklistMode"/> dependency property.</summary>
        public static readonly DependencyProperty IsBlacklistModeProperty =
            DependencyProperty.Register(nameof(IsBlacklistMode), typeof(bool), typeof(GameFilterPanel), new PropertyMetadata(false));

        /// <summary>Identifies the <see cref="ClearButtonText"/> dependency property.</summary>
        public static readonly DependencyProperty ClearButtonTextProperty =
            DependencyProperty.Register(nameof(ClearButtonText), typeof(string), typeof(GameFilterPanel), new PropertyMetadata("Clear whitelist"));

        /// <summary>Identifies the <see cref="ClearButtonMargin"/> dependency property.</summary>
        public static readonly DependencyProperty ClearButtonMarginProperty =
            DependencyProperty.Register(nameof(ClearButtonMargin), typeof(Thickness), typeof(GameFilterPanel), new PropertyMetadata(default(Thickness)));

        /// <summary>Raised when the user clicks the clear filter button.</summary>
        public event RoutedEventHandler ClearClick
        {
            add => AddHandler(ClearClickEvent, value);
            remove => RemoveHandler(ClearClickEvent, value);
        }

        /// <summary>Platform name shown in the panel header.</summary>
        public string PlatformTitle
        {
            get => (string)GetValue(PlatformTitleProperty);
            set => SetValue(PlatformTitleProperty, value);
        }

        /// <summary>Summary text describing the current whitelist or blacklist.</summary>
        public string WhitelistSummary
        {
            get => (string)GetValue(WhitelistSummaryProperty);
            set => SetValue(WhitelistSummaryProperty, value);
        }

        /// <summary>Selectable game options bound to the filter list.</summary>
        public IEnumerable? GameOptions
        {
            get => (IEnumerable?)GetValue(GameOptionsProperty);
            set => SetValue(GameOptionsProperty, value);
        }

        /// <summary>When <see langword="true"/>, the panel operates in blacklist mode instead of whitelist mode.</summary>
        public bool IsBlacklistMode
        {
            get => (bool)GetValue(IsBlacklistModeProperty);
            set => SetValue(IsBlacklistModeProperty, value);
        }

        /// <summary>Label text for the clear filter button.</summary>
        public string ClearButtonText
        {
            get => (string)GetValue(ClearButtonTextProperty);
            set => SetValue(ClearButtonTextProperty, value);
        }

        /// <summary>Margin applied to the clear filter button.</summary>
        public Thickness ClearButtonMargin
        {
            get => (Thickness)GetValue(ClearButtonMarginProperty);
            set => SetValue(ClearButtonMarginProperty, value);
        }

        /// <summary>Initializes the game filter panel.</summary>
        public GameFilterPanel()
        {
            InitializeComponent();
        }

        private ICollectionView? _view;

        private void RebuildView()
        {
            // A private view, so filtering here does not affect other consumers of the same collection.
            _view = GameOptions is null ? null : new CollectionViewSource { Source = GameOptions }.View;

            if (_view != null)
                _view.Filter = MatchesSearch;

            OptionsList.ItemsSource = _view;
        }

        private bool MatchesSearch(object item)
        {
            string query = SearchBox.Text.Trim();
            return query.Length == 0
                || item is not GameFilterOption option
                || option.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

        private void OnClearClick(object sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(ClearClickEvent, this));
    }
}