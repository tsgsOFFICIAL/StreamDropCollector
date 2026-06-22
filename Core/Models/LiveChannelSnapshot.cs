namespace Core.Models
{
    /// <summary>
    /// Normalized live-channel metadata from platform HTTP APIs (Kick v2 channels, Twitch equivalent).
    /// </summary>
    public sealed record LiveChannelSnapshot(
        string Login,
        bool IsLive,
        IReadOnlyList<string> CategorySlugs,
        string? ProfileImageUrl,
        string? DisplayName);
}