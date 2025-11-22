using Core.Interfaces;
using Core.Models;

namespace Core.Services
{
    /// <summary>
    /// Provides methods for retrieving active drops campaigns from supported streaming platforms.
    /// </summary>
    /// <remarks>This service currently supports retrieving campaigns from Kick. Support for additional
    /// platforms, such as Twitch, may be added in the future.</remarks>
    public class DropsService
    {
        private readonly KickDropsProvider _kickProvider = new();
        private readonly TwitchDropsProvider _twitchProvider = new();

        /// <summary>
        /// Asynchronously retrieves all active drops campaigns from the specified providers.
        /// </summary>
        /// <remarks>Currently, only Kick campaigns are supported. Twitch integration may be added in the
        /// future.</remarks>
        /// <param name="kickHost">The web view host instance used to access Kick campaigns. Cannot be null.</param>
        /// <param name="twitchHost">The web view host instance used to access Twitch campaigns, or null to exclude Twitch.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A read-only list of active drops campaigns. The list will be empty if no active campaigns are found.</returns>
        public async Task<IReadOnlyList<DropsCampaign>> GetAllActiveCampaignsAsync(IWebViewHost kickHost, IWebViewHost twitchHost, CancellationToken ct = default)
        {
            List<Task<IReadOnlyList<DropsCampaign>>> tasks = new List<Task<IReadOnlyList<DropsCampaign>>>()
            {
                _kickProvider.GetActiveCampaignsAsync(kickHost, ct),
                _twitchProvider.GetActiveCampaignsAsync(twitchHost, ct)
            };

            IReadOnlyList<DropsCampaign>[] results = await Task.WhenAll(tasks);
            return [.. results.Where(x => x != null).SelectMany(x => x)];
        }
    }
}