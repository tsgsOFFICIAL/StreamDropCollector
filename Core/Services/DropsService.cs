using System.Runtime.CompilerServices;
using Core.Interfaces;
using Core.Models;
using Core.Enums;

namespace Core.Services
{
    /// <summary>
    /// Provides methods for retrieving active drops campaigns from supported streaming platforms.
    /// </summary>
    /// <remarks>Delegates to platform-specific providers and streams results as each platform completes.</remarks>
    public class DropsService
    {
        private readonly KickDropsProvider _kickProvider = new();
        private TwitchDropsProvider? _twitchProvider;

        /// <summary>
        /// Asynchronously streams active drops campaigns from connected hosts, yielding each platform's
        /// results as soon as they are available rather than waiting for all platforms to finish.
        /// </summary>
        /// <remarks>Tasks for each connected platform run concurrently. Results are yielded in
        /// completion order - whichever platform responds first is returned first.</remarks>
        /// <param name="kickHost">The Kick web view host used to query active campaigns.</param>
        /// <param name="kickStatus">If <see langword="Connected"/>, Kick campaigns are included.</param>
        /// <param name="twitchHost">The Twitch web view host used to query active campaigns.</param>
        /// <param name="twitchStatus">If <see langword="Connected"/>, Twitch campaigns are included.</param>
        /// <param name="gqlService">The GQL service required by the Twitch provider.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>
        /// An async stream of per-platform campaign lists, yielded as each platform completes.
        /// Yields nothing if neither host is connected.
        /// </returns>
        public async IAsyncEnumerable<IReadOnlyList<DropsCampaign>> GetAllActiveCampaignsAsync(
            IWebViewHost kickHost, ConnectionStatus? kickStatus,
            IWebViewHost twitchHost, ConnectionStatus? twitchStatus,
            IGqlService? gqlService,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            List<Task<IReadOnlyList<DropsCampaign>>> pending = [];

            if (kickStatus == ConnectionStatus.Connected)
                pending.Add(_kickProvider.GetActiveCampaignsAsync(kickHost, ct));

            if (twitchStatus == ConnectionStatus.Connected)
            {
                _twitchProvider = new TwitchDropsProvider(gqlService!);
                pending.Add(_twitchProvider.GetActiveCampaignsAsync(twitchHost, ct));
            }

            // Yield each platform's results as soon as it finishes, without blocking on the other
            while (pending.Count > 0)
            {
                Task<IReadOnlyList<DropsCampaign>> completed = await Task.WhenAny(pending);
                pending.Remove(completed);
                yield return await completed;
            }
        }
    }
}