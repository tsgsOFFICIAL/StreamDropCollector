using System.Text.Json;
using Core.Models;

namespace Core.Services.Twitch.Helix
{
    internal static class TwitchHelixChannelParser
    {
        public static LiveChannelSnapshot ParseUser(
            JsonElement user,
            bool isLive,
            IReadOnlyList<string> categorySlugs,
            string? gameId = null)
        {
            string login = user.GetProperty("login").GetString() ?? string.Empty;
            string? displayName = user.TryGetProperty("display_name", out JsonElement displayNameElement)
                ? displayNameElement.GetString()
                : null;
            string? profileImageUrl = user.TryGetProperty("profile_image_url", out JsonElement profileElement)
                ? profileElement.GetString()
                : null;

            return new LiveChannelSnapshot(
                login,
                isLive,
                categorySlugs,
                profileImageUrl,
                displayName,
                gameId);
        }

        public static IReadOnlyList<string> BuildCategorySlugs(string? gameName, string? gameId)
        {
            List<string> slugs = [];

            if (!string.IsNullOrWhiteSpace(gameName))
            {
                slugs.Add(gameName.Trim());
                string slugified = TwitchGameSlugHelper.Slugify(gameName);
                if (!string.IsNullOrWhiteSpace(slugified))
                    slugs.Add(slugified);
            }

            if (!string.IsNullOrWhiteSpace(gameId))
                slugs.Add(gameId.Trim());

            return slugs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}