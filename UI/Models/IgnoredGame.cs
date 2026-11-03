namespace UI.Models
{
    /// <summary>
    /// A game the game filter currently excludes, listed on the inventory page so it can be allowed again.
    /// </summary>
    /// <param name="Slug">The game's category slug.</param>
    /// <param name="Name">The game's display name.</param>
    public sealed record IgnoredGame(string Slug, string Name);
}
