using Core.Enums;

namespace Core.Interfaces
{
    /// <summary>
    /// Defines a contract for retrieving active Drops campaigns and identifying the platform context.
    /// </summary>
    public interface IDropsCampaignProvider
    {
        /// <summary>
        /// Asynchronously retrieves a list of currently active Drops campaigns.
        /// </summary>
        /// <param name="host">The web view host used to access campaign data. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a read-only list of active Drops
        /// campaigns. The list is empty if no campaigns are active.</returns>
        Task<IReadOnlyList<Models.DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken cancellationToken = default);
        /// <summary>
        /// Gets the platform on which the application is running.
        /// </summary>
        Platform Platform { get; }
    }
}