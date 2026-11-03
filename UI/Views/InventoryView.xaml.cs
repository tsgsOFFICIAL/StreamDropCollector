using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Core.Enums;
using Core.Managers;
using Core.Models;
using UI.Accounts;
using UI.Models;

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
        private ListCollectionView? _rewardCampaignView;

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
            SortPicker.SelectedValue = UISettingsManager.Instance.InventorySortMode;
            BindCampaigns(AccountManager.Instance.SelectedSession);

            AccountManager.Instance.SelectedSessionChanged += session =>
            {
                AccountPicker.SelectedItem = session;
                BindCampaigns(session);
            };
        }

        private AccountSession? _boundSession;

        private void BindCampaigns(AccountSession? session)
        {
            if (_boundSession is not null)
            {
                _boundSession.GameFilterEdited -= RefreshIgnoredGames;
                _boundSession.Engine.ActiveCampaigns.CollectionChanged -= OnActiveCampaignsChanged;
            }

            _boundSession = session;
            if (session is not null)
            {
                session.GameFilterEdited += RefreshIgnoredGames;
                session.Engine.ActiveCampaigns.CollectionChanged += OnActiveCampaignsChanged;
            }

            RefreshIgnoredGames();

            InventoryScroll.DataContext = session?.Engine;

            // A private view per engine so the completed-campaign filter doesn't leak into the shared default view.
            _campaignView = session?.Engine is { } engine
                ? new ListCollectionView(engine.ActiveCampaigns) { Filter = IsVisibleCampaign }
                : null;
            ApplySort(_campaignView);
            CampaignList.ItemsSource = _campaignView;

            _rewardCampaignView = session?.Engine is { } rewardEngine
                ? new ListCollectionView(rewardEngine.RewardCampaigns) { Filter = IsVisibleCampaign }
                : null;
            ApplySort(_rewardCampaignView);
            RewardCampaignList.ItemsSource = _rewardCampaignView;
        }

        // The campaign list is rebuilt whenever a filter changes, including from the settings page.
        private void OnActiveCampaignsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
            Dispatcher.InvokeAsync(RefreshIgnoredGames);

        private void RefreshIgnoredGames()
        {
            IReadOnlyList<IgnoredGame> ignored = _boundSession?.GetIgnoredGames() ?? [];
            IgnoredGamesList.ItemsSource = ignored;
            IgnoredGamesPanel.Visibility = ignored.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            IgnoredGamesHint.Text = _boundSession is null
                ? string.Empty
                : $"Not mined because of your game filter ({_boundSession.GameFilterScopeText}). Allow a game to mine it again.";
        }

        private void OnUnignoreGameClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string slug })
                _boundSession?.UnignoreGame(slug);
        }

        private static void ApplySort(ListCollectionView? view)
        {
            if (view is null)
                return;

            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                switch (UISettingsManager.Instance.InventorySortMode)
                {
                    case InventorySortMode.EndingSoonest:
                        view.SortDescriptions.Add(new SortDescription(nameof(DropsCampaign.EndsAt), ListSortDirection.Ascending));
                        break;
                    case InventorySortMode.GameName:
                        view.SortDescriptions.Add(new SortDescription(nameof(DropsCampaign.GameName), ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription(nameof(DropsCampaign.EndsAt), ListSortDirection.Ascending));
                        break;
                    case InventorySortMode.LeastTimeRemaining:
                        // Finished campaigns have 0 minutes left, so keep them below the ones still in progress.
                        view.SortDescriptions.Add(new SortDescription(nameof(DropsCampaign.AllRewardsClaimed), ListSortDirection.Ascending));
                        view.SortDescriptions.Add(new SortDescription(nameof(DropsCampaign.RemainingMinutes), ListSortDirection.Ascending));
                        break;
                }
            }
        }

        private void OnSortPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SortPicker.SelectedValue is not InventorySortMode mode)
                return;

            UISettingsManager.Instance.InventorySortMode = mode;
            ApplySort(_campaignView);
            ApplySort(_rewardCampaignView);
        }

        private static bool IsVisibleCampaign(object item) =>
            !(UISettingsManager.Instance.HideCompletedCampaigns && item is DropsCampaign { AllRewardsClaimed: true });

        private void OnHideCompletedChanged(object sender, RoutedEventArgs e)
        {
            UISettingsManager.Instance.HideCompletedCampaigns = HideCompletedBox.IsChecked == true;
            UISettingsManager.Instance.SaveSettings();
            _campaignView?.Refresh();
            _rewardCampaignView?.Refresh();
        }

        private void OnAccountPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccountPicker.SelectedItem is AccountSession session)
                AccountManager.Instance.SelectedSession = session;
        }
    }
}
