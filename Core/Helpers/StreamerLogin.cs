using Core.Enums;

namespace Core.Helpers
{
    /// <summary>
    /// Validates and cleans streamer logins typed or pasted by the user.
    /// </summary>
    public static class StreamerLogin
    {
        private const int MaxTwitchLength = 25;
        private const int MaxKickLength = 64;

        /// <summary>
        /// Returns whether <paramref name="login"/> only uses characters the platform allows in a channel login.
        /// </summary>
        /// <remarks>
        /// Twitch logins are <c>[a-z0-9_]</c>, and Helix rejects a whole request with 400 when one login in it
        /// breaks that rule. Kick channel slugs also allow <c>-</c>.
        /// </remarks>
        /// <param name="login">Lowercase login to check.</param>
        /// <param name="platform">Platform the login belongs to.</param>
        public static bool IsValid(string? login, Platform platform)
        {
            if (string.IsNullOrEmpty(login))
                return false;

            int maxLength = platform == Platform.Twitch ? MaxTwitchLength : MaxKickLength;
            if (login.Length > maxLength)
                return false;

            foreach (char c in login)
            {
                if (!IsLoginChar(c, platform))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Lowercases a login and trims stray punctuation from its ends (for example the period in "perzzon.").
        /// </summary>
        /// <param name="login">Raw login text.</param>
        /// <param name="platform">Platform the login belongs to.</param>
        /// <returns>The cleaned login, or <see langword="null"/> when what is left is still not a valid login.</returns>
        public static string? Clean(string? login, Platform platform)
        {
            if (string.IsNullOrWhiteSpace(login))
                return null;

            string lower = login.Trim().ToLowerInvariant();
            int start = 0;
            int end = lower.Length;

            while (start < end && !IsLoginChar(lower[start], platform))
                start++;

            while (end > start && !IsLoginChar(lower[end - 1], platform))
                end--;

            string cleaned = lower[start..end];
            return IsValid(cleaned, platform) ? cleaned : null;
        }

        private static bool IsLoginChar(char c, Platform platform) =>
            c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'
            || (platform == Platform.Kick && c == '-');
    }
}
