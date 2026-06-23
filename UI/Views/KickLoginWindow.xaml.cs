using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Windows;

namespace UI.Views
{
    /// <summary>
    /// Interaction logic for KickLoginWindow.xaml
    /// </summary>
    public partial class KickLoginWindow : Window
    {
        private const string LoginSelector = "[data-testid='login']";
        private const int PollIntervalMs = 250;

        /// <summary>
        /// Initializes the Kick login window and navigates to the Kick site when loaded.
        /// </summary>
        public KickLoginWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            await Web.EnsureCoreWebView2Async();
            Web.NavigationCompleted += OnNavigationCompleted;
            Web.Source = new Uri("https://kick.com");
        }

        private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            Web.NavigationCompleted -= OnNavigationCompleted;
            await ClickWhenEarlyButtonReadyAsync();
        }

        private async Task ClickWhenEarlyButtonReadyAsync(int timeoutMs = 30000)
        {
            string escaped = JsonSerializer.Serialize(LoginSelector);
            string isEarlyScript = $$"""
                (() => {
                    const el = document.querySelector({{escaped}});
                    if (!el) return false;
                    const rect = el.getBoundingClientRect();
                    return (el.textContent || '').trim().length === 0 && rect.width > 0 && rect.height > 0;
                })()
                """;
            string clickScript = $"document.querySelector({escaped})?.click()";

            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                if (await Web.ExecuteScriptAsync(isEarlyScript) == "true")
                {
                    await Web.ExecuteScriptAsync(clickScript);
                    return;
                }

                await Task.Delay(PollIntervalMs);
                elapsed += PollIntervalMs;
            }
        }
    }
}