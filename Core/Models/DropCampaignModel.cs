using Core.Enums;

namespace Core.Models
{
    /// <summary>
    /// Represents a reward available through a drops campaign, including progress and claim status information.
    /// </summary>
    /// <param name="Id">The unique identifier for the reward.</param>
    /// <param name="Name">The display name of the reward.</param>
    /// <param name="ImageUrl">The URL of the image representing the reward, or null if no image is available.</param>
    /// <param name="RequiredMinutes">The total number of minutes required to earn the reward.</param>
    /// <param name="ProgressMinutes">The number of minutes of progress accumulated toward earning the reward. Defaults to 0.</param>
    /// <param name="IsClaimed">true if the reward has been claimed; otherwise, false. Defaults to false.</param>
    /// <param name="DropInstanceId">The identifier of the specific drop instance associated with this reward, or null if not applicable.</param>
    public record DropsReward(
        string Id,
        string Name,
        string? ImageUrl,
        int RequiredMinutes,
        int ProgressMinutes = 0,
        bool IsClaimed = false,
        string? DropInstanceId = null);
    /// <summary>
    /// Represents a campaign that offers in-game rewards through a drops program for a specific game and platform.
    /// </summary>
    /// <param name="Id">The unique identifier for the drops campaign.</param>
    /// <param name="Name">The display name of the drops campaign.</param>
    /// <param name="GameName">The name of the game associated with the campaign.</param>
    /// <param name="GameImageUrl">The URL of the image representing the game. Can be null if no image is available.</param>
    /// <param name="StartsAt">The date and time when the campaign becomes active, in UTC.</param>
    /// <param name="EndsAt">The date and time when the campaign ends, in UTC.</param>
    /// <param name="Rewards">A read-only list of rewards available in this campaign. Cannot be null or empty.</param>
    /// <param name="Platform">The platform on which the campaign is available.</param>
    /// <param name="ConnectUrls">A read-only list of URLs that users can use to connect their accounts for eligibility. Cannot be null.</param>
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
