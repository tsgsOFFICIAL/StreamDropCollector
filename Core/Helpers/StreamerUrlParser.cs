using Core.Logging;

namespace Core.Helpers
{
    /// <summary>
    /// Parses streamer logins from Kick and Twitch channel URLs.
    /// </summary>
    public static class StreamerUrlParser
    {
        /// <summary>
        /// Extracts the first path segment from a channel URL (the streamer login).
        /// </summary>
        /// <param name="url">Absolute Kick or Twitch channel URL.</param>
        /// <returns>The login slug, or an empty string when parsing fails.</returns>
        public static string GetLoginFromUrl(string url)
        {
            try
            {
                Uri uri = new(url);
                string path = uri.AbsolutePath.Trim('/');
                return path.Split('/')[0];
            }
            catch (Exception ex)
            {
                AppLogger.Warn("StreamSelection", $"Failed extracting streamer name from url '{url}'. {ex.Message}");
                return string.Empty;
            }
        }
    }
}