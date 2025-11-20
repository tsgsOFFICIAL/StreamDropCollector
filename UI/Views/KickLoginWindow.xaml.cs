using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for KickLoginWindow.xaml
    /// </summary>
    public partial class KickLoginWindow : Window
    {
        public string? SessionToken { get; private set; }
        public KickLoginWindow()
        {
            InitializeComponent();
            Initialize();
        }

        private async void Initialize()
        {
            await Web.EnsureCoreWebView2Async();

            Web.Source = new Uri("https://kick.com");
        }
    }
}