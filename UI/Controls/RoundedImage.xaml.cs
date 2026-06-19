using UserControl = System.Windows.Controls.UserControl;
using System.Windows;
using System.Windows.Media;

namespace UI.Controls
{
    /// <summary>
    /// Image control with configurable dimensions and rounded-corner clipping.
    /// </summary>
    public partial class RoundedImage : UserControl
    {
        private static readonly PropertyChangedCallback OnLayoutChanged =
            (d, _) => ((RoundedImage)d).UpdateClip();

        /// <summary>Identifies the <see cref="SourceUrl"/> dependency property.</summary>
        public static readonly DependencyProperty SourceUrlProperty =
            DependencyProperty.Register(nameof(SourceUrl), typeof(string), typeof(RoundedImage), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="ImageWidth"/> dependency property.</summary>
        public static readonly DependencyProperty ImageWidthProperty =
            DependencyProperty.Register(nameof(ImageWidth), typeof(double), typeof(RoundedImage), new PropertyMetadata(100.0, OnLayoutChanged));

        /// <summary>Identifies the <see cref="ImageHeight"/> dependency property.</summary>
        public static readonly DependencyProperty ImageHeightProperty =
            DependencyProperty.Register(nameof(ImageHeight), typeof(double), typeof(RoundedImage), new PropertyMetadata(100.0, OnLayoutChanged));

        /// <summary>Identifies the <see cref="CornerRadius"/> dependency property.</summary>
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(RoundedImage), new PropertyMetadata(10.0, OnLayoutChanged));

        /// <summary>Image source URL bound to the inner image.</summary>
        public string SourceUrl
        {
            get => (string)GetValue(SourceUrlProperty);
            set => SetValue(SourceUrlProperty, value);
        }

        /// <summary>Rendered image width in device-independent pixels.</summary>
        public double ImageWidth
        {
            get => (double)GetValue(ImageWidthProperty);
            set => SetValue(ImageWidthProperty, value);
        }

        /// <summary>Rendered image height in device-independent pixels.</summary>
        public double ImageHeight
        {
            get => (double)GetValue(ImageHeightProperty);
            set => SetValue(ImageHeightProperty, value);
        }

        /// <summary>Corner radius used for border and clip geometry.</summary>
        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        /// <summary>Initializes the rounded image control and applies clipping on load.</summary>
        public RoundedImage()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateClip();
        }

        private void UpdateClip()
        {
            if (ImageBorder == null)
                return;

            double radius = CornerRadius;
            ImageBorder.CornerRadius = new CornerRadius(radius);
            ImageBorder.Clip = new RectangleGeometry(
                new Rect(0, 0, ImageWidth, ImageHeight),
                radius,
                radius);
        }
    }
}