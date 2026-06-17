using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;
using System.Windows;

namespace UI.Controls
{
    [ContentProperty(nameof(SectionContent))]
    public partial class HelpSectionCard : UserControl
    {
        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(HelpSectionCard), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SectionContentProperty =
            DependencyProperty.Register(nameof(SectionContent), typeof(object), typeof(HelpSectionCard), new PropertyMetadata(null));

        public static readonly DependencyProperty SectionMarginProperty =
         DependencyProperty.Register(
                nameof(SectionMargin),
                typeof(Thickness),
                typeof(HelpSectionCard),
                new PropertyMetadata(new Thickness(0, 0, 0, 32))
            );

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

        public HelpSectionCard()
        {
            InitializeComponent();
        }
    }
}