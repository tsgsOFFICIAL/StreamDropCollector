using System.Text.Json;
using Core.Enums;
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
    private readonly Func<AudioLoopMode> _loopMode;

    private double? _lastCurrentTime;
    private DateTime _lastCheckUtc;
    private bool _viewerSocketHookInstalled;

    /// <summary>
    /// Initializes a new instance of the <see cref="KickStreamPageReader"/> class.
    /// </summary>
    /// <param name="loopMode">Returns the stream loop mode for this account.</param>
    public KickStreamPageReader(IWebViewHost? webViewHost, WebViewUiRunner uiRunner, Func<AudioLoopMode> loopMode)
    {
        _webViewHost = webViewHost;
        _uiRunner = uiRunner;
        _loopMode = loopMode;
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

        // HEAD-only mining pauses the video, and Kick then stops repeating its watch events. The hook must exist before
        // the page opens its viewer socket, so it is registered here and takes effect on the refresh just below.
        if (_loopMode() == AudioLoopMode.HeadOnly && !_viewerSocketHookInstalled)
        {
            try
            {
                await _webViewHost.AddScriptOnDocumentCreatedAsync(ViewerSocketHookScript);
                _viewerSocketHookInstalled = true;
                AppLogger.Debug("KickSelection", "[Kick] Viewer socket hook registered for HEAD-only mining.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("KickSelection", $"Failed to register the Kick viewer socket hook. {ex.Message}");
            }
        }

        await DismissMatureGateAsync();
        await SetLowestQualityAsync();
        await _uiRunner.RunAsync(() => _webViewHost.ForceRefreshAsync());
        await Task.Delay(5000, ct);
        await DismissMatureGateAsync();

        if (_loopMode() != AudioLoopMode.Off)
        {
            await StartAudioOnlyAsync(null, ct);
            await Task.Delay(3000, ct);
        }

        // Pins page visibility and resumes a paused player right away instead of waiting for the first health tick.
        await LogPlaybackDiagnosticsAsync();
    }

    /// <summary>
    /// Starts the in-page stream loop and pauses the video. The loop polls the audio-only (or lowest-bandwidth) HLS
    /// playlist and downloads each new segment, or only sends HEAD requests for them in HEAD-only mode.
    /// </summary>
    /// <remarks>The loop state is kept in <c>window.__sdcAudio</c> and ends when the page navigates.</remarks>
    /// <param name="login">The mined channel, or <see langword="null"/> to read it from the page address.</param>
    /// <returns><see langword="true"/> when the loop is running.</returns>
    private async Task<bool> StartAudioOnlyAsync(string? login, CancellationToken ct)
    {
        if (_webViewHost == null)
            return false;

        try
        {
            string body = AudioOnlyScript
                .Replace("__LOGIN__", JsonSerializer.Serialize(login ?? string.Empty));

            string? result = await _uiRunner.RunAsync(() => _webViewHost.ExecuteAsyncScriptAsync(body, 25000, ct));
            AppLogger.Debug("KickSelection", $"[Kick] Audio-only start: {result ?? "(no result / timeout)"}");

            return result != null && result.Contains("\"ok\":true", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Warn("KickSelection", $"Failed starting Kick audio-only mining. {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restarts the stream loop when a page reload removed it or it stopped fetching segments, and pauses the video
    /// again if the page resumed it. Called from the health tick.
    /// </summary>
    /// <param name="login">The channel currently being mined.</param>
    public async Task EnsureAudioOnlyAsync(string login, CancellationToken ct = default)
    {
        if (_webViewHost == null || _loopMode() == AudioLoopMode.Off)
            return;

        const string stateJs = """
            (() => {
                const a = window.__sdcAudio;
                const videos = [...document.querySelectorAll('video')];
                return JSON.stringify({
                    path: location.pathname,
                    alive: !!a && !a.stopped,
                    age: a && a.lastSegmentAt ? Math.round((Date.now() - a.lastSegmentAt) / 1000) : null,
                    videoPlaying: videos.some(v => !v.paused)
                });
            })();
            """;

        // The page can hold several <video> elements (clips, previews, the live player); pause every one of them.
        const string pauseVideoJs = """
            (() => { document.querySelectorAll('video').forEach(v => v.pause()); })();
            """;

        const string stopJs = """
            (() => {
                const a = window.__sdcAudio;
                if (a) {
                    a.stopped = true;
                    clearTimeout(a.timer);
                    (a.wsTimers || []).forEach(t => clearTimeout(t));
                    window.__sdcAudio = null;
                }
            })();
            """;

        try
        {
            string raw = await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(stateJs));
            using JsonDocument outer = JsonDocument.Parse(raw);
            using JsonDocument state = JsonDocument.Parse(outer.RootElement.GetString() ?? "{}");

            string path = state.RootElement.GetProperty("path").GetString() ?? string.Empty;
            bool alive = state.RootElement.GetProperty("alive").GetBoolean();
            bool videoPlaying = state.RootElement.GetProperty("videoPlaying").GetBoolean();
            JsonElement ageElement = state.RootElement.GetProperty("age");
            int? age = ageElement.ValueKind == JsonValueKind.Number ? ageElement.GetInt32() : null;

            if (!string.Equals(path.Trim('/'), login, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Debug("KickSelection", $"[Kick] Audio-only watchdog skipped: page is at '{path}', not on '{login}'.");
                return;
            }

            if (alive && age is <= 30)
            {
                if (videoPlaying)
                {
                    AppLogger.Warn("KickSelection", "[Kick] Audio-only watchdog: the page resumed the video; pausing it again.");
                    await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(pauseVideoJs));
                }

                return;
            }

            AppLogger.Warn("KickSelection", $"[Kick] Audio-only watchdog restarting the loop (alive={alive}, secondsSinceLastSegment={age?.ToString() ?? "none"}).");

            if (alive)
                await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(stopJs));

            await StartAudioOnlyAsync(login, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Warn("KickSelection", $"Kick audio-only watchdog failed. {ex.Message}");
        }
    }

    // Wraps WebSocket so the page's viewer socket (wss://websockets.kick.com/viewer/v1/connect) and the watch frames it
    // sends (channel_handshake, tracking.user.watch.livestream) are captured for the HEAD-only loop to repeat.
    private const string ViewerSocketHookScript = """
        (() => {
            if (window.__sdcViewerHook)
                return;
            window.__sdcViewerHook = true;

            const Orig = window.WebSocket;
            const info = { ws: null, handshake: null, userEvent: null, userEventAt: null, sockets: 0 };
            window.__sdcViewer = info;

            const Wrapped = function (url, protocols) {
                const ws = protocols === undefined ? new Orig(url) : new Orig(url, protocols);
                try {
                    if (/\/viewer\/v1\/connect/.test(String(url))) {
                        info.ws = ws;
                        info.sockets++;
                        const send = ws.send;
                        ws.send = function (data) {
                            try {
                                if (typeof data === 'string') {
                                    const frame = JSON.parse(data);
                                    if (frame.type === 'channel_handshake')
                                        info.handshake = data;
                                    else if (frame.type === 'user_event' && frame.data && frame.data.message
                                        && frame.data.message.name === 'tracking.user.watch.livestream') {
                                        info.userEvent = data;
                                        info.userEventAt = Date.now();
                                    }
                                }
                            } catch (e) { }
                            return send.apply(this, arguments);
                        };
                    }
                } catch (e) { }
                return ws;
            };
            Wrapped.prototype = Orig.prototype;
            ['CONNECTING', 'OPEN', 'CLOSING', 'CLOSED'].forEach(k => { Wrapped[k] = Orig[k]; });
            window.WebSocket = Wrapped;
        })();
        """;

    // The channel API returns the IVS master playlist as playback_url. The loop uses its audio-only variant, or the
    // lowest-bandwidth one when there is none.
    private const string AudioOnlyScript = """
        const fail = (error) => JSON.stringify({ ok: false, error });
        const expectedLogin = __LOGIN__;
        const pathLogin = location.pathname.split('/').filter(Boolean)[0];
        const slug = expectedLogin || pathLogin;
        if (!slug)
            return fail('no channel in the page url');
        if (expectedLogin && (pathLogin || '').toLowerCase() !== expectedLogin.toLowerCase())
            return fail('page is not on the mined channel: ' + location.pathname);
        if (window.__sdcAudio)
            return JSON.stringify({ ok: true, already: true });

        const state = { segments: 0, bytes: 0, lastSegmentAt: null, lastError: null, variant: null, stopped: false, timer: null, bigSegments: 0, maxSegmentKb: 0, adPolls: 0 };

        // Channel API -> master playlist -> media playlist URL of the cheapest variant.
        const resolvePlaylistUrl = async () => {
            const channelResponse = await fetch('https://kick.com/api/v2/channels/' + encodeURIComponent(slug), {
                credentials: 'include',
                headers: { 'Accept': 'application/json' }
            });
            if (!channelResponse.ok)
                throw new Error('channel api returned ' + channelResponse.status);

            const masterUrl = (await channelResponse.json())?.playback_url;
            if (!masterUrl)
                throw new Error('no playback_url (channel offline?)');

            const masterResponse = await fetch(masterUrl);
            if (!masterResponse.ok)
                throw new Error('master playlist returned ' + masterResponse.status);

            const lines = (await masterResponse.text()).split('\n').map(l => l.trim());
            const variants = [];
            for (let i = 0; i < lines.length; i++) {
                if (!lines[i].startsWith('#EXT-X-STREAM-INF'))
                    continue;
                let j = i + 1;
                while (j < lines.length && (lines[j] === '' || lines[j].startsWith('#')))
                    j++;
                if (j >= lines.length)
                    continue;

                const info = lines[i];
                const bandwidth = Number((info.match(/BANDWIDTH=(\d+)/) || [])[1] || Number.MAX_SAFE_INTEGER);
                const codecs = (info.match(/CODECS="([^"]*)"/) || [])[1] || '';
                const name = (info.match(/NAME="([^"]*)"/) || [])[1] || '';
                const audioOnly = /audio/i.test(name)
                    || (codecs !== '' && !/avc|hvc|hev|vp0|av01/i.test(codecs))
                    || (codecs === '' && !info.includes('RESOLUTION='));
                variants.push({ url: new URL(lines[j], masterUrl).toString(), bandwidth, audioOnly, name });
            }
            if (variants.length === 0)
                throw new Error('master playlist has no variants');

            const pool = variants.some(v => v.audioOnly) ? variants.filter(v => v.audioOnly) : variants;
            const pick = pool.reduce((a, b) => (b.bandwidth < a.bandwidth ? b : a));
            state.variant = (pick.name || 'unnamed') + ' @' + pick.bandwidth + (pick.audioOnly ? ' (audio-only)' : ' (lowest video, no audio-only variant)');
            state.variants = variants.map(v => (v.name || '?') + '@' + v.bandwidth + (v.audioOnly ? 'A' : ''));
            return pick.url;
        };

        window.__sdcAudio = state;

        // HEAD-only mode: HEAD requests for new segments and a slower playlist poll.
        const pollMs = 10000;
        state.mode = 'head';

        // The paused video no longer makes Kick's player repeat its watch frames, so repeat the captured ones on the
        // page's own viewer socket: the handshake every 15 s and the watch event every 120 s, as the playing player does.
        state.ws = { handshakes: 0, events: 0, skipped: 0, lastSkip: null };
        state.wsTimers = [];
        {
            const sendFrame = (kind) => {
                if (state.stopped)
                    return;
                const viewer = window.__sdcViewer;
                const frame = viewer && (kind === 'event' ? viewer.userEvent : viewer.handshake);
                if (!frame || !viewer.ws || viewer.ws.readyState !== 1) {
                    state.ws.skipped++;
                    state.ws.lastSkip = !viewer ? 'no hook' : !frame ? 'no ' + kind + ' frame captured' : 'socket not open';
                    return;
                }
                viewer.ws.send(frame);
                if (kind === 'event')
                    state.ws.events++;
                else
                    state.ws.handshakes++;
            };
            state.wsTimers.push(setInterval(() => sendFrame('handshake'), 15000));

            const captured = window.__sdcViewer;
            const sinceEvent = captured && captured.userEventAt ? Date.now() - captured.userEventAt : 0;
            state.wsTimers.push(setTimeout(() => {
                sendFrame('event');
                state.wsTimers.push(setInterval(() => sendFrame('event'), 120000));
            }, Math.max(5000, 120000 - sinceEvent)));
        }

        const seen = new Set();
        let playlistUrl = null;
        let failures = 0;
        let first = true;

        const poll = async () => {
            if (state.stopped)
                return;
            try {
                if (!playlistUrl)
                    playlistUrl = await resolvePlaylistUrl();

                const response = await fetch(playlistUrl);
                if (!response.ok)
                    throw new Error('playlist returned ' + response.status);

                const playlistText = await response.text();
                if (/ad-?break|stitched-ad|X-TV-.*-AD/i.test(playlistText))
                    state.adPolls++;
                const urls = playlistText.split('\n').map(l => l.trim())
                    .filter(l => l !== '' && !l.startsWith('#')).map(l => new URL(l, playlistUrl).toString());
                // On the first poll only take the newest segment instead of downloading the whole backlog.
                const fresh = urls.filter(u => !seen.has(u));
                const toFetch = first ? fresh.slice(-1) : fresh;
                fresh.forEach(u => seen.add(u));
                first = false;

                for (const url of toFetch) {
                    const probe = await fetch(url, { method: 'HEAD' });
                    if (!probe.ok)
                        throw new Error('segment HEAD returned ' + probe.status);
                    state.segments++;
                    state.lastSegmentAt = Date.now();
                }

                if (seen.size > 200)
                    [...seen].slice(0, 100).forEach(u => seen.delete(u));
                failures = 0;
                state.lastError = null;
            } catch (e) {
                state.lastError = String(e.message || e);
                // Resolve a fresh playlist after repeated failures (stream restart, expired URL).
                if (++failures >= 3) {
                    playlistUrl = null;
                    first = true;
                    failures = 0;
                }
            }
            state.timer = setTimeout(poll, pollMs);
        };

        await poll();
        if (state.lastError && state.segments === 0) {
            state.stopped = true;
            clearTimeout(state.timer);
            state.wsTimers.forEach(t => clearTimeout(t));
            window.__sdcAudio = null;
            return fail(state.lastError);
        }

        // Pause every <video> (not just the first: the live player can be a later element or mount after this runs), and
        // pause any that starts playing while the loop is active. By now the page has sent its first watch frames.
        const videos = [...document.querySelectorAll('video')];
        const videoWasPlaying = videos.some(v => !v.paused);
        videos.forEach(v => v.pause());
        document.addEventListener('play', (e) => {
            if (window.__sdcAudio === state && !state.stopped && e.target instanceof HTMLMediaElement) {
                e.target.pause();
                state.replayPauses = (state.replayPauses || 0) + 1;
            }
        }, true);

        return JSON.stringify({ ok: true, slug, segments: state.segments, variant: state.variant, variants: state.variants, videoWasPlaying, hasVideo: videos.length > 0, videoCount: videos.length });
        """;

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

        // The hidden host window is parked off-screen, so Chromium can report the page as hidden/occluded; Kick's
        // player then pauses and stops sending watch heartbeats while our local timer keeps counting. Report the
        // real visibility first, then pin the page to "visible" and resume playback if it was paused.
        const string js = """
            (() => {
                const realHidden = Object.getOwnPropertyDescriptor(Document.prototype, 'hidden').get.call(document);
                const realVisibility = Object.getOwnPropertyDescriptor(Document.prototype, 'visibilityState').get.call(document);

                if (!window.__sdcVisibilityPinned) {
                    window.__sdcVisibilityPinned = true;
                    Object.defineProperty(document, 'hidden', { configurable: true, get: () => false });
                    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
                    document.addEventListener('visibilitychange', e => e.stopImmediatePropagation(), true);
                }

                const v = document.querySelector('video');
                if (!v) {
                    return JSON.stringify({
                        videoFound: false,
                        visibilityState: realVisibility,
                        hasFocus: document.hasFocus(),
                        hidden: realHidden
                    });
                }

                let resumed = false;
                // The audio-only loop pauses the video on purpose; leave it alone then.
                if (v.paused && !v.ended && !window.__sdcAudio) {
                    v.muted = true;
                    v.play().catch(() => {});
                    resumed = true;
                }

                const buffered = [];
                for (let i = 0; i < v.buffered.length; i++)
                    buffered.push([v.buffered.start(i), v.buffered.end(i)]);

                return JSON.stringify({
                    videoFound: true,
                    paused: v.paused,
                    resumed: resumed,
                    ended: v.ended,
                    currentTime: v.currentTime,
                    duration: v.duration,
                    readyState: v.readyState,
                    networkState: v.networkState,
                    muted: v.muted,
                    volume: v.volume,
                    error: v.error ? { code: v.error.code, message: v.error.message } : null,
                    audio: window.__sdcAudio ? {
                        segments: window.__sdcAudio.segments,
                        kilobytes: Math.round(window.__sdcAudio.bytes / 1024),
                        secondsSinceLastSegment: window.__sdcAudio.lastSegmentAt ? Math.round((Date.now() - window.__sdcAudio.lastSegmentAt) / 1000) : null,
                        variant: window.__sdcAudio.variant,
                        lastError: window.__sdcAudio.lastError,
                        viewerFrames: window.__sdcAudio.ws || null,
                        replayPauses: window.__sdcAudio.replayPauses || 0,
                        videos: [...document.querySelectorAll('video')].map(el => ({ paused: el.paused, duration: el.duration, t: Math.round(el.currentTime) })),
                        viewerSocketOpen: !!(window.__sdcViewer && window.__sdcViewer.ws && window.__sdcViewer.ws.readyState === 1)
                    } : null,
                    buffered: buffered,
                    visibilityState: realVisibility,
                    hasFocus: document.hasFocus(),
                    hidden: realHidden
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

            bool audioLoop = root.TryGetProperty("audio", out JsonElement audioEl) && audioEl.ValueKind != JsonValueKind.Null;
            if (audioLoop)
                advanceStatus = "AUDIO_ONLY (video is paused on purpose)";

            _lastCurrentTime = currentTime;
            _lastCheckUtc = nowUtc;

            string message =
                $"video state={advanceStatus} paused={GetBool(root, "paused")} resumed={GetBool(root, "resumed")} ended={GetBool(root, "ended")} " +
                $"currentTime={currentTime:F1} duration={GetDouble(root, "duration"):F1} " +
                $"readyState={GetInt(root, "readyState")} networkState={GetInt(root, "networkState")} " +
                $"muted={GetBool(root, "muted")} volume={GetDouble(root, "volume"):F2} " +
                $"error={(root.TryGetProperty("error", out JsonElement errEl) && errEl.ValueKind != JsonValueKind.Null ? errEl.GetRawText() : "(none)")} " +
                $"buffered={(root.TryGetProperty("buffered", out JsonElement bufEl) ? bufEl.GetRawText() : "[]")} " +
                $"visibilityState={GetString(root, "visibilityState")} hasFocus={GetBool(root, "hasFocus")} hidden={GetBool(root, "hidden")} " +
                $"audio={(audioLoop ? audioEl.GetRawText() : "(off)")}";

            // Abnormal playback is logged at Warn so it reaches shared logs without verbose debug logging enabled.
            // In audio-loop mode the video is paused on purpose, so only the real page visibility is suspicious.
            bool abnormal = audioLoop
                ? GetBool(root, "hidden")
                : advanceStatus.StartsWith("STALLED", StringComparison.Ordinal)
                    || GetBool(root, "paused")
                    || GetBool(root, "resumed")
                    || GetBool(root, "hidden");
            if (abnormal)
                AppLogger.Warn("KickPlayback", message);
            else
                AppLogger.Debug("KickPlayback", message);
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