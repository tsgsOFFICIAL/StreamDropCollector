using UserControl = System.Windows.Controls.UserControl;
using System.Windows;

namespace UI.Controls
{
    public partial class HelpDonationCard : UserControl
    {
        public HelpDonationCard()
        {
            InitializeComponent();
        }

        private void OnBuyMeCoffeeClick(object sender, RoutedEventArgs e) =>
            Core.Utility.LaunchWeb("https://ko-fi.com/tsgsOFFICIAL");
    }
}