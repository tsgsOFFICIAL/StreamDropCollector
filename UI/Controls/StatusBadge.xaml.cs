using UserControl = System.Windows.Controls.UserControl;
using System.Windows;
using UI.Models;

namespace UI.Controls
{
    /// <summary>
    /// Compact status label with variant-specific styling.
    /// </summary>
    public partial class StatusBadge : UserControl
    {
        /// <summary>Identifies the <see cref="BadgeText"/> dependency property.</summary>
        public static readonly DependencyProperty BadgeTextProperty =
            DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

        /// <summary>Identifies the <see cref="Variant"/> dependency property.</summary>
        public static readonly DependencyProperty VariantProperty =
            DependencyProperty.Register(nameof(Variant), typeof(StatusBadgeVariant), typeof(StatusBadge), new PropertyMetadata(StatusBadgeVariant.Mining));

        /// <summary>Text displayed inside the badge.</summary>
        public string BadgeText
        {
            get => (string)GetValue(BadgeTextProperty);
            set => SetValue(BadgeTextProperty, value);
        }

        /// <summary>Visual variant that controls badge colors and styling.</summary>
        public StatusBadgeVariant Variant
        {
            get => (StatusBadgeVariant)GetValue(VariantProperty);
            set => SetValue(VariantProperty, value);
        }

        /// <summary>Initializes the status badge control.</summary>
        public StatusBadge()
        {
            InitializeComponent();
        }
    }
}