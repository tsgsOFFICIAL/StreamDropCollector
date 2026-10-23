using System.Text.Json;
using Core.Enums;
using Core.Logging;

namespace Core.Helpers
{
    /// <summary>
    /// Centralized login detection markers and polling helpers for Kick and Twitch WebView sessions.
    /// Update the markers here when platform UIs change.
    /// </summary>
    public static class PlatformLoginDetector
    {
        public const int DefaultPollIntervalMs = 250;

        public const int DefaultStatePollsRequired = 3;
        public const int DefaultStateTimeoutMs = 20000;

        public const string KickLoginButtonSelector = "[data-testid='login']";
        public const string TwitchLoginButtonSelector = "[data-a-target='login-button']";
        public const string TwitchHelixAuthCompleteUrl = "https://www.twitch.tv/settings/connections";

        // Elements present in the platform header once it has rendered, whether or not the user is logged in.
        private const string KickHeaderRenderedSelector = "[data-testid='navbar-search']";
        private const string TwitchHeaderRenderedSelector = "[data-a-target='top-nav-container']";
        private const string KickSignUpSelector = "[data-testid='sign-up']";

        private const string KickAuthModalSelector = "[data-testid='auth-modal']";
        private const string KickCookieBannerSelector = "#onetrust-banner-sdk";
        private const string KickCookieRejectSelector = "#onetrust-reject-all-handler";
        private const string KickCookieSdkSelector = "#onetrust-consent-sdk";
        private const string TwitchCookieBannerSelector = "[data-a-target='consent-banner']";
        private const string TwitchCookieAcceptSelector = "[data-a-target='consent-banner-accept']";
        private const string TwitchCookieCustomizeSelector = "[data-a-target='consent-banner-manage-preferences']";
        private const int TwitchCookieBannerGraceMs = 8000;
        private const int KickCookieSdkGraceMs = 8000;
        private const int KickLoginClickRetryMs = 2000;

        /// <summary>
        /// Builds a script that returns <c>"loggedIn"</c>, <c>"loggedOut"</c> or <c>"unknown"</c>.
        /// The result is only definitive once the document has loaded and the platform header has rendered,
        /// so an unrendered page is never read as logged in.
        /// </summary>
        public static string BuildLoginStateScript(Platform platform)
        {
            (string login, string rendered, string? extraLoggedOut) = platform switch
            {
                Platform.Kick => (KickLoginButtonSelector, KickHeaderRenderedSelector, KickSignUpSelector),
                Platform.Twitch => (TwitchLoginButtonSelector, TwitchHeaderRenderedSelector, null),
                _ => throw new ArgumentOutOfRangeException(nameof(platform))
            };

            string loginJs = JsonSerializer.Serialize(login);
            string renderedJs = JsonSerializer.Serialize(rendered);
            string extraJs = extraLoggedOut is null ? "null" : JsonSerializer.Serialize(extraLoggedOut);

            return $$"""
                (() => {
                    if (document.readyState !== 'complete') return 'unknown';
                    const visible = el => {
                        const r = el.getBoundingClientRect();
                        return r.width > 0 && r.height > 0;
                    };
                    // Kick renders hidden duplicates of its header buttons, so look at every match.
                    const anyVisible = sel => [...document.querySelectorAll(sel)].some(visible);
                    if (anyVisible({{loginJs}})) return 'loggedOut';
                    const extra = {{extraJs}};
                    if (extra && anyVisible(extra)) return 'loggedOut';
                    return document.querySelector({{renderedJs}}) ? 'loggedIn' : 'unknown';
                })()
                """;
        }

