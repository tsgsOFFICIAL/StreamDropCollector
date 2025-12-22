using System.Text.Json.Nodes;

namespace Core.Interfaces
{
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
        /// Asynchronously retrieves a hash representing the current drop campaign details.
        /// </summary>
        /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a string hash of the current
        /// drop campaign details.</returns>
        Task<string> GetCurrentDropCampaignDetailsHashAsync(CancellationToken ct = default);
        string UserId { set; }
    }
}