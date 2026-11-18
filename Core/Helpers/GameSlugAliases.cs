using Core.Services.Twitch.Helix;

namespace Core.Helpers
{
    /// <summary>
    /// Collapses the several slugs one game can be reported under (Twitch's own slug for a campaign versus one generated from
    /// the display name) into a single selectable entry.
    /// </summary>
    public static class GameSlugAliases
    {
        /// <summary>
        /// Maps every slug that shares its display name with another slug to the one that should represent the game.
        /// Slugs that have no twin are not in the result.
        /// </summary>
        /// <param name="games">Every known slug with its display name.</param>
        /// <param name="liveSlugs">Slugs real campaigns currently use; these are the ones filtering matches against.</param>
        public static Dictionary<string, string> BuildAliasMap(
            IEnumerable<(string Slug, string Name)> games,
            IReadOnlySet<string>? liveSlugs = null)
        {
            Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);

            foreach (IGrouping<string, (string Slug, string Name)> group in games
                         .Where(g => !string.IsNullOrWhiteSpace(g.Slug) && !string.IsNullOrWhiteSpace(g.Name))
                         .GroupBy(g => g.Name.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                List<string> slugs = [.. group.Select(g => g.Slug.Trim().ToLowerInvariant()).Distinct()];
                if (slugs.Count < 2)
                    continue;

                string generated = TwitchGameSlugHelper.Slugify(group.Key);
                string canonical =
                    slugs.FirstOrDefault(s => liveSlugs?.Contains(s) == true)
                    ?? slugs.FirstOrDefault(s => !string.Equals(s, generated, StringComparison.OrdinalIgnoreCase))
                    ?? slugs[0];

                foreach (string slug in slugs.Where(s => s != canonical))
                    aliases[slug] = canonical;
            }

            return aliases;
        }
    }
}
