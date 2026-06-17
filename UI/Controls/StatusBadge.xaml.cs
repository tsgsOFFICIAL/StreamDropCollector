using UserControl = System.Windows.Controls.UserControl;
using System.Windows;
using UI.Models;

namespace UI.Controls
{
    public partial class StatusBadge : UserControl
    {
        public static readonly DependencyProperty BadgeTextProperty =
            DependencyProperty.Register(nameof(BadgeText), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty VariantProperty =
            DependencyProperty.Register(nameof(Variant), typeof(StatusBadgeVariant), typeof(StatusBadge), new PropertyMetadata(StatusBadgeVariant.Watching));

        public string BadgeText
        {
            get => (string)GetValue(BadgeTextProperty);
            set => SetValue(BadgeTextProperty, value);
        }

        public StatusBadgeVariant Variant
        {
            get => (StatusBadgeVariant)GetValue(VariantProperty);
            set => SetValue(VariantProperty, value);
        }

        public StatusBadge()
        {
            InitializeComponent();
        }
    }
}