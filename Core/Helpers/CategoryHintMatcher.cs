using System.Text.RegularExpressions;

namespace Core.Helpers
{
    /// <summary>
    /// Matches a loose category hint from a campaign's prose (such as "GTAVI") against real Twitch category names.
    /// </summary>
    /// <remarks>
    /// Twitch search ranks "GTAVI" by text similarity, so it can put "Grand Theft Auto V" ahead of "Grand Theft Auto VI".
    /// A category only counts as the hint's category when its name contains the hint or abbreviates to it.
    /// </remarks>
    public static partial class CategoryHintMatcher
    {
        /// <summary>
        /// Builds the search terms worth trying for a hint, most literal first: the hint itself, then with a trailing
        /// roman or arabic numeral split off ("GTAVI" becomes "GTA VI").
        /// </summary>
        public static IReadOnlyList<string> BuildSearchTerms(string hint)
        {
            string trimmed = hint.Trim();
            List<string> terms = [trimmed];

            Match split = TrailingNumberRegex().Match(trimmed);
            if (split.Success && !trimmed.Contains(' '))
                terms.Add($"{split.Groups["head"].Value} {split.Groups["tail"].Value}");

            return terms;
        }

        /// <summary>
        /// Returns whether a category name is the one the hint refers to.
        /// </summary>
        public static bool IsMatch(string hint, string categoryName)
        {
            string wanted = Normalize(hint);
            if (wanted.Length < 2)
                return false;

            // "Pokemon" finds "Pokémon GO" and "Rocket League" finds "Rocket League": the name contains the hint.
            if (Normalize(categoryName).Contains(wanted, StringComparison.Ordinal))
                return true;

            return string.Equals(Acronym(categoryName), wanted, StringComparison.Ordinal);
        }

        /// <summary>
        /// "Grand Theft Auto VI (GTA)" becomes "gtavi": the first letter of every word, with a numeral kept whole.
        /// </summary>
        private static string Acronym(string name)
        {
            string withoutNote = ParenthesesRegex().Replace(name, " ");
            List<string> words = [.. WordRegex().Matches(withoutNote).Select(m => m.Value)];

            return string.Concat(words.Select(w => IsNumeral(w) ? w : w[..1])).ToLowerInvariant();
        }

        private static bool IsNumeral(string word) => NumeralRegex().IsMatch(word);

        private static string Normalize(string value) =>
            new([.. RemoveDiacritics(value).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

        private static string RemoveDiacritics(string value) =>
            new([.. value.Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)]);

        [GeneratedRegex(@"^(?<head>[A-Za-z]{2,}?)(?<tail>XII|XI|X|IX|VIII|VII|VI|V|IV|III|II|\d{1,3})$", RegexOptions.CultureInvariant)]
        private static partial Regex TrailingNumberRegex();

        [GeneratedRegex(@"\([^)]*\)")]
        private static partial Regex ParenthesesRegex();

        [GeneratedRegex(@"[\p{L}\p{N}]+")]
        private static partial Regex WordRegex();

        [GeneratedRegex(@"^(?:[IVX]{1,5}|\d+)$", RegexOptions.CultureInvariant)]
        private static partial Regex NumeralRegex();
    }
}
