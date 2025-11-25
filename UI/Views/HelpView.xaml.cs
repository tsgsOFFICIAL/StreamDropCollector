using System.Windows.Navigation;
using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for HelpView.xaml
    /// </summary>
    public partial class HelpView : System.Windows.Controls.UserControl
    {
        private static readonly Lazy<HelpView> _instance = new(() => new HelpView());
        public static HelpView Instance => _instance.Value;

        private HelpView()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Core.Utility.LaunchWeb(e.Uri.AbsoluteUri);
        }

        private void OnBuyMeCoffeeButtonClicked(object sender, RoutedEventArgs e)
        {
            Core.Utility.LaunchWeb("https://ko-fi.com/tsgsOFFICIAL");
        }
    }
}