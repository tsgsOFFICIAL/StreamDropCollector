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

        public const string KickLoginHtmlMarker = "data-testid=\"login\"";
        public const string TwitchLoginHtmlMarker = "data-a-target=\"login-button\"";
        public const string KickLoginButtonSelector = "[data-testid='login']";
        public const string TwitchLoginButtonSelector = "[data-a-target='login-button']";
        public const string TwitchHelixAuthCompleteUrl = "https://www.twitch.tv/settings/connections";

        /// <summary>
        /// Determines whether the user appears logged in based on page HTML.
        /// </summary>
        public static bool IsLoggedInFromHtml(Platform platform, string html)
        {
            bool loggedIn = platform switch
            {
                Platform.Kick => !html.Contains(KickLoginHtmlMarker, StringComparison.OrdinalIgnoreCase),
                Platform.Twitch => !html.Contains(TwitchLoginHtmlMarker, StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            AppLogger.Debug(
                "LoginDetect",
                $"IsLoggedInFromHtml platform={platform} htmlLength={html.Length} loggedIn={loggedIn} " +
                $"(marker={(platform == Platform.Kick ? KickLoginHtmlMarker : platform == Platform.Twitch ? TwitchLoginHtmlMarker : "n/a")})");

            return loggedIn;
        }

        /// <summary>
        /// Builds a script that returns <c>true</c> when the login button is present (page loaded, user not logged in).
        /// </summary>
        public static string BuildHasLoginButtonScript(Platform platform)
        {
            string selector = platform switch
            {
                Platform.Kick => KickLoginButtonSelector,
                Platform.Twitch => TwitchLoginButtonSelector,
                _ => throw new ArgumentOutOfRangeException(nameof(platform))
            };

            string escaped = JsonSerializer.Serialize(selector);
            return $"!!document.querySelector({escaped})";
        }

        /// <summary>
        /// Builds a script that returns <c>true</c> when the login button is absent (user is logged in).
        /// </summary>
        public static string BuildIsLoggedInScript(Platform platform)
        {
            string selector = platform switch
            {
                Platform.Kick => KickLoginButtonSelector,
                Platform.Twitch => TwitchLoginButtonSelector,
                _ => throw new ArgumentOutOfRangeException(nameof(platform))
            };

            string escaped = JsonSerializer.Serialize(selector);
            return $"!document.querySelector({escaped})";
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
        /// Builds a script that returns <c>true</c> when Kick's login button is present but not yet labeled.
        /// </summary>
        public static string BuildKickEarlyLoginButtonReadyScript()
        {
            string escaped = JsonSerializer.Serialize(KickLoginButtonSelector);
            return $$"""
                (() => {
                    const el = document.querySelector({{escaped}});
                    if (!el) return false;
                    const rect = el.getBoundingClientRect();
                    return (el.textContent || '').trim().length === 0 && rect.width > 0 && rect.height > 0;
                })()
                """;
        }

        /// <summary>
        /// Builds a script that clicks Kick's login button when present.
        /// </summary>
        public static string BuildKickLoginButtonClickScript()
        {
            string escaped = JsonSerializer.Serialize(KickLoginButtonSelector);
            return $"document.querySelector({escaped})?.click()";
        }

        /// <summary>
        /// Polls until the platform homepage login button appears, indicating the page has loaded while logged out.
        /// </summary>
        public static async Task<bool> PollUntilLoginButtonPresentAsync(
            Func<string, Task<string>> executeScriptAsync,
            Platform platform,
            int pollIntervalMs = DefaultPollIntervalMs,
            int timeoutMs = 30000,
            CancellationToken cancellationToken = default)
        {
            string script = BuildHasLoginButtonScript(platform);
            int elapsed = 0;

            while (!cancellationToken.IsCancellationRequested && elapsed < timeoutMs)
            {
                if (await executeScriptAsync(script) == "true")
                {
                    AppLogger.Debug("LoginDetect", $"PollUntilLoginButtonPresentAsync platform={platform} FOUND after {elapsed}ms.");
                    return true;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
                elapsed += pollIntervalMs;
            }

            AppLogger.Warn("LoginDetect", $"PollUntilLoginButtonPresentAsync platform={platform} TIMED OUT after {timeoutMs}ms (cancelled={cancellationToken.IsCancellationRequested}).");
            return false;
        }

        /// <summary>
        /// Polls until the platform login button disappears or the timeout/cancellation is reached.
        /// Requires the login button to appear first so an unloaded page is not treated as logged in.
        /// </summary>
        public static async Task<bool> PollUntilLoggedInAsync(
            Func<string, Task<string>> executeScriptAsync,
            Platform platform,
            int pollIntervalMs = DefaultPollIntervalMs,
            int? timeoutMs = null,
            CancellationToken cancellationToken = default)
        {
            if (!await PollUntilLoginButtonPresentAsync(
                    executeScriptAsync,
                    platform,
                    pollIntervalMs,
                    cancellationToken: cancellationToken))
                return false;

            string script = BuildIsLoggedInScript(platform);
            int elapsed = 0;
            int limit = timeoutMs ?? Timeout.Infinite;

            while (!cancellationToken.IsCancellationRequested && (limit == Timeout.Infinite || elapsed < limit))
            {
                if (await executeScriptAsync(script) == "true")
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
        /// Polls until Kick's unlabeled login button is ready, then clicks it.
        /// </summary>
        public static async Task<bool> ClickKickLoginWhenEarlyAsync(
            Func<string, Task<string>> executeScriptAsync,
            int pollIntervalMs = DefaultPollIntervalMs,
            int timeoutMs = 30000,
            CancellationToken cancellationToken = default)
        {
            string isEarlyScript = BuildKickEarlyLoginButtonReadyScript();
            string clickScript = BuildKickLoginButtonClickScript();

            int elapsed = 0;
            while (!cancellationToken.IsCancellationRequested && elapsed < timeoutMs)
            {
                if (await executeScriptAsync(isEarlyScript) == "true")
                {
                    AppLogger.Debug("LoginDetect", $"ClickKickLoginWhenEarlyAsync clicking early login button after {elapsed}ms.");
                    await executeScriptAsync(clickScript);
                    return true;
                }

                await Task.Delay(pollIntervalMs, cancellationToken);
                elapsed += pollIntervalMs;
            }

            AppLogger.Warn("LoginDetect", $"ClickKickLoginWhenEarlyAsync TIMED OUT after {timeoutMs}ms - early login button never appeared.");
            return false;
        }
    }
}