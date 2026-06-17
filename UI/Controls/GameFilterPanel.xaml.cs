using UserControl = System.Windows.Controls.UserControl;
using System.Collections;
using System.Windows;

namespace UI.Controls
{
    public partial class GameFilterPanel : UserControl
    {
        public static readonly RoutedEvent ClearClickEvent =
            EventManager.RegisterRoutedEvent(nameof(ClearClick), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GameFilterPanel));

        public static readonly DependencyProperty PlatformTitleProperty =
            DependencyProperty.Register(nameof(PlatformTitle), typeof(string), typeof(GameFilterPanel), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty WhitelistSummaryProperty =
            DependencyProperty.Register(nameof(WhitelistSummary), typeof(string), typeof(GameFilterPanel), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty GameOptionsProperty =
            DependencyProperty.Register(nameof(GameOptions), typeof(IEnumerable), typeof(GameFilterPanel), new PropertyMetadata(null));

        public static readonly DependencyProperty IsBlacklistModeProperty =
            DependencyProperty.Register(nameof(IsBlacklistMode), typeof(bool), typeof(GameFilterPanel), new PropertyMetadata(false));

        public static readonly DependencyProperty ClearButtonTextProperty =
            DependencyProperty.Register(nameof(ClearButtonText), typeof(string), typeof(GameFilterPanel), new PropertyMetadata("Clear whitelist"));

        public static readonly DependencyProperty ClearButtonMarginProperty =
            DependencyProperty.Register(nameof(ClearButtonMargin), typeof(Thickness), typeof(GameFilterPanel), new PropertyMetadata(default(Thickness)));

        public event RoutedEventHandler ClearClick
        {
            add => AddHandler(ClearClickEvent, value);
            remove => RemoveHandler(ClearClickEvent, value);
        }

        public string PlatformTitle
        {
            get => (string)GetValue(PlatformTitleProperty);
            set => SetValue(PlatformTitleProperty, value);
        }

        public string WhitelistSummary
        {
            get => (string)GetValue(WhitelistSummaryProperty);
            set => SetValue(WhitelistSummaryProperty, value);
        }

        public IEnumerable? GameOptions
        {
            get => (IEnumerable?)GetValue(GameOptionsProperty);
            set => SetValue(GameOptionsProperty, value);
        }

        public bool IsBlacklistMode
        {
            get => (bool)GetValue(IsBlacklistModeProperty);
            set => SetValue(IsBlacklistModeProperty, value);
        }

        public string ClearButtonText
        {
            get => (string)GetValue(ClearButtonTextProperty);
            set => SetValue(ClearButtonTextProperty, value);
        }

        public Thickness ClearButtonMargin
        {
            get => (Thickness)GetValue(ClearButtonMarginProperty);
            set => SetValue(ClearButtonMarginProperty, value);
        }

        public GameFilterPanel()
        {
            InitializeComponent();
        }

        private void OnClearClick(object sender, RoutedEventArgs e) =>
            RaiseEvent(new RoutedEventArgs(ClearClickEvent, this));
    }
}