using System.Text.Json;
using Core.Enums;
using Core.Interfaces;
using Core.Logging;

namespace Core.Mining.Twitch;

/// <summary>
/// Reads Twitch stream page state (online, category, quality, mature gate) via the hidden WebView.
/// </summary>
public sealed class TwitchStreamPageReader
{
    /// <summary>Public web Client-ID used by twitch.tv for gql.twitch.tv requests.</summary>
    private const string TwitchWebClientId = "kimne78kx3ncx6brgo4mv6wki5h1ko";

    private readonly IWebViewHost? _webViewHost;
    private readonly WebViewUiRunner _uiRunner;
    private readonly Func<AudioLoopMode> _loopMode;

    /// <summary>
    /// Initializes a new instance of the <see cref="TwitchStreamPageReader"/> class.
    /// </summary>
    /// <param name="loopMode">Returns the stream loop mode for this account.</param>
    public TwitchStreamPageReader(IWebViewHost? webViewHost, WebViewUiRunner uiRunner, Func<AudioLoopMode> loopMode)
    {
        _webViewHost = webViewHost;
        _uiRunner = uiRunner;
        _loopMode = loopMode;
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

        if (_loopMode() != AudioLoopMode.Off)
        {
            await StartAudioOnlyAsync(null, ct);
            await Task.Delay(3000, ct);
        }

        await LogPlayerStateAsync();
    }

    /// <summary>
    /// Starts the in-page stream loop and pauses the video player. The loop polls the <c>audio_only</c> HLS playlist and
    /// downloads each new segment, or only sends HEAD requests for them in HEAD-only mode.
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
            string? authToken = await _uiRunner.RunAsync(() => _webViewHost.GetCookieValueAsync("https://twitch.tv", "auth-token"));
            if (string.IsNullOrEmpty(authToken))
            {
                // Anonymous playback tokens work but earn no drops, so fall back to video.
                AppLogger.Warn("TwitchSelection", "[Twitch] Audio-only skipped: no auth-token cookie. Using video.");
                return false;
            }

            string body = AudioOnlyScript
                .Replace("__CLIENT_ID__", JsonSerializer.Serialize(TwitchWebClientId))
                .Replace("__OAUTH__", JsonSerializer.Serialize($"OAuth {authToken}"))
                .Replace("__LOGIN__", JsonSerializer.Serialize(login ?? string.Empty))
                .Replace("__HEAD_ONLY__", _loopMode() == AudioLoopMode.HeadOnly ? "true" : "false");

            string? result = await _uiRunner.RunAsync(() => _webViewHost.ExecuteAsyncScriptAsync(body, 25000, ct));
            AppLogger.Debug("TwitchSelection", $"[Twitch] Audio-only start: {result ?? "(no result / timeout)"}");

