using UserControl = System.Windows.Controls.UserControl;
using System.Windows;
using Core.Enums;
using UI.Accounts;

namespace UI.Views
{
    /// <summary>
    /// Dashboard listing every account with its connection state and mining progress.
    /// </summary>
    public partial class DashboardView : UserControl
    {
        private static readonly Lazy<DashboardView> _instance = new(() => new DashboardView());
        private bool _started;

        /// <summary>
        /// Gets the singleton instance of the dashboard view.
        /// </summary>
        public static DashboardView Instance => _instance.Value;

        private DashboardView()
        {
            InitializeComponent();
            DataContext = AccountManager.Instance;
            Loaded += async (_, _) => await StartAsync();
        }

        private async Task StartAsync()
        {
            if (_started)
                return;

            _started = true;
            await AccountManager.Instance.InitializeAsync();
        }

        private async void OnAddTwitchAccountClick(object sender, RoutedEventArgs e) =>
            await AccountManager.Instance.AddAccountAsync(Platform.Twitch);

        private async void OnAddKickAccountClick(object sender, RoutedEventArgs e) =>
            await AccountManager.Instance.AddAccountAsync(Platform.Kick);
    }
}
