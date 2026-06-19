using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Markup;
using System.Windows;

namespace UI.Controls
{
    /// <summary>
    /// Settings row with a fixed-width label and arbitrary row content.
    /// </summary>
    [ContentProperty(nameof(RowContent))]
    public partial class SettingsLabeledRow : UserControl
    {
        /// <summary>Identifies the <see cref="Label"/> dependency property.</summary>
        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(SettingsLabeledRow), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="LabelWidth"/> dependency property.</summary>
        public static readonly DependencyProperty LabelWidthProperty =
            DependencyProperty.Register(nameof(LabelWidth), typeof(double), typeof(SettingsLabeledRow), new PropertyMetadata(140.0));

        /// <summary>Identifies the <see cref="RowMargin"/> dependency property.</summary>
        public static readonly DependencyProperty RowMarginProperty =
            DependencyProperty.Register(nameof(RowMargin), typeof(Thickness), typeof(SettingsLabeledRow), new PropertyMetadata(default(Thickness)));

        /// <summary>Identifies the <see cref="RowContent"/> dependency property.</summary>
        public static readonly DependencyProperty RowContentProperty =
            DependencyProperty.Register(nameof(RowContent), typeof(object), typeof(SettingsLabeledRow), new PropertyMetadata(null));

        /// <summary>Label text shown at the start of the row.</summary>
        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        /// <summary>Width reserved for the label column.</summary>
        public double LabelWidth
        {
            get => (double)GetValue(LabelWidthProperty);
            set => SetValue(LabelWidthProperty, value);
        }

        /// <summary>Margin applied to the row container.</summary>
        public Thickness RowMargin
        {
            get => (Thickness)GetValue(RowMarginProperty);
            set => SetValue(RowMarginProperty, value);
        }

        /// <summary>Interactive or display content placed beside the label.</summary>
        public object RowContent
        {
            get => GetValue(RowContentProperty);
            set => SetValue(RowContentProperty, value);
        }

        /// <summary>Initializes the labeled settings row.</summary>
        public SettingsLabeledRow()
        {
            InitializeComponent();
        }
    }
}