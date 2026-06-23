using Core.Interfaces;
using Core.Logging;

namespace Core.Mining.Twitch;

/// <summary>
/// Reads Twitch stream page state (online, category, quality, mature gate) via the hidden WebView.
/// </summary>
public sealed class TwitchStreamPageReader
{
    private readonly IWebViewHost? _webViewHost;
    private readonly WebViewUiRunner _uiRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="TwitchStreamPageReader"/> class.
    /// </summary>
    public TwitchStreamPageReader(IWebViewHost? webViewHost, WebViewUiRunner uiRunner)
    {
        _webViewHost = webViewHost;
        _uiRunner = uiRunner;
    }

    /// <summary>
    /// Dismisses the Twitch mature-content gate if present.
    /// </summary>
    public async Task DismissMatureGateAsync()
    {
        if (_webViewHost == null)
            return;

        const string js = """
            (() => {
                const button = document.querySelector('button[data-a-target="content-classification-gate-overlay-start-watching-button"]');
                if (button) {
                    button.click();
                    return true;
                }
                return false;
            })();
            """;

        try
        {
            string result = await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(js));
            if (result?.Trim('"').Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                AppLogger.Debug("TwitchSelection", "[Twitch] Auto-accepted mature content gate.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TwitchSelection", $"Failed dismissing Twitch mature content gate. {ex.Message}");
        }
    }

    /// <summary>
    /// Sets Twitch player quality to 160p via local storage.
    /// </summary>
    public async Task SetLowestQualityAsync()
    {
        if (_webViewHost == null)
            return;

        const string js = """
            (() => {
                localStorage.setItem('video-quality', '{"default":"160p30"}');
            })();
            """;

        try
        {
            await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(js));
            AppLogger.Debug("TwitchSelection", "[Twitch] Quality set to 160p 30");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TwitchSelection", $"Failed setting Twitch quality to lowest. {ex.Message}");
        }
    }

    /// <summary>
    /// Prepares the stream page after navigation: dismiss mature gate, set quality, refresh player.
    /// </summary>
    public async Task PrepareForMiningAsync(CancellationToken ct = default)
    {
        if (_webViewHost == null)
            return;

        await DismissMatureGateAsync();
        await SetLowestQualityAsync();
        await _uiRunner.RunAsync(() => _webViewHost.ForceRefreshAsync());
        await Task.Delay(5000, ct);
        await DismissMatureGateAsync();
    }
}