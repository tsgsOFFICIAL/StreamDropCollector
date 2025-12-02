using Core.Managers;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for InventoryView.xaml
    /// </summary>
    public partial class InventoryView : System.Windows.Controls.UserControl
    {
        private static readonly Lazy<InventoryView> _instance = new(() => new InventoryView());
        public static InventoryView Instance => _instance.Value;

        private InventoryView()
        {
            InitializeComponent();
            DataContext = DropsInventoryManager.Instance;
        }
    }
}