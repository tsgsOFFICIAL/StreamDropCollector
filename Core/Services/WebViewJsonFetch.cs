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

                if (!response.ok) {{
                    return JSON.stringify({{ __fetchError: true, status: response.status }});
                }}

                return await response.text();
            ";

            try
            {
                string? payload = await host.ExecuteAsyncScriptAsync(asyncBody, DefaultTimeoutMs, ct);

                if (string.IsNullOrWhiteSpace(payload))
                    return null;

                if (payload.Contains("__fetchError", StringComparison.Ordinal))
                {
                    AppLogger.Warn("WebViewJsonFetch", $"GET {url} failed in-page: {payload}");
                    return null;
                }

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