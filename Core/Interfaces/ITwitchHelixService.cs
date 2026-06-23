using Core.Models;

namespace Core.Interfaces
{
    /// <summary>
    /// Twitch Helix + EventSub service for channel metadata (live state, category, profile images).
    /// </summary>
    /// <remarks>
    /// Helix REST is used for batch metadata and streamer selection. EventSub WebSocket subscriptions
    /// are limited to the actively mined channel to stay within Twitch subscription cost limits.
    /// </remarks>
    public interface ITwitchHelixService
    {
        /// <summary>
        /// Gets whether a valid Helix user access token is available and the service is ready for API calls.
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Gets the most recently known channel snapshots keyed by login slug.
        /// </summary>
        IReadOnlyDictionary<string, LiveChannelSnapshot> Snapshots { get; }

        /// <summary>
        /// Occurs when cached snapshots are updated after a Helix refresh or an EventSub notification.
        /// </summary>
        event Action<IReadOnlyDictionary<string, LiveChannelSnapshot>>? SnapshotsChanged;

        /// <summary>
        /// Ensures Helix authentication, prompting for device-code approval when no saved refresh token exists.
        /// </summary>
        /// <param name="promptAsync">
        /// Callback invoked with device-code details so the UI can collect user approval
        /// (for example via <c>TwitchHelixAuthWindow</c>).
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <see langword="true"/> when authentication succeeded; otherwise <see langword="false"/>.
        /// </returns>
        Task<bool> EnsureAuthenticatedAsync(
            Func<TwitchDeviceCodePrompt, Task> promptAsync,
            CancellationToken ct = default);

        /// <summary>
        /// Refreshes snapshots for the given logins via Helix REST endpoints (<c>/users</c>, <c>/streams</c>, <c>/channels</c>).
        /// </summary>
        /// <remarks>Does not create EventSub subscriptions. Safe to call for all eligible inventory streamers.</remarks>
        /// <param name="logins">Channel login slugs to refresh.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that represents the asynchronous refresh operation.</returns>
        Task RefreshChannelsAsync(IEnumerable<string> logins, CancellationToken ct = default);

        /// <summary>
        /// Points EventSub at the actively mined Twitch channel, or clears all subscriptions when <paramref name="channelLogin"/> is null.
        /// </summary>
        /// <remarks>
        /// While mining, subscribes to <c>stream.offline</c> and <c>channel.update</c> for real-time health checks.
        /// Passing null drops every EventSub subscription (for example on pause or re-evaluation).
        /// </remarks>
        /// <param name="channelLogin">Login of the mined streamer, or null to clear the watcher.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that represents the asynchronous watcher update.</returns>
        Task SetMinedChannelWatcherAsync(string? channelLogin, CancellationToken ct = default);

        /// <summary>
        /// Gets a cached snapshot for a login, fetching via Helix when the cache has no entry yet.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The channel snapshot, or <see langword="null"/> when unknown or Helix is not authenticated.</returns>
        Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default);
    }
}