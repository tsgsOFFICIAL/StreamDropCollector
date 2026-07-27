using System.Net.Http;
using System.Text.Json;
using Core.Constants;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Twitch.Helix
{
    /// <summary>
    /// Singleton orchestrator for Twitch Helix REST and mine-only EventSub channel watching.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <see cref="RefreshChannelsAsync"/> for inventory metadata and streamer selection.
    /// Use <see cref="SetMinedChannelWatcherAsync"/> to attach real-time EventSub listeners to the channel
    /// currently being mined.
    /// </para>
    /// <para>
    /// Authentication is separate from the WebView drops login: this service uses the device-code OAuth flow
    /// and persists a refresh token under the SDC app-data folder.
    /// </para>
    /// </remarks>
    public sealed class TwitchHelixService : ITwitchHelixService, IAsyncDisposable
    {
        private static readonly Lazy<TwitchHelixService> InstanceLazy = new(() => new TwitchHelixService());

        private readonly HttpClient _http = new();
        private readonly TwitchChannelSnapshotCache _cache = new();
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private readonly SemaphoreSlim _authLock = new(1, 1);

        private static readonly TimeSpan[] TransientAuthRetryDelays =
        [
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(5)
        ];

        private TwitchHelixTokenManager? _tokenManager;
        private TwitchHelixClient? _client;
        private TwitchEventSubHub? _eventSubHub;
        private bool _isAuthenticated;
        private int _transientAuthRetryScheduled;

        private TwitchHelixService()
        {
            _http.DefaultRequestHeaders.Add("Client-Id", TwitchHelixConstants.ClientId);
        }

        /// <summary>
        /// Gets the process-wide singleton instance of the Twitch Helix service.
        /// </summary>
        public static TwitchHelixService Instance => InstanceLazy.Value;

        /// <summary>
        /// Gets whether a valid Helix user access token is available and the service is ready for API calls.
        /// </summary>
        public bool IsAuthenticated => _isAuthenticated;

        /// <summary>
        /// Gets the most recently known channel snapshots keyed by login slug.
        /// </summary>
        public IReadOnlyDictionary<string, LiveChannelSnapshot> Snapshots => _cache.Snapshots;

        /// <summary>
        /// Occurs when cached snapshots are updated after a Helix refresh or an EventSub notification.
        /// </summary>
        public event Action<IReadOnlyDictionary<string, LiveChannelSnapshot>>? SnapshotsChanged;

        /// <summary>
        /// Ensures Helix authentication, prompting for device-code approval when no saved refresh token exists.
        /// </summary>
        /// <param name="promptAsync">
        /// Callback invoked with device-code details so the UI can collect user approval.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <see langword="true"/> when authentication succeeded; otherwise <see langword="false"/>.
        /// </returns>
        public async Task<bool> EnsureAuthenticatedAsync(
            Func<TwitchDeviceCodePrompt, Task> promptAsync,
            CancellationToken ct = default)
        {
            if (_isAuthenticated)
                return true;

            await _authLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_isAuthenticated)
                    return true;

                _tokenManager = await TwitchHelixTokenManager.AuthenticateAsync(
                    _http,
                    TwitchHelixConstants.ClientId,
                    promptAsync,
                    ct).ConfigureAwait(false);

                _client = new TwitchHelixClient(_http, _tokenManager);
                _eventSubHub = new TwitchEventSubHub(_client, _cache, OnStreamOnlineAsync, PublishSnapshotsChanged);
                _eventSubHub.Start();
                _isAuthenticated = true;
                AppLogger.Info("TwitchHelix", "Helix authentication ready.");
                return true;
            }
            catch (TwitchHelixTransientAuthException ex)
            {
                AppLogger.Warn(
                    "TwitchHelix",
                    $"Helix authentication deferred ({ex.Message}); will retry automatically in the background.");
                ScheduleTransientAuthRetry(promptAsync);
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("TwitchHelix", "Helix authentication failed.", ex);
                return false;
            }
            finally
            {
                _authLock.Release();
            }
        }

        /// <summary>
        /// Retries authentication with backoff after a transient network failure, so a temporary outage
        /// (e.g. a DNS blip while refreshing the saved token) recovers on its own instead of requiring
        /// the user to restart the app or re-approve the device-code flow.
        /// </summary>
        private void ScheduleTransientAuthRetry(Func<TwitchDeviceCodePrompt, Task> promptAsync)
        {
            if (Interlocked.CompareExchange(ref _transientAuthRetryScheduled, 1, 0) != 0)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    foreach (TimeSpan delay in TransientAuthRetryDelays)
                    {
                        if (_isAuthenticated)
                            return;

                        await Task.Delay(delay).ConfigureAwait(false);

                        if (_isAuthenticated)
                            return;

                        if (await EnsureAuthenticatedAsync(promptAsync).ConfigureAwait(false))
                        {
                            AppLogger.Info("TwitchHelix", "Helix authentication recovered after transient network failure.");
                            return;
                        }
                    }

                    while (!_isAuthenticated)
                    {
                        await Task.Delay(TransientAuthRetryDelays[^1]).ConfigureAwait(false);

                        if (await EnsureAuthenticatedAsync(promptAsync).ConfigureAwait(false))
                        {
                            AppLogger.Info("TwitchHelix", "Helix authentication recovered after transient network failure.");
                            return;
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _transientAuthRetryScheduled, 0);
                }
            });
        }

        /// <summary>
        /// Refreshes snapshots for the given logins via Helix REST endpoints (<c>/users</c>, <c>/streams</c>, <c>/channels</c>).
        /// </summary>
        /// <remarks>Does not create EventSub subscriptions. Safe to call for all eligible inventory streamers.</remarks>
        /// <param name="logins">Channel login slugs to refresh.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A task that represents the asynchronous refresh operation.</returns>
        public async Task RefreshChannelsAsync(IEnumerable<string> logins, CancellationToken ct = default)
        {
            if (!_isAuthenticated || _client is null || _eventSubHub is null)
            {
                AppLogger.Warn(
                    "TwitchHelix",
                    $"RefreshChannelsAsync SKIPPED auth={_isAuthenticated} client={_client is not null} hub={_eventSubHub is not null}");
                return;
            }

            List<string> normalized = logins
                .Where(login => !string.IsNullOrWhiteSpace(login))
                .Select(login => login.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0)
            {
                AppLogger.Debug("TwitchHelix", "RefreshChannelsAsync SKIPPED - empty login list.");
                return;
            }

            AppLogger.Debug("TwitchHelix", $"RefreshChannelsAsync START count={normalized.Count} logins=[{string.Join(", ", normalized.Take(20))}{(normalized.Count > 20 ? ", ..." : "")}]");

            await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await RefreshChannelsCoreAsync(normalized, ct).ConfigureAwait(false);
                PublishSnapshotsChanged();
            }
            finally
            {
                _refreshLock.Release();
            }
        }

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
        public async Task SetMinedChannelWatcherAsync(string? channelLogin, CancellationToken ct = default)
        {
            if (!_isAuthenticated || _eventSubHub is null || _client is null)
            {
                AppLogger.Warn(
                    "TwitchMining",
                    $"SetMinedChannelWatcher SKIPPED login={channelLogin ?? "(null)"} auth={_isAuthenticated} hub={_eventSubHub is not null} client={_client is not null}");
                return;
            }

            if (string.IsNullOrWhiteSpace(channelLogin))
            {
                AppLogger.Debug("TwitchMining", "SetMinedChannelWatcher CLEAR - no mined channel.");
                await _eventSubHub.ClearWatcherAsync(ct).ConfigureAwait(false);
                return;
            }

            string login = channelLogin.Trim().ToLowerInvariant();
            string? broadcasterId = await _client.GetUserIdByLoginAsync(login, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(broadcasterId))
            {
                AppLogger.Warn("TwitchHelix", $"Cannot watch mined channel '{login}': broadcaster id not found.");
                await _eventSubHub.ClearWatcherAsync(ct).ConfigureAwait(false);
                return;
            }

            AppLogger.Debug("TwitchMining", $"SetMinedChannelWatcher SET login={login} broadcasterId={broadcasterId}");
            await _eventSubHub.SetWatcherAsync(login, broadcasterId, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a cached snapshot for a login, fetching via Helix when the cache has no entry yet.
        /// </summary>
        /// <param name="channelLogin">Channel login slug.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The channel snapshot, or <see langword="null"/> when unknown or Helix is not authenticated.</returns>
        public async Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channelLogin))
                return null;

            string login = channelLogin.Trim().ToLowerInvariant();

            if (_cache.TryGet(login, out LiveChannelSnapshot? cached) && cached is not null)
            {
                AppLogger.Debug(
                    "TwitchHelix",
                    $"GetChannelAsync CACHE HIT login={login} live={cached.IsLive} categories=[{string.Join(", ", cached.CategorySlugs)}]");
                return cached;
            }

            if (!_isAuthenticated)
            {
                AppLogger.Warn("TwitchHelix", $"GetChannelAsync CACHE MISS login={login} - not authenticated.");
                return null;
            }

            AppLogger.Debug("TwitchHelix", $"GetChannelAsync CACHE MISS login={login} - refreshing via Helix.");
            await RefreshChannelsAsync([login], ct).ConfigureAwait(false);
            _cache.TryGet(login, out cached);

            if (cached is null)
                AppLogger.Warn("TwitchHelix", $"GetChannelAsync STILL MISSING login={login} after refresh.");
            else
                AppLogger.Debug(
                    "TwitchHelix",
                    $"GetChannelAsync REFRESHED login={login} live={cached.IsLive} categories=[{string.Join(", ", cached.CategorySlugs)}]");

            return cached;
        }

        /// <summary>
        /// Stops the EventSub WebSocket, releases HTTP resources, and disposes synchronization primitives.
        /// </summary>
        /// <returns>A task that represents the asynchronous dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_eventSubHub is not null)
                await _eventSubHub.DisposeAsync().ConfigureAwait(false);

            _http.Dispose();
            _refreshLock.Dispose();
            _authLock.Dispose();
        }

        private async Task OnStreamOnlineAsync(string login, CancellationToken ct)
        {
            try
            {
                await RefreshChannelsAsync([login], ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("TwitchEventSub", $"Failed to refresh {login} after stream.online: {ex.Message}");
            }
        }

        private async Task RefreshChannelsCoreAsync(IReadOnlyList<string> logins, CancellationToken ct)
        {
            TwitchHelixClient client = _client!;

            Dictionary<string, JsonElement> users = await client.GetUsersByLoginAsync(logins, ct).ConfigureAwait(false);
            Dictionary<string, JsonElement> streams = await client.GetStreamsByLoginAsync(logins, ct).ConfigureAwait(false);

            List<string> offlineBroadcasterIds = [];

            foreach (string login in logins)
            {
                if (!users.TryGetValue(login, out JsonElement user))
                    continue;

                string? broadcasterId = user.GetProperty("id").GetString();
                if (string.IsNullOrWhiteSpace(broadcasterId))
                    continue;

                if (!streams.ContainsKey(login))
                    offlineBroadcasterIds.Add(broadcasterId);
            }

            Dictionary<string, JsonElement> channels = offlineBroadcasterIds.Count > 0
                ? await client.GetChannelsByBroadcasterIdAsync(offlineBroadcasterIds, ct).ConfigureAwait(false)
                : [];

            foreach (string login in logins)
            {
                if (!users.TryGetValue(login, out JsonElement user))
                    continue;

                bool isLive = streams.TryGetValue(login, out JsonElement stream);
                string? gameId = null;
                IReadOnlyList<string> categorySlugs;

                if (isLive)
                {
                    gameId = stream.TryGetProperty("game_id", out JsonElement liveGameIdElement)
                        ? liveGameIdElement.GetString()
                        : null;
                    categorySlugs = TwitchHelixChannelParser.BuildCategorySlugs(
                        stream.GetProperty("game_name").GetString(),
                        gameId);
                }
                else
                {
                    TryGetOfflineChannelGame(user, channels, out categorySlugs, out gameId);
                }

                LiveChannelSnapshot snapshot = TwitchHelixChannelParser.ParseUser(user, isLive, categorySlugs, gameId);
                _cache.Upsert(snapshot);
            }

            int liveCount = streams.Count;
            int missingUsers = logins.Count - users.Count;
            IEnumerable<string> liveDetails = logins
                .Where(login => streams.ContainsKey(login))
                .Select(login =>
                {
                    JsonElement stream = streams[login];
                    string game = stream.TryGetProperty("game_name", out JsonElement gn) ? gn.GetString() ?? "?" : "?";
                    return $"{login}:{game}";
                })
                .Take(15);

            AppLogger.Debug(
                "TwitchHelix",
                $"RefreshChannelsCore DONE requested={logins.Count} users={users.Count} live={liveCount} missingUsers={missingUsers}");

            if (liveCount > 0)
            {
                AppLogger.Debug(
                    "TwitchHelix",
                    $"RefreshChannelsCore LIVE sample=[{string.Join(", ", liveDetails)}{(liveCount > 15 ? ", ..." : "")}]");
            }
        }

        private static bool TryGetOfflineChannelGame(
            JsonElement user,
            Dictionary<string, JsonElement> channels,
            out IReadOnlyList<string> categorySlugs,
            out string? gameId)
        {
            categorySlugs = [];
            gameId = null;

            string? broadcasterId = user.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(broadcasterId)
                || !channels.TryGetValue(broadcasterId, out JsonElement channel))
            {
                return false;
            }

            gameId = channel.TryGetProperty("game_id", out JsonElement gameIdElement)
                ? gameIdElement.GetString()
                : null;
            categorySlugs = TwitchHelixChannelParser.BuildCategorySlugs(
                channel.TryGetProperty("game_name", out JsonElement gameNameElement)
                    ? gameNameElement.GetString()
                    : null,
                gameId);
            return true;
        }

        private void PublishSnapshotsChanged() =>
            SnapshotsChanged?.Invoke(_cache.Snapshots);
    }
}