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
    /// <param name="IsCurrentReward">true if this reward is currently being progressed; otherwise, false. Defaults to false.</param>
    public record DropsReward(
        string Id,
        string Name,
        string? ImageUrl,
        int RequiredMinutes,
        int ProgressMinutes = 0,
        bool IsClaimed = false,
        string? DropInstanceId = null,
        bool IsCurrentReward = false)
    {
        /// <summary>
        /// Gets the minutes of watch time still needed to earn this reward (0 once claimed or complete).
        /// </summary>
        public int RemainingMinutes => IsClaimed ? 0 : Math.Max(0, RequiredMinutes - ProgressMinutes);

        /// <summary>
        /// Gets a short "time to complete" label, such as "2h 15m left", "Ready to claim" or "Done".
        /// </summary>
        public string TimeToCompleteText => IsClaimed ? "Done" : DurationFormatter.FormatRemaining(RemainingMinutes);
    }

    /// <summary>
    /// Formats minute counts for display.
    /// </summary>
    public static class DurationFormatter
    {
        /// <summary>Formats a minute count as "1d 2h", "2h 15m" or "45m".</summary>
        public static string Format(int minutes)
        {
            if (minutes <= 0)
                return "0m";

            int days = minutes / 1440;
            int hours = minutes % 1440 / 60;
            int mins = minutes % 60;

            if (days > 0)
                return hours > 0 ? $"{days}d {hours}h" : $"{days}d";

            if (hours > 0)
                return mins > 0 ? $"{hours}h {mins}m" : $"{hours}h";

            return $"{mins}m";
        }

        /// <summary>Formats a second count as a live countdown such as "1h 12m 05s", "12m 05s" or "45s".</summary>
        public static string FormatCountdown(int seconds)
        {
            if (seconds <= 0)
                return string.Empty;

            int hours = seconds / 3600;
            int mins = seconds % 3600 / 60;
            int secs = seconds % 60;

            if (hours > 0)
                return $"{hours}h {mins:D2}m {secs:D2}s";

            return mins > 0 ? $"{mins}m {secs:D2}s" : $"{secs}s";
        }

        /// <summary>Formats the remaining minutes as a "left" label, or "Ready to claim" when none remain.</summary>
        public static string FormatRemaining(int minutes) =>
            minutes <= 0 ? "Ready to claim" : $"{Format(minutes)} left";
    }
    /// <summary>
    /// Represents a campaign that offers in-game rewards through a drops program for a specific game and platform.
    /// </summary>
    /// <param name="Id">The unique identifier for the drops campaign.</param>
    /// <param name="Name">The display name of the drops campaign.</param>
    /// <param name="Slug">A URL-friendly identifier for the campaign, often used in API endpoints or web URLs.</param>
    /// <param name="GameId">The platform game identifier when available (Twitch Helix game id from drops inventory).</param>
    /// <param name="GameName">The name of the game associated with the campaign.</param>
    /// <param name="GameImageUrl">The URL of the image representing the game. Can be null if no image is available.</param>
    /// <param name="StartsAt">The date and time when the campaign becomes active, in UTC.</param>
    /// <param name="EndsAt">The date and time when the campaign ends, in UTC.</param>
    /// <param name="Rewards">A read-only list of rewards available in this campaign. Cannot be null or empty.</param>
    /// <param name="Platform">The platform on which the campaign is available.</param>
    /// <param name="ConnectUrls">A read-only list of URLs that users can use to connect their accounts for eligibility. Cannot be null.</param>
    /// <param name="IsGeneralDrop">true if the campaign is a general (non-game-specific) drop event; otherwise, false.</param>
    /// <param name="IsCurrentCampaign">true if this campaign is currently being mined; otherwise, false. Defaults to false.</param>
    public record DropsCampaign(
        string Id,
        string Name,
        string Slug,
        string? GameId,
        string GameName,
        string? GameImageUrl,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        IReadOnlyList<DropsReward> Rewards,
        Platform Platform,
        IReadOnlyList<string> ConnectUrls,
        bool IsGeneralDrop,
        bool IsCurrentCampaign = false)
    {
        /// <summary>
        /// Gets a value indicating whether every reward in the campaign has been claimed.
        /// </summary>
        public bool AllRewardsClaimed => Rewards.All(r => r.IsClaimed);

        /// <summary>
        /// Gets the minutes of watch time still needed to finish every reward in the campaign.
        /// </summary>
        /// <remarks>Kick tracks one progress counter per campaign, so the longest remaining reward decides the total;
        /// Twitch tracks each drop separately, so the remaining minutes add up.</remarks>
        public int RemainingMinutes => Platform == Platform.Kick
            ? Rewards.Select(r => r.RemainingMinutes).DefaultIfEmpty(0).Max()
            : Rewards.Sum(r => r.RemainingMinutes);

        /// <summary>
        /// Gets a short "time to complete" label for the whole campaign.
        /// </summary>
        public string TimeToCompleteText => AllRewardsClaimed
            ? "Completed"
            : DurationFormatter.FormatRemaining(RemainingMinutes).Replace("left", "to complete");
    }
}
