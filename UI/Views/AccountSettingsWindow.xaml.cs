using System.Collections.ObjectModel;
using System.Windows;
using Core.Enums;
using Core.Models;
using UI.Accounts;

namespace UI.Views
{
    /// <summary>
    /// Edits one account's optional overrides of the global settings.
    /// </summary>
    public partial class AccountSettingsWindow : Window
    {
        private readonly AccountSession _session;
        private readonly ObservableCollection<GameFilterOption> _games = [];

        /// <summary>Initializes the window for the given account.</summary>
        public AccountSettingsWindow(AccountSession session)
        {
            InitializeComponent();
            _session = session;

            AccountSettingsOverrides overrides = session.Model.Overrides;
            TitleText.Text = $"{session.DisplayName} ({session.PlatformName})";
            LevelFarmingCard.Visibility = session.Model.Platform == Platform.Kick ? Visibility.Visible : Visibility.Collapsed;

            UseGlobalAutoClaim.IsChecked = overrides.AutoClaimRewards is null;
            AutoClaimBox.IsChecked = overrides.AutoClaimRewards ?? Core.Managers.UISettingsManager.Instance.AutoClaimRewards;

            UseGlobalLevelFarming.IsChecked = overrides.KickLevelFarming is null;
            LevelFarmingBox.IsChecked = overrides.KickLevelFarming ?? Core.Managers.UISettingsManager.Instance.KickLevelFarming;

            UseGlobalPriority.IsChecked = overrides.MiningPriorityMode is null;
            PriorityBox.SelectedValue = overrides.MiningPriorityMode ?? Core.Managers.UISettingsManager.Instance.MiningPriorityMode;

            UseGlobalFilter.IsChecked = overrides.GameWhitelistSlugs is null;
            BlacklistBox.IsChecked = overrides.GameFilterBlacklistMode;

            HashSet<string> selected = new(overrides.GameWhitelistSlugs ?? [], StringComparer.OrdinalIgnoreCase);
            foreach ((string slug, string name) in session.Engine.GetKnownGames())
            {
                _games.Add(new GameFilterOption(session.Model.Platform, slug, name, selected.Contains(slug)));
                selected.Remove(slug);
            }

            // Keep selections for games that are not in the current campaign list so they are not lost on save.
            foreach (string slug in selected)
                _games.Add(new GameFilterOption(session.Model.Platform, slug, $"{slug} (inactive)", true));

            GameList.ItemsSource = _games;
            if (_games.Count == 0)
                FilterHint.Text = "No games loaded yet for this account. Games appear here once its campaigns have loaded.";
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            AccountSettingsOverrides overrides = _session.Model.Overrides;

            overrides.AutoClaimRewards = UseGlobalAutoClaim.IsChecked == true ? null : AutoClaimBox.IsChecked == true;
            overrides.KickLevelFarming = _session.Model.Platform != Platform.Kick || UseGlobalLevelFarming.IsChecked == true
                ? null
                : LevelFarmingBox.IsChecked == true;
            overrides.MiningPriorityMode = UseGlobalPriority.IsChecked == true || PriorityBox.SelectedValue is not MiningPriorityMode mode
                ? null
                : mode;

            if (UseGlobalFilter.IsChecked == true)
            {
                overrides.GameWhitelistSlugs = null;
                overrides.GameFilterBlacklistMode = false;
            }
            else
            {
                overrides.GameWhitelistSlugs = _games.Where(g => g.IsSelected).Select(g => g.Slug).ToList();
                overrides.GameFilterBlacklistMode = BlacklistBox.IsChecked == true;
            }

            _session.ApplyOverridesChanged();
            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}