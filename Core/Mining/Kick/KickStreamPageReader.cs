using Core.Interfaces;
using Core.Logging;

namespace Core.Mining.Kick;

/// <summary>
/// Reads Kick stream page state (quality, mature gate) via the hidden WebView.
/// </summary>
public sealed class KickStreamPageReader
{
    private readonly IWebViewHost? _webViewHost;
    private readonly WebViewUiRunner _uiRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="KickStreamPageReader"/> class.
    /// </summary>
    public KickStreamPageReader(IWebViewHost? webViewHost, WebViewUiRunner uiRunner)
    {
        _webViewHost = webViewHost;
        _uiRunner = uiRunner;
    }

    /// <summary>
    /// Dismisses the Kick mature-content gate if present.
    /// </summary>
    public async Task DismissMatureGateAsync()
    {
        if (_webViewHost == null)
            return;

        const string js = """
            (() => {
                const button = document.querySelector('button[data-a-target="player-overlay-mature-accept"]') ||
                               document.querySelector('button[data-testid="mature-gate-button"]') ||
                               Array.from(document.querySelectorAll('button')).find(b =>
                                   /continue|start watching|i understand/i.test(b.textContent || ''));
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
                AppLogger.Debug("KickSelection", "[Kick] Auto-accepted mature content gate.");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("KickSelection", $"Failed dismissing Kick mature content gate. {ex.Message}");
        }
    }

    /// <summary>
    /// Sets Kick player quality to 160p via session storage.
    /// </summary>
    public async Task SetLowestQualityAsync()
    {
        if (_webViewHost == null)
            return;

        const string js = """
            (() => {
                sessionStorage.setItem('stream_quality', '160');
            })();
            """;

        try
        {
            await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(js));
            AppLogger.Debug("KickSelection", "[Kick] Quality set to lowest: 160p 30");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("KickSelection", $"Failed setting Kick quality to lowest. {ex.Message}");
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