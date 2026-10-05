using System.Windows.Controls;
using UI.Accounts;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for InventoryView.xaml
    /// </summary>
    public partial class InventoryView : System.Windows.Controls.UserControl
    {
        private static readonly Lazy<InventoryView> _instance = new(() => new InventoryView());

        /// <summary>
        /// Gets the singleton instance of the inventory view.
        /// </summary>
        public static InventoryView Instance => _instance.Value;

        private InventoryView()
        {
            InitializeComponent();

            AccountPicker.ItemsSource = AccountManager.Instance.Sessions;
            AccountPicker.SelectedItem = AccountManager.Instance.SelectedSession;
            InventoryScroll.DataContext = AccountManager.Instance.SelectedSession?.Engine;

            AccountManager.Instance.SelectedSessionChanged += session =>
            {
                AccountPicker.SelectedItem = session;
                InventoryScroll.DataContext = session?.Engine;
            };
        }

        private void OnAccountPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountPicker.SelectedItem is AccountSession session)
                AccountManager.Instance.SelectedSession = session;
        }
    }
}
