using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Navigation;
using System.Windows;

namespace UI.Controls
{
    /// <summary>
    /// Clickable row with icon and text that opens an external URL in the default browser.
    /// </summary>
    public partial class ExternalLinkRow : UserControl
    {
        /// <summary>Identifies the <see cref="IconSource"/> dependency property.</summary>
        public static readonly DependencyProperty IconSourceProperty =
            DependencyProperty.Register(nameof(IconSource), typeof(string), typeof(ExternalLinkRow), new PropertyMetadata(null));

        /// <summary>Identifies the <see cref="LinkText"/> dependency property.</summary>
        public static readonly DependencyProperty LinkTextProperty =
            DependencyProperty.Register(nameof(LinkText), typeof(string), typeof(ExternalLinkRow), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="NavigateUri"/> dependency property.</summary>
        public static readonly DependencyProperty NavigateUriProperty =
            DependencyProperty.Register(nameof(NavigateUri), typeof(string), typeof(ExternalLinkRow), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="RowMargin"/> dependency property.</summary>
        public static readonly DependencyProperty RowMarginProperty =
            DependencyProperty.Register(nameof(RowMargin), typeof(Thickness), typeof(ExternalLinkRow), new PropertyMetadata(default(Thickness)));

        /// <summary>Pack or file path for the row icon.</summary>
        public string IconSource
        {
            get => (string)GetValue(IconSourceProperty);
            set => SetValue(IconSourceProperty, value);
        }

        /// <summary>Hyperlink display text.</summary>
        public string LinkText
        {
            get => (string)GetValue(LinkTextProperty);
            set => SetValue(LinkTextProperty, value);
        }

        /// <summary>Target URL opened when the link is activated.</summary>
        public string NavigateUri
        {
            get => (string)GetValue(NavigateUriProperty);
            set => SetValue(NavigateUriProperty, value);
        }

        /// <summary>Margin applied to the row container.</summary>
        public Thickness RowMargin
        {
            get => (Thickness)GetValue(RowMarginProperty);
            set => SetValue(RowMarginProperty, value);
        }

        /// <summary>Initializes the external link row.</summary>
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