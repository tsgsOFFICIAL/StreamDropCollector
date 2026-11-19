using System.Text.RegularExpressions;

namespace Core.Helpers
{
    /// <summary>
    /// Reads the participating categories out of a Twitch open reward campaign's free-text summary.
    /// </summary>
    /// <remarks>
    /// Twitch gives no structured category list for these campaigns; the summary carries a sentence such as
    /// "Participating categories include: Just Chatting, Art, Pokémon GO".
    /// </remarks>
    public static partial class RewardCampaignCategories
    {
        /// <summary>
        /// Extracts the category names listed after "Participating categories include:".
        /// </summary>
        /// <param name="summary">The campaign summary, or <see langword="null"/>.</param>
        /// <returns>The category names in the order listed; empty when the summary names none.</returns>
        public static IReadOnlyList<string> Parse(string? summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return [];

            Match match = ParticipatingRegex().Match(summary);
            if (!match.Success)
                return [];

            return [.. match.Groups["list"].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => name.Trim().TrimEnd('.').Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>
        /// Extracts a loose category hint from a summary that names its category in prose rather than in a list, such as
        /// "streamers in the GTAVI Category" or "watching GTAVI streamers". The hint is a search term, not an exact category name.
        /// </summary>
        /// <param name="summary">The campaign summary or description, or <see langword="null"/>.</param>
        /// <returns>The hint, or <see langword="null"/> when the text names no category.</returns>
        public static string? ParseHint(string? summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return null;

            Match match = InCategoryRegex().Match(summary);
            if (!match.Success)
                match = WatchingStreamersRegex().Match(summary);

            string hint = match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
            return hint.Length >= 2 ? hint : null;
        }

        /// <summary>
        /// Returns whether a summary sentence names the reward as something to unlock (for example "...you'll unlock the rare
        /// Golden Iguana Badge"). The wording alone proves nothing; the caller must also have a structural reason.
        /// </summary>
        /// <param name="summary">The campaign summary, or <see langword="null"/>.</param>
        /// <param name="rewardName">The reward's display name.</param>
        public static bool IsUnlockedByOtherRewards(string? summary, string? rewardName)
        {
            if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(rewardName))
                return false;

            return summary
                .Split(['.', '!', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(sentence => sentence.Contains(rewardName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && sentence.Contains("unlock", StringComparison.OrdinalIgnoreCase));
        }

        [GeneratedRegex(@"streamers\s+in\s+the\s+(?<name>[^.,;\r\n]{2,60}?)\s+category", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex InCategoryRegex();

        [GeneratedRegex(@"watch(?:ing)?\s+(?<name>[^\s.,;]{2,40})\s+streamers", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex WatchingStreamersRegex();

        // The list runs to the end of its line; a trailing full stop belongs to the sentence, not the last name.
        [GeneratedRegex(@"participating\s+categories\s+include\s*:\s*(?<list>[^\r\n]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex ParticipatingRegex();
    }
}