using System.Text.RegularExpressions;

namespace Core.Services.Twitch.Helix
{
    internal static partial class TwitchGameSlugHelper
    {
        public static string Slugify(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string lowered = value.Trim().ToLowerInvariant();
            string slug = NonAlphaNumericRegex().Replace(lowered, "-");
            slug = MultiHyphenRegex().Replace(slug, "-").Trim('-');
            return slug;
        }

        [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
        private static partial Regex NonAlphaNumericRegex();

        [GeneratedRegex(@"-{2,}", RegexOptions.CultureInvariant)]
        private static partial Regex MultiHyphenRegex();
    }
}