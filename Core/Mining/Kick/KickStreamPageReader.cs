using System.Text.Json;
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

    private double? _lastCurrentTime;
    private DateTime _lastCheckUtc;

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

        try
        {
            await _webViewHost.EnableVerboseNetworkAndConsoleLoggingAsync("KickNet");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("KickNet", $"EnableVerboseNetworkAndConsoleLoggingAsync failed. {ex.Message}");
        }

        await DismissMatureGateAsync();
        await SetLowestQualityAsync();
        await _uiRunner.RunAsync(() => _webViewHost.ForceRefreshAsync());
        await Task.Delay(5000, ct);
        await DismissMatureGateAsync();
    }

    /// <summary>
    /// Reads the live &lt;video&gt; element's real playback state (paused, currentTime, buffered, errors) and
    /// document visibility/focus, logging whether playback is actually advancing between calls.
    /// </summary>
    /// <remarks>
    /// The app's own minute-tick counter is a dumb wall-clock timer with no awareness of whether the stream is
    /// actually playing. This gives ground truth so a frozen server-side progress value can be attributed to
    /// either "server just hasn't caught up" or "the video silently stalled while we kept counting."
    /// </remarks>
    public async Task LogPlaybackDiagnosticsAsync()
    {
        if (_webViewHost == null)
            return;

        const string js = """
            (() => {
                const v = document.querySelector('video');
                if (!v) {
                    return JSON.stringify({
                        videoFound: false,
                        visibilityState: document.visibilityState,
                        hasFocus: document.hasFocus(),
                        hidden: document.hidden
                    });
                }

                const buffered = [];
                for (let i = 0; i < v.buffered.length; i++)
                    buffered.push([v.buffered.start(i), v.buffered.end(i)]);

                return JSON.stringify({
                    videoFound: true,
                    paused: v.paused,
                    ended: v.ended,
                    currentTime: v.currentTime,
                    duration: v.duration,
                    readyState: v.readyState,
                    networkState: v.networkState,
                    muted: v.muted,
                    volume: v.volume,
                    error: v.error ? { code: v.error.code, message: v.error.message } : null,
                    buffered: buffered,
                    visibilityState: document.visibilityState,
                    hasFocus: document.hasFocus(),
                    hidden: document.hidden
                });
            })();
            """;

        try
        {
            string raw = await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(js));
            string? json = JsonSerializer.Deserialize<string>(raw);
            if (string.IsNullOrWhiteSpace(json))
            {
                AppLogger.Warn("KickPlayback", "LogPlaybackDiagnosticsAsync got empty result.");
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            bool videoFound = root.TryGetProperty("videoFound", out JsonElement vf) && vf.GetBoolean();
            if (!videoFound)
            {
                AppLogger.Warn(
                    "KickPlayback",
                    $"No <video> element found on page. visibilityState={GetString(root, "visibilityState")} " +
                    $"hasFocus={GetBool(root, "hasFocus")} hidden={GetBool(root, "hidden")}");
                _lastCurrentTime = null;
                return;
            }

            double currentTime = root.GetProperty("currentTime").GetDouble();
            string advanceStatus;
            if (_lastCurrentTime is double previous)
            {
                double elapsedRealSeconds = Math.Max(0.001, (nowUtc - _lastCheckUtc).TotalSeconds);
                double videoDelta = currentTime - previous;
                advanceStatus = videoDelta > 0.5
                    ? $"ADVANCING (+{videoDelta:F1}s video over {elapsedRealSeconds:F0}s real)"
                    : $"STALLED (+{videoDelta:F2}s video over {elapsedRealSeconds:F0}s real)";
            }
            else
            {
                advanceStatus = "FIRST_CHECK";
            }

            _lastCurrentTime = currentTime;
            _lastCheckUtc = nowUtc;

            AppLogger.Debug(
                "KickPlayback",
                $"video state={advanceStatus} paused={GetBool(root, "paused")} ended={GetBool(root, "ended")} " +
                $"currentTime={currentTime:F1} duration={GetDouble(root, "duration"):F1} " +
                $"readyState={GetInt(root, "readyState")} networkState={GetInt(root, "networkState")} " +
                $"muted={GetBool(root, "muted")} volume={GetDouble(root, "volume"):F2} " +
                $"error={(root.TryGetProperty("error", out JsonElement errEl) && errEl.ValueKind != JsonValueKind.Null ? errEl.GetRawText() : "(none)")} " +
                $"buffered={(root.TryGetProperty("buffered", out JsonElement bufEl) ? bufEl.GetRawText() : "[]")} " +
                $"visibilityState={GetString(root, "visibilityState")} hasFocus={GetBool(root, "hasFocus")} hidden={GetBool(root, "hidden")}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("KickPlayback", $"LogPlaybackDiagnosticsAsync failed. {ex.Message}");
        }
    }

    private static string GetString(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out JsonElement el) ? el.GetString() ?? "(null)" : "(missing)";

    private static bool GetBool(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out JsonElement el) && el.ValueKind == JsonValueKind.True;

    private static double GetDouble(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out JsonElement el) && el.ValueKind is JsonValueKind.Number ? el.GetDouble() : double.NaN;

    private static int GetInt(JsonElement root, string prop) =>
        root.TryGetProperty(prop, out JsonElement el) && el.ValueKind is JsonValueKind.Number ? el.GetInt32() : -1;
}