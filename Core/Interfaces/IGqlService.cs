using System.Text.Json.Nodes;

namespace Core.Interfaces
{
    public interface IGqlService : IDisposable
    {
        /// <summary>
        /// Executes an asynchronous query using the specified payload and returns the result as a JSON object.
        /// </summary>
        /// <param name="payload">The payload to be sent with the query. This object contains the data or parameters required for the query
        /// operation. Cannot be null.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the query operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="JsonObject"/>
        /// representing the query response.</returns>
        Task<JsonNode> QueryAsync(object payload, CancellationToken ct = default);
        /// <summary>
        /// Asynchronously retrieves the complete data set for the drops dashboard.
        /// </summary>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a JsonObject with the full
        /// dashboard data.</returns>
        Task<JsonObject> QueryFullDropsDashboardAsync(CancellationToken ct = default);
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
    }
}