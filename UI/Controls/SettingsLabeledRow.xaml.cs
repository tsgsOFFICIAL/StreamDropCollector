using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;
using System.Windows;

namespace UI.Controls
{
    [ContentProperty(nameof(RowContent))]
    public partial class SettingsLabeledRow : UserControl
    {
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingsLabeledRow), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty LabelWidthProperty =
            DependencyProperty.Register(nameof(LabelWidth), typeof(double), typeof(SettingsLabeledRow), new PropertyMetadata(140.0));

        public static readonly DependencyProperty RowMarginProperty =
            DependencyProperty.Register(nameof(RowMargin), typeof(Thickness), typeof(SettingsLabeledRow), new PropertyMetadata(default(Thickness)));

        public static readonly DependencyProperty RowContentProperty =
            DependencyProperty.Register(nameof(RowContent), typeof(object), typeof(SettingsLabeledRow), new PropertyMetadata(null));

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public double LabelWidth
        {
            get => (double)GetValue(LabelWidthProperty);
            set => SetValue(LabelWidthProperty, value);
        }

        public Thickness RowMargin
        {
            get => (Thickness)GetValue(RowMarginProperty);
            set => SetValue(RowMarginProperty, value);
        }

        public object RowContent
        {
            get => GetValue(RowContentProperty);
            set => SetValue(RowContentProperty, value);
        }

        public SettingsLabeledRow()
        {
            InitializeComponent();
        }
    }
}