namespace Core.Models
{
    /// <summary>
    /// Normalized live-channel metadata from platform HTTP APIs (Kick v2 channels, Twitch Helix).
    /// </summary>
    /// <param name="Login">Channel login slug.</param>
    /// <param name="IsLive">Whether the channel is currently broadcasting.</param>
    /// <param name="CategorySlugs">Game/category labels from the platform API (display names and slugs).</param>
    /// <param name="ProfileImageUrl">Avatar URL from the platform API, or null when unavailable.</param>
    /// <param name="DisplayName">Human-readable channel name from the platform API, or null when unavailable.</param>
    /// <param name="GameId">Platform game identifier when available (Twitch Helix <c>game_id</c>).</param>
    public sealed record LiveChannelSnapshot(
        string Login,
        bool IsLive,
        IReadOnlyList<string> CategorySlugs,
        string? ProfileImageUrl,
        string? DisplayName,
        string? GameId = null);
}