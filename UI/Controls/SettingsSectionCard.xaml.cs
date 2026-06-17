using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;
using System.Windows;

namespace UI.Controls
{
    [ContentProperty(nameof(SectionContent))]
    public partial class SettingsSectionCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingsSectionCard), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SectionContentProperty =
            DependencyProperty.Register(nameof(SectionContent), typeof(object), typeof(SettingsSectionCard), new PropertyMetadata(null));

        public static readonly DependencyProperty SectionMarginProperty =
            DependencyProperty.Register(nameof(SectionMargin), typeof(Thickness), typeof(SettingsSectionCard),
                new PropertyMetadata(new Thickness(0, 0, 0, 24)));

        public static readonly DependencyProperty SectionOpacityProperty =
            DependencyProperty.Register(nameof(SectionOpacity), typeof(double), typeof(SettingsSectionCard), new PropertyMetadata(1.0));

        public static readonly DependencyProperty IsDangerProperty =
            DependencyProperty.Register(nameof(IsDanger), typeof(bool), typeof(SettingsSectionCard), new PropertyMetadata(false));

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public object SectionContent
        {
            get => GetValue(SectionContentProperty);
            set => SetValue(SectionContentProperty, value);
        }

        public Thickness SectionMargin
        {
            get => (Thickness)GetValue(SectionMarginProperty);
            set => SetValue(SectionMarginProperty, value);
        }

        public double SectionOpacity
        {
            get => (double)GetValue(SectionOpacityProperty);
            set => SetValue(SectionOpacityProperty, value);
        }

        public bool IsDanger
        {
            get => (bool)GetValue(IsDangerProperty);
            set => SetValue(IsDangerProperty, value);
        }

        public SettingsSectionCard()
        {
            InitializeComponent();
        }
    }
}