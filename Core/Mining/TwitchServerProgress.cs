using Core.Models;

namespace Core.Mining
{
    /// <summary>
    /// What Twitch's inventory reports for a set of locally tracked campaigns.
    /// </summary>
    /// <param name="Succeeded">Whether Twitch returned its in-progress list. When <see langword="false"/> nothing is known
    /// and the other members are empty.</param>
    /// <param name="Campaigns">Server-reported rewards per campaign id, for campaigns Twitch lists as in progress.</param>
    /// <param name="DropInstanceIds">Twitch's claim id per drop (see <see cref="DropKey"/>), with an entry only for drops
    /// Twitch returned a record for. The value is <see langword="null"/> until the drop is claimable.</param>
    public sealed record TwitchServerProgress(
        bool Succeeded,
        IReadOnlyDictionary<string, IReadOnlyList<DropsReward>> Campaigns,
        IReadOnlyDictionary<string, string?> DropInstanceIds)
    {
        /// <summary>The result of a query that was skipped or failed.</summary>
        public static TwitchServerProgress Unavailable { get; } = new(
            false,
            new Dictionary<string, IReadOnlyList<DropsReward>>(),
            new Dictionary<string, string?>());

        /// <summary>Builds the <see cref="DropInstanceIds"/> key for a drop.</summary>
        public static string DropKey(string campaignId, string dropId) => $"{campaignId}#{dropId}";
    }

    /// <summary>What to do with a Twitch drop the local counters consider ready to claim.</summary>
    public enum TwitchClaimDecision
    {
        /// <summary>Twitch could not be asked, or has no record for this one drop: claim as before, unconfirmed.</summary>
        Unverified,

        /// <summary>Twitch confirms the drop is earned and unclaimed.</summary>
        Claim,

        /// <summary>Twitch reports the drop as already claimed.</summary>
        AlreadyClaimed,

        /// <summary>Twitch tracks the drop but has credited fewer minutes than the local counter.</summary>
        NotReady,

        /// <summary>Twitch answered and tracks no progress at all for the campaign: the local minutes were never credited.</summary>
        Untracked
    }

    /// <summary>
    /// Checks a locally "ready" Twitch drop against Twitch's own inventory before a claim is sent.
    /// </summary>
    /// <remarks>
    /// Watch time is counted locally between server syncs, and Twitch accepts a claim request for a drop it never
    /// credited, so a claim is only trusted when the inventory confirms the drop first.
    /// </remarks>
    public static class TwitchClaimVerifier
    {
        /// <summary>
        /// Decides how to handle a drop whose local progress has reached its requirement.
        /// </summary>
        /// <param name="server">Inventory state fetched for the drop's campaign, or <see langword="null"/> when not fetched.</param>
        /// <param name="campaignId">Id of the campaign that owns the drop.</param>
        /// <param name="reward">The locally ready drop.</param>
        /// <param name="dropInstanceId">Twitch's own claim id for the drop when it supplied one.</param>
        public static TwitchClaimDecision Decide(TwitchServerProgress? server, string campaignId, DropsReward reward, out string? dropInstanceId)
        {
            dropInstanceId = null;

            if (server is not { Succeeded: true })
                return TwitchClaimDecision.Unverified;

            if (!server.Campaigns.TryGetValue(campaignId, out IReadOnlyList<DropsReward>? serverRewards))
                return TwitchClaimDecision.Untracked;

            if (!server.DropInstanceIds.TryGetValue(TwitchServerProgress.DropKey(campaignId, reward.Id), out dropInstanceId))
                return TwitchClaimDecision.Unverified;

            DropsReward? serverReward = serverRewards.FirstOrDefault(r => r.Id == reward.Id);
            if (serverReward == null)
                return TwitchClaimDecision.Unverified;

            if (serverReward.IsClaimed)
                return TwitchClaimDecision.AlreadyClaimed;

            return !string.IsNullOrEmpty(dropInstanceId) || serverReward.ProgressMinutes >= reward.RequiredMinutes
                ? TwitchClaimDecision.Claim
                : TwitchClaimDecision.NotReady;
        }
    }
}