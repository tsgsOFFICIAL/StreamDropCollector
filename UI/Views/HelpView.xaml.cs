using System.Windows.Navigation;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for HelpView.xaml
    /// </summary>
    public partial class HelpView : System.Windows.Controls.UserControl
    {
        public HelpView()
        {
            InitializeComponent();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Core.Utility.LaunchWeb(e.Uri.AbsoluteUri);
        }
    }
}