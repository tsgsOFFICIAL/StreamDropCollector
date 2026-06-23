namespace Core.Mining;

/// <summary>
/// Matches stream category hrefs from the WebView DOM against campaign slugs.
/// </summary>
public static class StreamCategoryMatcher
{
    /// <summary>
    /// Returns whether Twitch category hrefs contain the expected directory path for the campaign slug.
    /// </summary>
    public static bool TwitchMatches(string? rawCategoryHrefs, string? campaignSlug)
    {
        if (string.IsNullOrWhiteSpace(rawCategoryHrefs) || string.IsNullOrWhiteSpace(campaignSlug))
            return false;

        string expectedCategoryPath = $"/directory/category/{campaignSlug}";
        string hrefs = rawCategoryHrefs.Trim().Trim('"');
        return hrefs.Contains(expectedCategoryPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether Kick category hrefs contain the expected path for the campaign slug.
    /// </summary>
    public static bool KickMatches(string? rawCategoryHref, string? campaignSlug)
    {
        if (string.IsNullOrWhiteSpace(rawCategoryHref) || rawCategoryHref == "null")
            return false;

        if (string.IsNullOrWhiteSpace(campaignSlug))
            return true;

        string expectedCategoryPath = $"/category/{campaignSlug}";
        string hrefs = rawCategoryHref.Trim().Trim('"');
        return hrefs.Contains(expectedCategoryPath, StringComparison.OrdinalIgnoreCase);
    }
}