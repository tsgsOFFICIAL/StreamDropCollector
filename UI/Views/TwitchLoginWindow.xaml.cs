using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for TwitchLoginWindow.xaml
    /// </summary>
    public partial class TwitchLoginWindow : Window
    {
        public TwitchLoginWindow()
        {
            InitializeComponent();
            Initialize();
        }

        private async void Initialize()
        {
            await Web.EnsureCoreWebView2Async();

            Web.Source = new Uri("https://twitch.tv/login");
        }
    }
}