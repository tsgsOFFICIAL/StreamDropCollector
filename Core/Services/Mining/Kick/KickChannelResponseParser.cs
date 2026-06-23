using System.Text.Json;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Kick
{
    /// <summary>
    /// Parses Kick <c>GET /api/v2/channels/{slug}</c> JSON into <see cref="LiveChannelSnapshot"/>.
    /// </summary>
    public static class KickChannelResponseParser
    {
        /// <summary>
        /// Parses a Kick channel API JSON payload into a <see cref="LiveChannelSnapshot"/>.
        /// </summary>
        /// <param name="json">Raw JSON body from <c>GET /api/v2/channels/{slug}</c>.</param>
        /// <param name="login">Fallback login slug when the payload omits <c>slug</c>.</param>
        /// <returns>A normalized snapshot, or <see langword="null"/> when parsing fails.</returns>
        public static LiveChannelSnapshot? Parse(string json, string login)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                return Parse(doc.RootElement, login);
            }
            catch (JsonException ex)
            {
                AppLogger.Warn("KickChannelApi", $"Failed to parse Kick channel JSON for '{login}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses a Kick channel API JSON root element into a <see cref="LiveChannelSnapshot"/>.
        /// </summary>
        /// <param name="root">Parsed JSON root from the channel API response.</param>
        /// <param name="login">Fallback login slug when the payload omits <c>slug</c>.</param>
        /// <returns>A normalized snapshot with live state and category slugs.</returns>
        public static LiveChannelSnapshot? Parse(JsonElement root, string login)
        {
            string? responseSlug = root.TryGetProperty("slug", out JsonElement slugElement)
                ? slugElement.GetString()
                : login;

            string resolvedLogin = string.IsNullOrWhiteSpace(responseSlug) ? login : responseSlug;

            string? displayName = null;
            string? profileImageUrl = null;
            if (root.TryGetProperty("user", out JsonElement user) && user.ValueKind == JsonValueKind.Object)
            {
                displayName = user.TryGetProperty("username", out JsonElement username)
                    ? username.GetString()
                    : null;
                profileImageUrl = user.TryGetProperty("profile_pic", out JsonElement profilePic)
                    ? profilePic.GetString()
                    : null;
            }

            List<string> categorySlugs = [];
            bool isLive = false;

            if (root.TryGetProperty("livestream", out JsonElement livestream)
                && livestream.ValueKind == JsonValueKind.Object)
            {
                if (livestream.TryGetProperty("is_live", out JsonElement isLiveElement)
                    && isLiveElement.ValueKind == JsonValueKind.False)
                {
                    isLive = false;
                }
                else
                {
                    isLive = true;
                }

                if (livestream.TryGetProperty("categories", out JsonElement categories)
                    && categories.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement category in categories.EnumerateArray())
                    {
                        if (!category.TryGetProperty("slug", out JsonElement categorySlug))
                            continue;

                        string? slugValue = categorySlug.GetString();
                        if (!string.IsNullOrWhiteSpace(slugValue))
                            categorySlugs.Add(slugValue);
                    }
                }
            }

            LiveChannelSnapshot snapshot = new(
                resolvedLogin,
                isLive,
                categorySlugs,
                profileImageUrl,
                displayName);

            AppLogger.Debug(
                "KickChannelApi",
                $"Channel '{resolvedLogin}' parsed | live={isLive} | categories=[{string.Join(", ", categorySlugs)}] | has profile picture={(profileImageUrl != null ? "yes" : "no")}");

            return snapshot;
        }
    }
}