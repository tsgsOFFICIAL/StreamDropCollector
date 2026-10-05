using System.Windows;
using UI.Accounts;
using UI.Views;
using UserControl = System.Windows.Controls.UserControl;

namespace UI.Controls
{
    /// <summary>
    /// Shows one account's connection state, controls and mining progress.
    /// </summary>
    public partial class AccountCard : UserControl
    {
        /// <summary>Initializes the account card.</summary>
        public AccountCard()
        {
            InitializeComponent();
        }

        private async void OnLoginClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is AccountSession session)
                await session.LoginAsync();
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AccountSession session)
                return;

            new AccountSettingsWindow(session) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is AccountSession session)
                AccountManager.Instance.RemoveAccount(session);
        }
    }
}