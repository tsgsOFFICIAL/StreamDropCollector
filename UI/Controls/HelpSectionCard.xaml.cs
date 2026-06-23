using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;
using System.Windows;

namespace UI.Controls
{
    /// <summary>
    /// Card container for a titled help section.
    /// </summary>
    [ContentProperty(nameof(SectionContent))]
    public partial class HelpSectionCard : UserControl
    {
        /// <summary>Identifies the <see cref="Title"/> dependency property.</summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(HelpSectionCard), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="SectionContent"/> dependency property.</summary>
        public static readonly DependencyProperty SectionContentProperty =
            DependencyProperty.Register(nameof(SectionContent), typeof(object), typeof(HelpSectionCard), new PropertyMetadata(null));

        /// <summary>Identifies the <see cref="SectionMargin"/> dependency property.</summary>
        public static readonly DependencyProperty SectionMarginProperty =
         DependencyProperty.Register(
                nameof(SectionMargin),
                typeof(Thickness),
                typeof(HelpSectionCard),
                new PropertyMetadata(new Thickness(0, 0, 0, 32))
            );

        /// <summary>Section heading text.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Child content hosted inside the help section card.</summary>
        public object SectionContent
        {
            get => GetValue(SectionContentProperty);
            set => SetValue(SectionContentProperty, value);
        }

        /// <summary>Outer margin applied to the section.</summary>
        public Thickness SectionMargin
        {
            get => (Thickness)GetValue(SectionMarginProperty);
            set => SetValue(SectionMarginProperty, value);
        }

        /// <summary>Initializes the help section card.</summary>
        public HelpSectionCard()
        {
            InitializeComponent();
        }
    }
}