namespace UI.Views
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : System.Windows.Controls.UserControl
    {
        private static readonly Lazy<SettingsView> _instance = new(() => new SettingsView());
        public static SettingsView Instance => _instance.Value;

        private SettingsView()
        {
            InitializeComponent();
        }
    }
}