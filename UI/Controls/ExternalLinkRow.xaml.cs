using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Navigation;
using System.Windows;

namespace UI.Controls
{
    public partial class ExternalLinkRow : UserControl
    {
        public static readonly DependencyProperty IconSourceProperty =
            DependencyProperty.Register(nameof(IconSource), typeof(string), typeof(ExternalLinkRow), new PropertyMetadata(null));

        public static readonly DependencyProperty LinkTextProperty =
            DependencyProperty.Register(nameof(LinkText), typeof(string), typeof(ExternalLinkRow), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty NavigateUriProperty =
            DependencyProperty.Register(nameof(NavigateUri), typeof(string), typeof(ExternalLinkRow), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty RowMarginProperty =
            DependencyProperty.Register(nameof(RowMargin), typeof(Thickness), typeof(ExternalLinkRow), new PropertyMetadata(default(Thickness)));

        public string IconSource
        {
            get => (string)GetValue(IconSourceProperty);
            set => SetValue(IconSourceProperty, value);
        }

        public string LinkText
        {
            get => (string)GetValue(LinkTextProperty);
            set => SetValue(LinkTextProperty, value);
        }

        public string NavigateUri
        {
            get => (string)GetValue(NavigateUriProperty);
            set => SetValue(NavigateUriProperty, value);
        }

        public Thickness RowMargin
        {
            get => (Thickness)GetValue(RowMarginProperty);
            set => SetValue(RowMarginProperty, value);
        }

        public ExternalLinkRow()
        {
            InitializeComponent();
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Core.Utility.LaunchWeb(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }
}