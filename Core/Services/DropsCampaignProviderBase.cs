using Core.Interfaces;
using Core.Enums;

namespace Core.Services
{
    /// <summary>
    /// Serves as the base class for providers that supply Drops campaign data for a specific platform.
    /// </summary>
    /// <remarks>Implementations of this class are responsible for retrieving active Drops campaigns from
    /// their respective platforms. This class defines the contract for accessing campaign data and platform
    /// information, and should be extended to support additional platforms as needed.</remarks>
    public abstract class DropsCampaignProviderBase : IDropsCampaignProvider
    {
        /// <summary>
        /// Gets the platform on which the current instance is running.
        /// </summary>
        public abstract Platform Platform { get; }
        /// <summary>
        /// Asynchronously retrieves a list of currently active Drops campaigns.
        /// </summary>
        /// <param name="host">The web view host used to perform the operation. Cannot be null.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a read-only list of active Drops
        /// campaigns. The list is empty if no campaigns are active.</returns>
        public abstract Task<IReadOnlyList<Models.DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken cancellationToken = default);
    }
}