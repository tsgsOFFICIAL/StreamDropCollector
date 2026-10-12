using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Core.Managers;
using Core.Models;
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

        private readonly ListCollectionView _enabledAccounts;
        private ListCollectionView? _campaignView;

        private InventoryView()
        {
            InitializeComponent();

            // A private view (the default view is shared with the dashboard list) showing enabled accounts only.
            _enabledAccounts = new ListCollectionView(AccountManager.Instance.Sessions)
            {
                Filter = item => item is AccountSession { Enabled: true }
            };
            AccountPicker.ItemsSource = _enabledAccounts;
            AccountManager.Instance.EnabledAccountsChanged += () => _enabledAccounts.Refresh();
            AccountPicker.SelectedItem = AccountManager.Instance.SelectedSession;
            HideCompletedBox.IsChecked = UISettingsManager.Instance.HideCompletedCampaigns;
            BindCampaigns(AccountManager.Instance.SelectedSession);

            AccountManager.Instance.SelectedSessionChanged += session =>
            {
                AccountPicker.SelectedItem = session;
                BindCampaigns(session);
            };
        }

        private void BindCampaigns(AccountSession? session)
        {
            InventoryScroll.DataContext = session?.Engine;

            // A private view per engine so the completed-campaign filter doesn't leak into the shared default view.
            _campaignView = session?.Engine is { } engine
                ? new ListCollectionView(engine.ActiveCampaigns) { Filter = IsVisibleCampaign }
                : null;
            CampaignList.ItemsSource = _campaignView;
        }

        private static bool IsVisibleCampaign(object item) =>
            !(UISettingsManager.Instance.HideCompletedCampaigns && item is DropsCampaign { AllRewardsClaimed: true });

        private void OnHideCompletedChanged(object sender, RoutedEventArgs e)
        {
            UISettingsManager.Instance.HideCompletedCampaigns = HideCompletedBox.IsChecked == true;
            UISettingsManager.Instance.SaveSettings();
            _campaignView?.Refresh();
        }

        private void OnAccountPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountPicker.SelectedItem is AccountSession session)
                AccountManager.Instance.SelectedSession = session;
        }
    }
}