        /// <summary>
        /// Runs the login-state script once. Script failures (for example mid-navigation) are reported as <see cref="LoginState.Unknown"/>.
        /// </summary>
        public static async Task<LoginState> GetLoginStateAsync(
            Func<string, Task<string>> executeScriptAsync,
            Platform platform)
        {
            try
            {
                string raw = await executeScriptAsync(BuildLoginStateScript(platform));
                return raw?.Trim('"') switch
                {
                    "loggedIn" => LoginState.LoggedIn,
                    "loggedOut" => LoginState.LoggedOut,
                    _ => LoginState.Unknown
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Debug("LoginDetect", $"GetLoginStateAsync platform={platform} script failed: {ex.Message}");
                return LoginState.Unknown;
            }
        }

        /// <summary>
        /// Polls until the same definitive state is seen <paramref name="pollsRequired"/> times in a row,
        /// so a transient re-render cannot flip the result. Returns <see cref="LoginState.Unknown"/> on timeout.
        /// </summary>
        public static async Task<LoginState> WaitForLoginStateAsync(
            Func<string, Task<string>> executeScriptAsync,
            Platform platform,
            int pollIntervalMs = DefaultPollIntervalMs,
            int timeoutMs = DefaultStateTimeoutMs,
            int pollsRequired = DefaultStatePollsRequired,
            CancellationToken cancellationToken = default)
        {
            LoginState last = LoginState.Unknown;
            int streak = 0;
            int elapsed = 0;

            while (!cancellationToken.IsCancellationRequested && elapsed < timeoutMs)
            {
                LoginState state = await GetLoginStateAsync(executeScriptAsync, platform);
                streak = state != LoginState.Unknown && state == last ? streak + 1 : state == LoginState.Unknown ? 0 : 1;
                last = state;

                if (streak >= pollsRequired)
                {
                    AppLogger.Debug("LoginDetect", $"WaitForLoginStateAsync platform={platform} state={state} after {elapsed}ms.");
                    return state;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
                elapsed += pollIntervalMs;
            }

            AppLogger.Warn("LoginDetect", $"WaitForLoginStateAsync platform={platform} TIMED OUT after {timeoutMs}ms (cancelled={cancellationToken.IsCancellationRequested}).");
            return LoginState.Unknown;
        }

        /// <summary>
        /// Builds a script that returns <c>true</c> when the user has left the Twitch login page after authenticating.
        /// </summary>
        public static string BuildTwitchLeftLoginPageScript() =>
            "!window.location.pathname.toLowerCase().includes('/login')";

        /// <summary>
        /// Builds a script that returns <c>true</c> when the browser URL starts with the given prefix.
        /// </summary>
        public static string BuildUrlReachedScript(string urlPrefix)
        {
            string escaped = JsonSerializer.Serialize(urlPrefix);
            return $"window.location.href.toLowerCase().startsWith({escaped}.toLowerCase())";
        }

        /// <summary>
        /// Builds a script that returns <c>true</c> when Twitch Helix device-code authorization has finished.
        /// </summary>
        public static string BuildTwitchHelixAuthCompleteScript() =>
            BuildUrlReachedScript(TwitchHelixAuthCompleteUrl);

        /// <summary>
        /// Builds a script that performs one step towards opening Kick's login modal and returns the outcome:
        /// <c>"banner"</c> (cookie banner was visible, rejected it), <c>"modal"</c> (login modal is open),
        /// <c>"clicked"</c> (login button clicked) or <c>"waiting"</c> (page not ready yet).
        /// The cookie banner is dismissed first because, while it is up, Kick's login inputs are blocked.
        /// </summary>
        public static string BuildKickOpenLoginModalStepScript()
        {
            string login = JsonSerializer.Serialize(KickLoginButtonSelector);
            string rendered = JsonSerializer.Serialize(KickHeaderRenderedSelector);
            string modal = JsonSerializer.Serialize(KickAuthModalSelector);
            string banner = JsonSerializer.Serialize(KickCookieBannerSelector);
            string reject = JsonSerializer.Serialize(KickCookieRejectSelector);
            string sdk = JsonSerializer.Serialize(KickCookieSdkSelector);

            return $$"""
                (() => {
                    const visible = el => {
                        const r = el.getBoundingClientRect();
                        return r.width > 0 && r.height > 0;
                    };
                    // Kick renders hidden duplicates of its header buttons, so only consider visible matches.
                    const findVisible = sel => [...document.querySelectorAll(sel)].find(visible);
                    const now = Date.now();
                    window.__kickLoginStart = window.__kickLoginStart || now;

                    if (findVisible({{banner}})) {
                        findVisible({{reject}})?.click();
                        return 'banner';
                    }

                    if (findVisible({{modal}})) return 'modal';
                    if (document.readyState !== 'complete') return 'waiting';

                    const loginBtn = findVisible({{login}});
                    if (!loginBtn || !document.querySelector({{rendered}})) return 'waiting';

                    // The consent SDK loads late; clicking before it does lets its banner appear over the modal.
                    if (!document.querySelector({{sdk}}) && now - window.__kickLoginStart < {{KickCookieSdkGraceMs}}) return 'waiting';

                    // Give a previous click time to open the modal before retrying.
                    if (now - (window.__kickLoginClickAt || 0) < {{KickLoginClickRetryMs}}) return 'waiting';

                    window.__kickLoginClickAt = now;
                    loginBtn.click();
                    return 'clicked';
                })()
                """;
        }

        /// <summary>
        /// Polls until the platform page is stably logged in, or the timeout/cancellation is reached.
        /// An unrendered page counts as unknown, so it is never treated as logged in, and a user who is
        /// already logged in is detected without the login button ever appearing.
        /// </summary>
        public static async Task<bool> PollUntilLoggedInAsync(
            Func<string, Task<string>> executeScriptAsync,
            Platform platform,
            int pollIntervalMs = DefaultPollIntervalMs,
            int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            int elapsed = 0;
            int limit = timeoutMs ?? Timeout.Infinite;
            int streak = 0;

            while (!cancellationToken.IsCancellationRequested && (limit == Timeout.Infinite || elapsed < limit))
            {
                streak = await GetLoginStateAsync(executeScriptAsync, platform) == LoginState.LoggedIn ? streak + 1 : 0;

                if (streak >= DefaultStatePollsRequired)
                {
                    AppLogger.Debug("LoginDetect", $"PollUntilLoggedInAsync platform={platform} LOGGED IN after {elapsed}ms.");
                    return true;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
                if (limit != Timeout.Infinite)
                    elapsed += pollIntervalMs;
            }

            AppLogger.Warn("LoginDetect", $"PollUntilLoggedInAsync platform={platform} TIMED OUT after {elapsed}ms (cancelled={cancellationToken.IsCancellationRequested}).");
            return false;
        }

        /// <summary>
        /// Polls until the browser navigates to a URL with the given prefix.
        /// </summary>
        public static async Task<bool> PollUntilUrlReachedAsync(
            Func<string, Task<string>> executeScriptAsync,
            string urlPrefix,
            int pollIntervalMs = DefaultPollIntervalMs,
            int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            string script = BuildUrlReachedScript(urlPrefix);
            int elapsed = 0;
            int limit = timeoutMs ?? Timeout.Infinite;

            while (!cancellationToken.IsCancellationRequested && (limit == Timeout.Infinite || elapsed < limit))
            {
                if (await executeScriptAsync(script) == "true")
                    return true;

                await Task.Delay(pollIntervalMs, cancellationToken);
                if (limit != Timeout.Infinite)
                    elapsed += pollIntervalMs;
            }

            return false;
        }

        /// <summary>
        /// Polls until the Twitch Helix device-code flow redirects to the connections settings page.
        /// </summary>
        public static Task<bool> PollUntilTwitchHelixAuthCompleteAsync(
            Func<string, Task<string>> executeScriptAsync,
            int pollIntervalMs = DefaultPollIntervalMs,
            int? timeoutMs = null,
            CancellationToken cancellationToken = default) =>
            PollUntilUrlReachedAsync(
                executeScriptAsync,
                TwitchHelixAuthCompleteUrl,
                pollIntervalMs,
                timeoutMs,
                cancellationToken);

        /// <summary>
        /// Polls until the Twitch login page navigation completes after the user authenticates.
        /// </summary>
        public static async Task<bool> PollUntilTwitchLoginCompleteAsync(
            Func<string, Task<string>> executeScriptAsync,
            int pollIntervalMs = DefaultPollIntervalMs,
            int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            string script = BuildTwitchLeftLoginPageScript();
            int elapsed = 0;
            int limit = timeoutMs ?? Timeout.Infinite;

            while (!cancellationToken.IsCancellationRequested && (limit == Timeout.Infinite || elapsed < limit))
            {
                if (await executeScriptAsync(script) == "true")
                    return true;

                await Task.Delay(pollIntervalMs, cancellationToken);
                if (limit != Timeout.Infinite)
                    elapsed += pollIntervalMs;
            }

            return false;
        }

        /// <summary>
        /// Builds a script that rejects Twitch's cookie banner when it is visible and returns <c>"banner"</c>, else <c>"idle"</c>.
        /// Reject has no stable attribute, so it is the banner button that is neither Accept nor Customize.
        /// </summary>
        public static string BuildTwitchRejectCookieBannerScript()
        {
            string banner = JsonSerializer.Serialize(TwitchCookieBannerSelector);
            string accept = JsonSerializer.Serialize(TwitchCookieAcceptSelector);
            string customize = JsonSerializer.Serialize(TwitchCookieCustomizeSelector);

            return $$"""
                (() => {
                    const visible = el => {
                        const r = el.getBoundingClientRect();
                        return r.width > 0 && r.height > 0;
                    };
                    const banner = [...document.querySelectorAll({{banner}})].find(visible);
                    if (!banner) return 'idle';
                    const reject = [...banner.querySelectorAll('button')].find(b =>
                        visible(b) && !b.matches({{accept}}) && !b.matches({{customize}}) && (b.innerText || '').trim().length > 0);
                    reject?.click();
                    return 'banner';
                })()
                """;
        }

        /// <summary>
        /// Rejects Twitch's cookie banner once it appears, so it no longer covers the login form.
        /// Returns <see langword="true"/> when a banner was seen and has gone, <see langword="false"/> if none appeared in time.
        /// </summary>
        public static async Task<bool> RejectTwitchCookieBannerAsync(
            Func<string, Task<string>> executeScriptAsync,
            int pollIntervalMs = DefaultPollIntervalMs,
            CancellationToken cancellationToken = default)
        {
            string script = BuildTwitchRejectCookieBannerScript();
            bool sawBanner = false;
            int elapsed = 0;

            while (!cancellationToken.IsCancellationRequested && (sawBanner || elapsed < TwitchCookieBannerGraceMs))
            {
                string step;
                try
                {
                    step = (await executeScriptAsync(script))?.Trim('"') ?? "";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLogger.Debug("LoginDetect", $"RejectTwitchCookieBannerAsync script failed: {ex.Message}");
                    step = "";
                }

                if (step == "banner" && !sawBanner)
                {
                    sawBanner = true;
                    AppLogger.Debug("LoginDetect", $"RejectTwitchCookieBannerAsync rejecting banner after {elapsed}ms.");
                }
                else if (step == "idle" && sawBanner)
                {
                    AppLogger.Debug("LoginDetect", $"RejectTwitchCookieBannerAsync banner dismissed after {elapsed}ms.");
                    return true;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
                elapsed += pollIntervalMs;
            }

            AppLogger.Debug("LoginDetect", $"RejectTwitchCookieBannerAsync finished after {elapsed}ms (sawBanner={sawBanner}).");
            return false;
        }

        /// <summary>
        /// Rejects Kick's cookie banner, then clicks the login button until the login modal is open.
        /// Retries a click that did not open the modal and keeps rejecting the banner if it appears late.
        /// Returns <see langword="false"/> on timeout (for example when the user is already logged in).
        /// </summary>
        public static async Task<bool> OpenKickLoginModalAsync(
            Func<string, Task<string>> executeScriptAsync,
            int pollIntervalMs = DefaultPollIntervalMs,
            int timeoutMs = 30000,
            CancellationToken cancellationToken = default)
        {
            string script = BuildKickOpenLoginModalStepScript();
            int elapsed = 0;

            while (!cancellationToken.IsCancellationRequested && elapsed < timeoutMs)
            {
                string step;
                try
                {
                    step = (await executeScriptAsync(script))?.Trim('"') ?? "";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLogger.Debug("LoginDetect", $"OpenKickLoginModalAsync script failed: {ex.Message}");
                    step = "waiting";
                }

                if (step is "banner" or "clicked")
                    AppLogger.Debug("LoginDetect", $"OpenKickLoginModalAsync step={step} after {elapsed}ms.");

                if (step == "modal")
                {
                    AppLogger.Debug("LoginDetect", $"OpenKickLoginModalAsync login modal open after {elapsed}ms.");
                    return true;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
                elapsed += pollIntervalMs;
            }

            AppLogger.Warn("LoginDetect", $"OpenKickLoginModalAsync TIMED OUT after {timeoutMs}ms - login modal never opened.");
            return false;
        }
    }
}