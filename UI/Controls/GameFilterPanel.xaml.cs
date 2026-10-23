using UserControl = System.Windows.Controls.UserControl;
using TextChangedEventArgs = System.Windows.Controls.TextChangedEventArgs;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using Core.Models;

namespace UI.Controls
{
    /// <summary>Handles <see cref="GameFilterPanel.PriorityMove"/>.</summary>
    public delegate void PriorityMoveEventHandler(object sender, PriorityMoveEventArgs e);

    /// <summary>Describes a request to move a game within the priority order.</summary>
    public sealed class PriorityMoveEventArgs(RoutedEvent routedEvent, object source, GameFilterOption option, int offset)
        : RoutedEventArgs(routedEvent, source)
    {
        /// <summary>The game to move.</summary>
        public GameFilterOption Option { get; } = option;

        /// <summary>-1 to raise the priority by one place, 1 to lower it.</summary>
        public int Offset { get; } = offset;
    }

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
            DependencyProperty.Register(
                nameof(IsBlacklistMode), typeof(bool), typeof(GameFilterPanel),
                new PropertyMetadata(false, (d, _) => ((GameFilterPanel)d).UpdatePriorityVisibility()));

        /// <summary>Identifies the <see cref="PriorityOptions"/> dependency property.</summary>
        public static readonly DependencyProperty PriorityOptionsProperty =
            DependencyProperty.Register(
                nameof(PriorityOptions), typeof(IEnumerable), typeof(GameFilterPanel),
                new PropertyMetadata(null, (d, e) => ((GameFilterPanel)d).OnPriorityOptionsChanged(e)));

        /// <summary>Identifies the <see cref="PriorityMove"/> routed event.</summary>
        public static readonly RoutedEvent PriorityMoveEvent =
            EventManager.RegisterRoutedEvent(nameof(PriorityMove), RoutingStrategy.Bubble, typeof(PriorityMoveEventHandler), typeof(GameFilterPanel));

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

        /// <summary>Selected games in mining priority order.</summary>
        public IEnumerable? PriorityOptions
        {
            get => (IEnumerable?)GetValue(PriorityOptionsProperty);
            set => SetValue(PriorityOptionsProperty, value);
        }

        /// <summary>Raised when the user moves a game up or down the priority order.</summary>
        public event PriorityMoveEventHandler PriorityMove
        {
            add => AddHandler(PriorityMoveEvent, value);
            remove => RemoveHandler(PriorityMoveEvent, value);
        }

        private void OnPriorityOptionsChanged(DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
                oldCollection.CollectionChanged -= OnPriorityCollectionChanged;

            if (e.NewValue is INotifyCollectionChanged newCollection)
                newCollection.CollectionChanged += OnPriorityCollectionChanged;

            PriorityList.ItemsSource = PriorityOptions;
            UpdatePriorityVisibility();
        }

        private void OnPriorityCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdatePriorityVisibility();

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

        private void OnMoveUpClick(object sender, RoutedEventArgs e) => RaiseMove(-1);

        private void OnMoveDownClick(object sender, RoutedEventArgs e) => RaiseMove(1);

        private void RaiseMove(int offset)
        {
            if (PriorityList.SelectedItem is not GameFilterOption option)
                return;

            RaiseEvent(new PriorityMoveEventArgs(PriorityMoveEvent, this, option, offset));
            PriorityList.SelectedItem = option;
        }

        private void UpdatePriorityVisibility() =>
            PriorityPanel.Visibility = !IsBlacklistMode && PriorityOptions?.Cast<object>().Skip(1).Any() == true
                ? Visibility.Visible
                : Visibility.Collapsed;
    }
}