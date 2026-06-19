using UserControl = System.Windows.Controls.UserControl;
using System.Windows;

namespace UI.Controls
{
    /// <summary>
    /// Help page card with a donation link to the project's Ko-fi page.
    /// </summary>
    public partial class HelpDonationCard : UserControl
    {
        /// <summary>Initializes the help donation card.</summary>
        public HelpDonationCard()
        {
            InitializeComponent();
        }

        private void OnBuyMeCoffeeClick(object sender, RoutedEventArgs e) =>
            Core.Utility.LaunchWeb("https://ko-fi.com/tsgsOFFICIAL");
    }
}