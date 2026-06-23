using System.Windows;
using Core.Models;

namespace UI.Views
{
    /// <summary>
    /// Device-code OAuth window for Twitch Helix API access (separate from drops WebView login).
    /// </summary>
    /// <remarks>
    /// Loads Twitch's activation URL in an embedded WebView so the user can approve Helix API access
    /// without copying a URL or confirmation code manually.
    /// </remarks>
    public partial class TwitchHelixAuthWindow : Window
    {
        private readonly TwitchDeviceCodePrompt _prompt;

        /// <summary>
        /// Opens the Twitch activation page so the user can approve Helix API access.
        /// </summary>
        /// <param name="prompt">Device-code details from the Helix OAuth flow.</param>
        public TwitchHelixAuthWindow(TwitchDeviceCodePrompt prompt)
        {
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await Web.EnsureCoreWebView2Async();
            Web.Source = new Uri(_prompt.ActivationUrl);
        }
    }
}