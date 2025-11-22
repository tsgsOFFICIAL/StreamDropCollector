using Core.Enums;

namespace Core.Models
{
    /// <summary>
    /// Represents a reward that can be earned by watching a stream for a specified number of minutes.
    /// </summary>
    /// <param name="Id">The unique identifier for the reward.</param>
    /// <param name="Name">The display name of the reward.</param>
    /// <param name="ImageUrl">The URL of the image representing the reward, or <see langword="null"/> if no image is available.</param>
    /// <param name="RequiredMinutes">The number of minutes a viewer must watch to earn the reward. Must be non-negative.</param>
    public record DropsReward(
        string Id,
        string Name,
        string? ImageUrl,
        int RequiredMinutes);
    /// <summary>
    /// Represents a campaign for in-game item drops, including details about the associated game, rewards, and
    /// platform.
    /// </summary>
    /// <param name="Id">The unique identifier for the drops campaign.</param>
    /// <param name="Name">The display name of the drops campaign.</param>
    /// <param name="GameName">The name of the game associated with the campaign.</param>
    /// <param name="GameImageUrl">The URL of the image representing the game. Can be null if no image is available.</param>
    /// <param name="StartsAt">The date and time when the campaign begins, in UTC.</param>
    /// <param name="EndsAt">The date and time when the campaign ends, in UTC.</param>
    /// <param name="Rewards">A read-only list of rewards available in the campaign. Cannot be null.</param>
    /// <param name="Platform">The platform on which the campaign is available.</param>
    /// <param name="ConnectUrl">An optional URL for users to connect their accounts or learn more about the campaign. Can be null if not
    /// applicable.</param>
    public record DropsCampaign(
        string Id,
        string Name,
        string GameName,
        string? GameImageUrl,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        IReadOnlyList<DropsReward> Rewards,
        Platform Platform,
       IReadOnlyList<string> ConnectUrls);
}
