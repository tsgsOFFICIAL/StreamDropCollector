namespace Models
{
    public record DropsReward(
        string Id,
        string Name,
        string? ImageUrl,
        int RequiredMinutes);

    public record DropsCampaign(
        string Id,
        string Name,
        string GameName,
        string? GameImageUrl,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        IReadOnlyList<DropsReward> Rewards,
        string? ConnectUrl = null,
        Platform Platform);
}