            return result != null && result.Contains("\"ok\":true", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Warn("TwitchSelection", $"Failed starting Twitch audio-only mining. {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restarts the stream loop when a page reload removed it or it stopped fetching segments. Called from the health tick.
    /// </summary>
    /// <param name="login">The channel currently being mined.</param>
    public async Task EnsureAudioOnlyAsync(string login, CancellationToken ct = default)
    {
        if (_webViewHost == null || _loopMode() == AudioLoopMode.Off)
            return;

        const string stateJs = """
            (() => {
                const a = window.__sdcAudio;
                return JSON.stringify({
                    path: location.pathname,
                    alive: !!a && !a.stopped,
                    age: a && a.lastSegmentAt ? Math.round((Date.now() - a.lastSegmentAt) / 1000) : null
                });
            })();
            """;

        const string stopJs = """
            (() => {
                const a = window.__sdcAudio;
                if (a) {
                    a.stopped = true;
                    clearTimeout(a.timer);
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
            JsonElement ageElement = state.RootElement.GetProperty("age");
            int? age = ageElement.ValueKind == JsonValueKind.Number ? ageElement.GetInt32() : null;

            if (!string.Equals(path.Trim('/'), login, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Debug("TwitchSelection", $"[Twitch] Audio-only watchdog skipped: page is at '{path}', not on '{login}'.");
                return;
            }

            if (alive && age is <= 30)
                return;

            AppLogger.Warn("TwitchSelection", $"[Twitch] Audio-only watchdog restarting the loop (alive={alive}, secondsSinceLastSegment={age?.ToString() ?? "none"}).");

            if (alive)
                await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(stopJs));

            await StartAudioOnlyAsync(login, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Warn("TwitchSelection", $"Audio-only watchdog failed. {ex.Message}");
        }
    }

    /// <summary>
    /// Logs the video element and stream loop state after the page is prepared.
    /// </summary>
    private async Task LogPlayerStateAsync()
    {
        if (_webViewHost == null)
            return;

        const string js = """
            (() => {
                const v = document.querySelector('video');
                const a = window.__sdcAudio;
                return JSON.stringify({
                    quality: localStorage.getItem('video-quality'),
                    hasVideo: !!v,
                    paused: v ? v.paused : null,
                    currentTime: v ? Math.round(v.currentTime) : null,
                    videoWidth: v ? v.videoWidth : null,
                    videoHeight: v ? v.videoHeight : null,
                    audio: a ? {
                        segments: a.segments,
                        kilobytes: Math.round(a.bytes / 1024),
                        secondsSinceLastSegment: a.lastSegmentAt ? Math.round((Date.now() - a.lastSegmentAt) / 1000) : null,
                        lastError: a.lastError
                    } : null
                });
            })();
            """;

        try
        {
            string state = await _uiRunner.RunAsync(() => _webViewHost.ExecuteScriptAsync(js));
            AppLogger.Debug("TwitchSelection", $"[Twitch] Player state after prepare: {state}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("TwitchSelection", $"Failed reading Twitch player state. {ex.Message}");
        }
    }

    // Token and usher steps follow twitch-audio-web (fetch.ts, url.ts). Segments are never played, only downloaded or HEAD-requested.
    private const string AudioOnlyScript = """
        const fail = (error) => JSON.stringify({ ok: false, error });
        const expectedLogin = __LOGIN__;
        const pathLogin = location.pathname.split('/').filter(Boolean)[0];
        const login = expectedLogin || pathLogin;
        if (!login)
            return fail('no channel in the page url');
        if (expectedLogin && (pathLogin || '').toLowerCase() !== expectedLogin.toLowerCase())
            return fail('page is not on the mined channel: ' + location.pathname);
        if (window.__sdcAudio)
            return JSON.stringify({ ok: true, already: true });

        const headers = { 'Client-ID': __CLIENT_ID__, 'Content-Type': 'text/plain; charset=UTF-8' };
        const oauth = __OAUTH__;
        if (oauth)
            headers['Authorization'] = oauth;

        const payload = {
            operationName: 'PlaybackAccessToken_Template',
            query: 'query PlaybackAccessToken_Template($login: String!, $isLive: Boolean!, $vodID: ID!, $isVod: Boolean!, $playerType: String!) {  streamPlaybackAccessToken(channelName: $login, params: {platform: "android", playerBackend: "mediaplayer", playerType: $playerType}) @include(if: $isLive) {    value    signature    __typename  }  videoPlaybackAccessToken(id: $vodID, params: {platform: "android", playerType: $playerType}) @include(if: $isVod) {    value    signature    __typename  }}',
            variables: { isLive: true, login, isVod: false, vodID: '', playerType: 'mobile' }
        };

        // Token -> usher master playlist -> the audio_only media playlist URL.
        const resolveAudioPlaylistUrl = async () => {
            const gqlResponse = await fetch('https://gql.twitch.tv/gql', { method: 'POST', headers, body: JSON.stringify(payload) });
            const token = (await gqlResponse.json())?.data?.streamPlaybackAccessToken;
            if (!token?.value || !token?.signature)
                throw new Error('no playback access token (offline or not logged in)');

            const usher = new URL('https://usher.ttvnw.net/api/channel/hls/' + login + '.m3u8');
            usher.searchParams.set('allow_audio_only', 'true');
            usher.searchParams.set('token', token.value);
            usher.searchParams.set('sig', token.signature);
            usher.searchParams.set('p', String(Math.floor(Math.random() * 1000000)));
            usher.searchParams.set('player', 'twitchweb');
            usher.searchParams.set('allow_source', 'true');
            usher.searchParams.set('type', 'any');

            const master = await fetch(usher.toString());
            if (!master.ok)
                throw new Error('usher returned ' + master.status);

            let audioOnlyFound = false;
            for (const line of (await master.text()).split('\n')) {
                if (line.includes('audio_only'))
                    audioOnlyFound = true;
                if (audioOnlyFound && line.startsWith('https://'))
                    return line.trim();
            }
            throw new Error('no audio_only stream in the playlist');
        };

        const state = { segments: 0, bytes: 0, lastSegmentAt: null, lastError: null, stopped: false, timer: null, bigSegments: 0, maxSegmentKb: 0, adPolls: 0 };
        window.__sdcAudio = state;

        // HEAD-only mode: HEAD requests for new segments and a slower playlist poll.
        const headOnly = __HEAD_ONLY__;
        const pollMs = headOnly ? 10000 : 2000;
        state.mode = headOnly ? 'head' : 'audio';

        const seen = new Set();
        let playlistUrl = null;
        let failures = 0;
        let first = true;

        const poll = async () => {
            if (state.stopped)
                return;
            try {
                if (!playlistUrl)
                    playlistUrl = await resolveAudioPlaylistUrl();

                const response = await fetch(playlistUrl);
                if (!response.ok)
                    throw new Error('playlist returned ' + response.status);

                const playlistText = await response.text();
                if (/stitched-ad|X-TV-TWITCH-AD/i.test(playlistText))
                    state.adPolls++;
                const urls = playlistText.split('\n').map(l => l.trim()).filter(l => l.startsWith('https://'));
                // On the first poll only take the newest segment instead of downloading the whole backlog.
                const fresh = urls.filter(u => !seen.has(u));
                const toFetch = first ? fresh.slice(-1) : fresh;
                fresh.forEach(u => seen.add(u));
                first = false;

                for (const url of toFetch) {
                    if (headOnly) {
                        const probe = await fetch(url, { method: 'HEAD' });
                        if (!probe.ok)
                            throw new Error('segment HEAD returned ' + probe.status);
                        state.segments++;
                        state.lastSegmentAt = Date.now();
                        continue;
                    }
                    const segment = await fetch(url);
                    if (!segment.ok)
                        throw new Error('segment returned ' + segment.status);
                    const size = (await segment.arrayBuffer()).byteLength;
                    state.bytes += size;
                    // Audio segments are ~50 KB; larger ones are counted separately.
                    if (size > 300 * 1024) {
                        state.bigSegments++;
                        state.maxSegmentKb = Math.max(state.maxSegmentKb, Math.round(size / 1024));
                    }
                    state.segments++;
                    state.lastSegmentAt = Date.now();
                }

                if (seen.size > 200)
                    [...seen].slice(0, 100).forEach(u => seen.delete(u));
                failures = 0;
                state.lastError = null;
            } catch (e) {
                state.lastError = String(e.message || e);
                // Fetch a fresh token and playlist after repeated failures (expired token, stream restart).
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
            window.__sdcAudio = null;
            return fail(state.lastError);
        }

        // Pause the video through Twitch's own play/pause button.
        const playButton = document.querySelector("button[data-a-target='player-play-pause-button']");
        const videoWasPlaying = playButton?.getAttribute('data-a-player-state') === 'playing';
        if (videoWasPlaying)
            playButton.click();

        return JSON.stringify({ ok: true, login, segments: state.segments, videoWasPlaying, hasPlayButton: !!playButton });
        """;
}
