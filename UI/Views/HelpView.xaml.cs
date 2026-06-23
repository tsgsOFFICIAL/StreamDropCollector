namespace UI.Views
{
    /// <summary>
    /// Interaction logic for HelpView.xaml
    /// </summary>
    public partial class HelpView : System.Windows.Controls.UserControl
    {
        private static readonly Lazy<HelpView> _instance = new(() => new HelpView());

        /// <summary>
        /// Gets the singleton instance of the help view.
        /// </summary>
        public static HelpView Instance => _instance.Value;

        private HelpView()
        {
            InitializeComponent();
        }
    }
}