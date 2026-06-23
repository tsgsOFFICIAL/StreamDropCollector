using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;
using System.Windows;

namespace UI.Controls
{
    /// <summary>
    /// Card container for a titled settings section with optional danger styling.
    /// </summary>
    [ContentProperty(nameof(SectionContent))]
    public partial class SettingsSectionCard : UserControl
    {
        /// <summary>Identifies the <see cref="Title"/> dependency property.</summary>
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingsSectionCard), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="SectionContent"/> dependency property.</summary>
        public static readonly DependencyProperty SectionContentProperty =
            DependencyProperty.Register(nameof(SectionContent), typeof(object), typeof(SettingsSectionCard), new PropertyMetadata(null));

        /// <summary>Identifies the <see cref="SectionMargin"/> dependency property.</summary>
        public static readonly DependencyProperty SectionMarginProperty =
            DependencyProperty.Register(nameof(SectionMargin), typeof(Thickness), typeof(SettingsSectionCard),
                new PropertyMetadata(new Thickness(0, 0, 0, 24)));

        /// <summary>Identifies the <see cref="SectionOpacity"/> dependency property.</summary>
        public static readonly DependencyProperty SectionOpacityProperty =
            DependencyProperty.Register(nameof(SectionOpacity), typeof(double), typeof(SettingsSectionCard), new PropertyMetadata(1.0));

        /// <summary>Identifies the <see cref="IsDanger"/> dependency property.</summary>
        public static readonly DependencyProperty IsDangerProperty =
            DependencyProperty.Register(nameof(IsDanger), typeof(bool), typeof(SettingsSectionCard), new PropertyMetadata(false));

        /// <summary>Section heading text.</summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>Child content hosted inside the section card.</summary>
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

        /// <summary>Opacity applied to the section container.</summary>
        public double SectionOpacity
        {
            get => (double)GetValue(SectionOpacityProperty);
            set => SetValue(SectionOpacityProperty, value);
        }

        /// <summary>When <see langword="true"/>, applies danger styling to the section.</summary>
        public bool IsDanger
        {
            get => (bool)GetValue(IsDangerProperty);
            set => SetValue(IsDangerProperty, value);
        }

        /// <summary>Initializes the settings section card.</summary>
        public SettingsSectionCard()
        {
            InitializeComponent();
        }
    }
}