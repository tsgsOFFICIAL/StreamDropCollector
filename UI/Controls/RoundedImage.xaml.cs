using UserControl = System.Windows.Controls.UserControl;
using System.Windows;

namespace UI.Controls
{
    public partial class RoundedImage : UserControl
    {
        public static readonly DependencyProperty SourceUrlProperty =
            DependencyProperty.Register(nameof(SourceUrl), typeof(string), typeof(RoundedImage), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ImageWidthProperty =
            DependencyProperty.Register(nameof(ImageWidth), typeof(double), typeof(RoundedImage), new PropertyMetadata(100.0));

        public static readonly DependencyProperty ImageHeightProperty =
            DependencyProperty.Register(nameof(ImageHeight), typeof(double), typeof(RoundedImage), new PropertyMetadata(100.0));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(RoundedImage), new PropertyMetadata(10.0));

        public string SourceUrl
        {
            get => (string)GetValue(SourceUrlProperty);
            set => SetValue(SourceUrlProperty, value);
        }

        public double ImageWidth
        {
            get => (double)GetValue(ImageWidthProperty);
            set => SetValue(ImageWidthProperty, value);
        }

        public double ImageHeight
        {
            get => (double)GetValue(ImageHeightProperty);
            set => SetValue(ImageHeightProperty, value);
        }

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public RoundedImage()
        {
            InitializeComponent();
        }
    }
}