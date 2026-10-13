using System.Text.Json.Nodes;

namespace Core.Interfaces
{
    /// <summary>
    /// GraphQL client for Twitch drops dashboard queries, drop claims, and live channel lookups.
    /// </summary>
    public interface IGqlService : IDisposable
    {
        /// <summary>
        /// Attempts to claim a reward drop for the specified campaign asynchronously.
        /// </summary>
        /// <param name="campaignId">The unique identifier of the campaign from which to claim the drop. Cannot be null or empty.</param>
        /// <param name="rewardId">The unique identifier of the reward to claim. Cannot be null or empty.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the drop was
        /// successfully claimed; otherwise, <see langword="false"/>.</returns>
        Task<bool> ClaimDropAsync(string campaignId, string rewardId, CancellationToken ct = default);
        /// <summary>
        /// Asynchronously retrieves the complete data set for the drops dashboard.
        /// </summary>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a JsonObject with the full
        /// dashboard data.</returns>
        Task<JsonArray> QueryFullDropsDashboardAsync(CancellationToken ct = default);
        /// <summary>
        /// Queries only the Inventory GQL operation using cached auth headers, without touching the WebView.
        /// Intended for lightweight progress sync while a stream is being mined.
        /// </summary>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>
        /// The Inventory response object when successful; otherwise <see langword="null"/> if cached headers are
        /// unavailable or the request fails.
        /// </returns>
        Task<JsonObject?> QueryInventoryProgressAsync(CancellationToken ct = default);
        /// <summary>
        /// Asynchronously retrieves details for multiple drop campaigns in a single batch operation.
        /// </summary>
        /// <param name="requests">A read-only list of tuples, each containing a drop campaign ID and a channel login, specifying the campaigns
        /// and channels for which to retrieve details. Cannot be null or contain null elements.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a dictionary mapping each
        /// requested drop campaign ID to its corresponding details as a JsonObject. If a campaign is not found, it may
        /// be omitted from the dictionary.</returns>
        Task<Dictionary<string, JsonObject>> QueryDropCampaignDetailsBatchAsync(IReadOnlyList<(string dropID, string channelLogin)> requests, CancellationToken ct = default);
        /// <summary>
        /// Sets the authenticated Twitch user identifier used for GraphQL requests.
        /// </summary>
        /// <summary>
        /// Queries the viewer's in-progress reward campaigns (the "Open Reward Campaigns" Twitch lists separately from drops).
        /// Returns the <c>ViewerRewardDropInventory</c> response object, or <see langword="null"/> on failure.
        /// </summary>
        Task<JsonObject?> QueryRewardCampaignProgressAsync(CancellationToken ct = default);
        /// <summary>
        /// Captures Twitch's own <c>DailyViewDropCollection</c> request from the drops campaigns page, replays it, and
        /// returns the response <c>data</c> object (<c>campaign0</c>, <c>campaign1</c>, ...). It carries each reward
        /// group's repeat rules even for campaigns the viewer has not started. Returns <see langword="null"/> on failure.
        /// </summary>
        Task<JsonObject?> QueryRewardCampaignRulesAsync(CancellationToken ct = default);
        string UserId { set; }
    }
}