using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for TwitchLoginWindow.xaml
    /// </summary>
    public partial class TwitchLoginWindow : Window
    {
        /// <summary>
        /// Initializes the Twitch login window and navigates to the Twitch login page when loaded.
        /// </summary>
        public TwitchLoginWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await Web.EnsureCoreWebView2Async();
            Web.Source = new Uri("https://twitch.tv/login");
        }
    }
}