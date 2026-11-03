using UserControl = System.Windows.Controls.UserControl;
using System.Windows;
using Core.Models;
using UI.Accounts;

namespace UI.Controls
{
    /// <summary>
    /// Card grouping inventory rewards under a single campaign.
    /// </summary>
    public partial class InventoryCampaignCard : UserControl
    {
        /// <summary>Initializes the inventory campaign card.</summary>
        public InventoryCampaignCard()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => RefreshGameFilterButtons();
        }

        /// <summary>
        /// Shows the game filter shortcuts only when they mean something for this campaign. A filter change replaces the
        /// inventory cards, so this does not need to track later state changes.
        /// </summary>
        private void RefreshGameFilterButtons()
        {
            if (DataContext is DropsCampaign { CanFilterByGame: true } campaign
                && AccountManager.Instance.SelectedSession is { } session
                && session.Model.Platform == campaign.Platform)
            {
                IgnoreButton.Visibility = Visibility.Visible;
                IgnoreButton.ToolTip = $"Stop mining {campaign.GameName} for {session.GameFilterScopeText}. " +
                                       "You can allow it again from \"Ignored games\" at the bottom of this page.";
                MoveTopButton.Visibility = session.CanMoveGameToTop(campaign.Slug) ? Visibility.Visible : Visibility.Collapsed;
                MoveTopButton.ToolTip = $"Mine {campaign.GameName} before your other prioritized games ({session.GameFilterScopeText}).";
            }
            else
            {
                IgnoreButton.Visibility = Visibility.Collapsed;
                MoveTopButton.Visibility = Visibility.Collapsed;
            }
        }

        private void OnIgnoreGameClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is DropsCampaign campaign && AccountManager.Instance.SelectedSession is { } session)
                session.IgnoreGame(campaign.Slug);
        }

        private void OnMoveGameToTopClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is DropsCampaign campaign && AccountManager.Instance.SelectedSession is { } session)
                session.MoveGameToTop(campaign.Slug);
        }
    }
}
