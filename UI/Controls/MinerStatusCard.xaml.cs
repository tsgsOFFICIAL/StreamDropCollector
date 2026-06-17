using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Media;
using System.Windows;

namespace UI.Controls
{
    public partial class MinerStatusCard : UserControl
    {
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(nameof(Status), typeof(string), typeof(MinerStatusCard), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DetailsProperty =
            DependencyProperty.Register(nameof(Details), typeof(string), typeof(MinerStatusCard), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty StatusBrushProperty =
            DependencyProperty.Register(
                    nameof(StatusBrush),
                    typeof(System.Windows.Media.Brush),
                    typeof(MinerStatusCard),
                    new PropertyMetadata(new SolidColorBrush(Colors.Orange))
                );

        public string Status
        {
            get => (string)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        public string Details
        {
            get => (string)GetValue(DetailsProperty);
            set => SetValue(DetailsProperty, value);
        }

        public System.Windows.Media.Brush StatusBrush
        {
            get => (System.Windows.Media.Brush)GetValue(StatusBrushProperty);
            set => SetValue(StatusBrushProperty, value);
        }

        public MinerStatusCard()
        {
            InitializeComponent();
        }
    }
}