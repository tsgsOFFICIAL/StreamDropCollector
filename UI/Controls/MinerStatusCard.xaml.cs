using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Media;
using System.Windows;

namespace UI.Controls
{
    /// <summary>
    /// Card displaying miner status text, details, and a colored status indicator.
    /// </summary>
    public partial class MinerStatusCard : UserControl
    {
        /// <summary>Identifies the <see cref="Status"/> dependency property.</summary>
        public static readonly DependencyProperty StatusProperty =
            DependencyProperty.Register(nameof(Status), typeof(string), typeof(MinerStatusCard), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="Details"/> dependency property.</summary>
        public static readonly DependencyProperty DetailsProperty =
            DependencyProperty.Register(nameof(Details), typeof(string), typeof(MinerStatusCard), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="StatusBrush"/> dependency property.</summary>
        public static readonly DependencyProperty StatusBrushProperty =
            DependencyProperty.Register(
                    nameof(StatusBrush),
                    typeof(System.Windows.Media.Brush),
                    typeof(MinerStatusCard),
                    new PropertyMetadata(new SolidColorBrush(Colors.Orange))
                );

        /// <summary>Primary miner status headline.</summary>
        public string Status
        {
            get => (string)GetValue(StatusProperty);
            set => SetValue(StatusProperty, value);
        }

        /// <summary>Secondary detail text shown beneath the status.</summary>
        public string Details
        {
            get => (string)GetValue(DetailsProperty);
            set => SetValue(DetailsProperty, value);
        }

        /// <summary>Brush used for the status indicator.</summary>
        public System.Windows.Media.Brush StatusBrush
        {
            get => (System.Windows.Media.Brush)GetValue(StatusBrushProperty);
            set => SetValue(StatusBrushProperty, value);
        }

        /// <summary>Initializes the miner status card.</summary>
        public MinerStatusCard()
        {
            InitializeComponent();
        }
    }
}