using UserControl = System.Windows.Controls.UserControl;

namespace UI.Controls
{
    /// <summary>
    /// Row displaying a single platform's connection status and login affordance.
    /// </summary>
    public partial class PlatformConnectionRow : UserControl
    {
        /// <summary>Initializes the platform connection row.</summary>
        public PlatformConnectionRow()
        {
            InitializeComponent();
        }
    }
}