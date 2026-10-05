using System.Windows;
using UI.Accounts;
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

        private void OnRemoveClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is AccountSession session)
                AccountManager.Instance.RemoveAccount(session);
        }
    }
}