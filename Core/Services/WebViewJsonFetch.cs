using System.Linq;
using System.Text.Json;
using Core.Interfaces;
using Core.Logging;

namespace Core.Services
{
    /// <summary>
    /// Executes same-origin <c>fetch</c> calls inside a WebView2 page context (credentials included).
    /// </summary>
    public static class WebViewJsonFetch
    {
        private const int DefaultTimeoutMs = 20000;

        /// <summary>
        /// Performs a GET request via in-page <c>fetch</c> and returns the response body when successful.
        /// </summary>
        /// <param name="host">WebView host that must already be on the target origin (or <paramref name="originUrl"/> is navigated first).</param>
        /// <param name="url">Absolute URL to fetch.</param>
        /// <param name="originUrl">Optional origin page to navigate to when the host is not already on the required domain.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Response body text, or null on failure.</returns>
        public static async Task<string?> GetAsync(
            IWebViewHost host,
            string url,
            string? originUrl = null,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            if (string.IsNullOrWhiteSpace(url))
                return null;

            ct.ThrowIfCancellationRequested();
            await host.EnsureInitializedAsync();

            if (!string.IsNullOrWhiteSpace(originUrl)
                && !await IsOnOriginAsync(host, originUrl, ct))
            {
                await host.NavigateAsync(originUrl);
            }

            return await ExecuteFetchAsync(host, url, ct);
        }

        private static async Task<bool> IsOnOriginAsync(IWebViewHost host, string originUrl, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!Uri.TryCreate(originUrl, UriKind.Absolute, out Uri? originUri))
                return false;

            string expectedHost = originUri.Host;
            string script = $@"
                (() => {{
                    const host = window.location.hostname || '';
                    return host === {JsonSerializer.Serialize(expectedHost)}
                        || host.endsWith('.' + {JsonSerializer.Serialize(expectedHost)});
                }})()
            ";

            string raw = await host.ExecuteScriptAsync(script);
            return raw.Contains("true", StringComparison.OrdinalIgnoreCase);
        }

        private static async Task<string?> ExecuteFetchAsync(IWebViewHost host, string url, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string asyncBody = $@"
                const response = await fetch({JsonSerializer.Serialize(url)}, {{
                    method: 'GET',
                    credentials: 'include',
                    headers: {{ 'Accept': 'application/json' }}
                }});

                const headersObj = {{}};
                response.headers.forEach((value, key) => {{ headersObj[key] = value; }});

                if (!response.ok) {{
                    return JSON.stringify({{ __fetchError: true, status: response.status, headers: headersObj }});
                }}

                const bodyText = await response.text();
                return JSON.stringify({{ status: response.status, headers: headersObj, body: bodyText }});
            ";

            AppLogger.Debug("WebViewJsonFetch", $"GET START url={url}");
            DateTime startedAtUtc = DateTime.UtcNow;

            try
            {
                string? rawEnvelope = await host.ExecuteAsyncScriptAsync(asyncBody, DefaultTimeoutMs, ct);
                TimeSpan duration = DateTime.UtcNow - startedAtUtc;

                if (string.IsNullOrWhiteSpace(rawEnvelope))
                {
                    AppLogger.Debug("WebViewJsonFetch", $"GET {url} returned null/empty after {duration.TotalMilliseconds:F0}ms.");
                    return null;
                }

                using JsonDocument envelopeDoc = JsonDocument.Parse(rawEnvelope);
                JsonElement envelopeRoot = envelopeDoc.RootElement;
                int status = envelopeRoot.TryGetProperty("status", out JsonElement statusEl) ? statusEl.GetInt32() : -1;
                string headersSummary = envelopeRoot.TryGetProperty("headers", out JsonElement headersEl)
                    ? string.Join(", ", headersEl.EnumerateObject().Select(p => $"{p.Name}={p.Value.GetString()}"))
                    : "(none)";

                if (envelopeRoot.TryGetProperty("__fetchError", out _))
                {
                    AppLogger.Warn("WebViewJsonFetch", $"GET {url} failed in-page after {duration.TotalMilliseconds:F0}ms status={status} headers=[{headersSummary}]");
                    return null;
                }

                string payload = envelopeRoot.TryGetProperty("body", out JsonElement bodyEl) ? bodyEl.GetString() ?? "" : "";

                AppLogger.Debug(
                    "WebViewJsonFetch",
                    $"GET {url} OK durationMs={duration.TotalMilliseconds:F0} status={status} headers=[{headersSummary}] bodyLength={payload.Length} rawBody={payload}");
                return payload;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("WebViewJsonFetch", $"GET {url} script failed: {ex.Message}");
                return null;
            }
        }
    }
}