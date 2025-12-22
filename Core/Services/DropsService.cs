using Core.Interfaces;
using Core.Models;
using Core.Enums;

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
        private TwitchDropsProvider? _twitchProvider;

        /// <summary>
        /// Retrieves all active drops campaigns from connected Kick and Twitch hosts asynchronously.
        /// </summary>
        /// <remarks>If both Kick and Twitch hosts are connected, campaigns from both sources are combined
        /// into a single list. The method returns quickly with an empty list if neither host is connected.</remarks>
        /// <param name="kickHost">The Kick web view host used to query active campaigns. Must not be null if Kick is connected.</param>
        /// <param name="kickStatus">The connection status of the Kick host. If set to <see langword="Connected"/>, Kick campaigns will be
        /// included.</param>
        /// <param name="twitchHost">The Twitch web view host used to query active campaigns. Must not be null if Twitch is connected.</param>
        /// <param name="twitchStatus">The connection status of the Twitch host. If set to <see langword="Connected"/>, Twitch campaigns will be
        /// included.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A read-only list containing all active drops campaigns from the specified connected hosts. The list will be
        /// empty if neither host is connected or no campaigns are found.</returns>
        public async Task<IReadOnlyList<DropsCampaign>> GetAllActiveCampaignsAsync(IWebViewHost kickHost, ConnectionStatus? kickStatus, IWebViewHost twitchHost, ConnectionStatus? twitchStatus, IGqlService? gqlService, CancellationToken ct = default)
        {
            List<Task<IReadOnlyList<DropsCampaign>>> tasks = new List<Task<IReadOnlyList<DropsCampaign>>>();

            if (kickStatus == ConnectionStatus.Connected)
                tasks.Add(_kickProvider.GetActiveCampaignsAsync(kickHost, ct));

            if (twitchStatus == ConnectionStatus.Connected)
            {
                _twitchProvider = new TwitchDropsProvider(gqlService!);

                tasks.Add(_twitchProvider.GetActiveCampaignsAsync(twitchHost, ct));
            }

            // If nothing to do -> return fast
            if (tasks.Count == 0)
                return Array.Empty<DropsCampaign>().AsReadOnly();

            IReadOnlyList<DropsCampaign>[] results = await Task.WhenAll(tasks);

            return results.SelectMany(x => x ?? []).ToList().AsReadOnly();
        }
    }
}