using System.Net.Http;
using System.Net.Http.Json;
using Core.Logging;
using Core.Enums;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Core.Managers
{
    /// <summary>
    /// Provides Windows toast notifications and webhook delivery for application events.
    /// </summary>
    public class NotificationManager
    {
        private const string BotName = "Stream Drop Collector";
        // Must be a publicly reachable image URL; Discord/Slack fetch it themselves.
        private const string BotAvatarUrl = "https://raw.githubusercontent.com/tsgsOFFICIAL/StreamDropCollector/master/UI/Assets/logo.png";
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

        /// <summary>
        /// Delivers a notification to every enabled channel: a Windows toast (unless quiet mode is on) and the configured webhook.
        /// </summary>
        /// <param name="title">The title text shown in the notification.</param>
        /// <param name="message">The body text shown in the notification.</param>
        /// <param name="type">The kind of notification; decides whether the webhook is allowed to receive it.</param>
        /// <param name="timeoutSeconds">The number of seconds before the toast expires. Defaults to 1 second.</param>
        /// <param name="details">Extra label/value pairs (account, platform, versions...) sent with the webhook only; the toast stays short.</param>
        public static void ShowNotification(string title, string message, NotificationType type, double timeoutSeconds = 1, IReadOnlyList<(string Name, string Value)>? details = null)
        {
            UISettingsManager settings = UISettingsManager.Instance;

            if (!settings.QuietMode)
                ShowToast(title, message, timeoutSeconds);

            if (settings.WebhookEnabled && !string.IsNullOrWhiteSpace(settings.WebhookUrl) && settings.IsWebhookEventEnabled(type))
                _ = SendWebhookAsync(settings.WebhookUrl, title, message, details);
        }

        /// <summary>
        /// Sends a test message to the specified webhook URL.
        /// </summary>
        /// <param name="url">The webhook URL to test.</param>
        /// <returns><see langword="null"/> on success; otherwise a short description of the failure.</returns>
        public static async Task<string?> SendTestWebhookAsync(string url)
        {
            return await SendWebhookAsync(url, "Stream Drop Collector", "Test notification - your webhook is working.");
        }

        private static void ShowToast(string title, string message, double timeoutSeconds)
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show(toast =>
                {
                    toast.ExpirationTime = DateTime.Now.AddSeconds(timeoutSeconds);
                });
        }

        private static async Task<string?> SendWebhookAsync(string url, string title, string message, IReadOnlyList<(string Name, string Value)>? details = null)
        {
            details ??= [];

            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                return "Invalid URL";

            try
            {
                string host = uri.Host;
                object payload;
                if (host.Contains("discord", StringComparison.OrdinalIgnoreCase))
                {
                    payload = new
                    {
                        username = BotName,
                        avatar_url = BotAvatarUrl,
                        embeds = new[]
                        {
                            new
                            {
                                title,
                                description = message,
                                timestamp = DateTime.UtcNow,
                                fields = details.Select(d => new { name = d.Name, value = d.Value, inline = true }).ToArray()
                            }
                        }
                    };
                }
                else if (host.Contains("slack", StringComparison.OrdinalIgnoreCase))
                {
                    string lines = string.Concat(details.Select(d => $"\n*{d.Name}:* {d.Value}"));
                    payload = new { username = BotName, icon_url = BotAvatarUrl, text = $"*{title}*\n{message}{lines}" };
                }
                else
                {
                    payload = new
                    {
                        title,
                        message,
                        timestamp = DateTime.UtcNow,
                        details = details.ToDictionary(d => d.Name, d => d.Value)
                    };
                }

                using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(uri, payload);
                if (response.IsSuccessStatusCode)
                    return null;

                // Never log the URL - it is effectively a secret.
                AppLogger.Warn("Webhook", $"Webhook returned {(int)response.StatusCode} {response.ReasonPhrase}. host={host}");
                return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                AppLogger.Warn("Webhook", $"Webhook send failed. {ex.GetType().Name}: {ex.Message}");
                return ex.Message;
            }
        }
    }
}
