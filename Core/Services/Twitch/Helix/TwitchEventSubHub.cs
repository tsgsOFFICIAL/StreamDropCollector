using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Core.Logging;

namespace Core.Services.Twitch.Helix
{
    /// <summary>
    /// EventSub WebSocket client that watches the actively mined broadcaster only (offline + category).
    /// </summary>
    internal sealed class TwitchEventSubHub : IAsyncDisposable
    {
        private const string WebSocketUrl = "wss://eventsub.wss.twitch.tv/ws";

        private readonly TwitchHelixClient _client;
        private readonly TwitchChannelSnapshotCache _cache;
        private readonly Func<string, CancellationToken, Task> _onStreamOnlineAsync;
        private readonly Action _onCacheUpdated;

        private readonly SemaphoreSlim _watchLock = new(1, 1);
        private readonly object _sync = new();

        private string? _watchedLogin;
        private string? _watchedBroadcasterId;
        private readonly List<string> _subscriptionIds = [];

        private ClientWebSocket _socket = new();
        private CancellationTokenSource? _cts;
        private Task? _runTask;
        private string? _sessionId;

        public TwitchEventSubHub(
            TwitchHelixClient client,
            TwitchChannelSnapshotCache cache,
            Func<string, CancellationToken, Task> onStreamOnlineAsync,
            Action onCacheUpdated)
        {
            _client = client;
            _cache = cache;
            _onStreamOnlineAsync = onStreamOnlineAsync;
            _onCacheUpdated = onCacheUpdated;
        }

        /// <summary>
        /// Points EventSub at the mined channel. Pass null to drop all subscriptions.
        /// </summary>
        public async Task SetWatcherAsync(string? login, string? broadcasterId, CancellationToken ct = default)
        {
            await _watchLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string? normalizedLogin = string.IsNullOrWhiteSpace(login)
                    ? null
                    : login.Trim().ToLowerInvariant();

                if (string.Equals(normalizedLogin, _watchedLogin, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(broadcasterId, _watchedBroadcasterId, StringComparison.Ordinal))
                {
                    return;
                }

                await UnsubscribeAllAsync(ct).ConfigureAwait(false);

                _watchedLogin = normalizedLogin;
                _watchedBroadcasterId = broadcasterId;

                if (string.IsNullOrWhiteSpace(_watchedBroadcasterId) || string.IsNullOrWhiteSpace(_watchedLogin))
                {
                    _watchedLogin = null;
                    _watchedBroadcasterId = null;
                    AppLogger.Debug("TwitchEventSub", "Mined-channel watcher cleared.");
                    return;
                }

                AppLogger.Debug("TwitchEventSub", $"Watching mined channel {_watchedLogin} ({_watchedBroadcasterId}).");

                if (_sessionId is not null)
                    await SubscribeMinedChannelAsync(_watchedBroadcasterId, _sessionId, ct).ConfigureAwait(false);
            }
            finally
            {
                _watchLock.Release();
            }
        }

        public Task ClearWatcherAsync(CancellationToken ct = default) =>
            SetWatcherAsync(null, null, ct);

        public void Start()
        {
            if (_runTask != null)
                return;

            _cts = new CancellationTokenSource();
            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }

        public async ValueTask DisposeAsync()
        {
            _cts?.Cancel();

            if (_runTask is not null)
            {
                try
                {
                    await _runTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown.
                }
            }

            if (_socket.State == WebSocketState.Open)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Best effort.
                }
            }

            _socket.Dispose();
            _cts?.Dispose();
            _watchLock.Dispose();
        }

        private async Task RunAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    _socket.Dispose();
                    _socket = new ClientWebSocket();
                    await _socket.ConnectAsync(new Uri(WebSocketUrl), ct).ConfigureAwait(false);

                    while (!ct.IsCancellationRequested && _socket.State == WebSocketState.Open)
                    {
                        string? json = await ReceiveFullMessageAsync(ct).ConfigureAwait(false);
                        if (json is null)
                            break;

                        await HandleMessageAsync(json, ct).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("TwitchEventSub", $"WebSocket error: {ex.Message}. Reconnecting in 5s...");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private async Task<string?> ReceiveFullMessageAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[8 * 1024];
            using MemoryStream ms = new();

            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                ms.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private async Task HandleMessageAsync(string json, CancellationToken ct)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string? messageType = root.GetProperty("metadata").GetProperty("message_type").GetString();
            JsonElement payload = root.GetProperty("payload");

            switch (messageType)
            {
                case "session_welcome":
                    string sessionId = payload.GetProperty("session").GetProperty("id").GetString()!;
                    lock (_sync)
                    {
                        _sessionId = sessionId;
                        _subscriptionIds.Clear();
                    }

                    AppLogger.Debug("TwitchEventSub", $"Connected (session={sessionId}).");
                    await ResubscribeCurrentWatcherAsync(sessionId, ct).ConfigureAwait(false);
                    break;

                case "session_keepalive":
                    break;

                case "notification":
                    HandleNotification(payload);
                    break;

                case "session_reconnect":
                    string reconnectUrl = payload.GetProperty("session").GetProperty("reconnect_url").GetString()!;
                    AppLogger.Debug("TwitchEventSub", "Twitch requested reconnect.");
                    ClientWebSocket newSocket = new();
                    await newSocket.ConnectAsync(new Uri(reconnectUrl), ct).ConfigureAwait(false);
                    _socket.Dispose();
                    _socket = newSocket;
                    break;

                case "revocation":
                    string subType = payload.GetProperty("subscription").GetProperty("type").GetString() ?? "unknown";
                    string status = payload.GetProperty("subscription").GetProperty("status").GetString() ?? "unknown";
                    AppLogger.Warn("TwitchEventSub", $"Subscription revoked: {subType} -> {status}");
                    break;
            }
        }

        private void HandleNotification(JsonElement payload)
        {
            string? subType = payload.GetProperty("subscription").GetProperty("type").GetString();
            JsonElement ev = payload.GetProperty("event");

            string? login = _watchedLogin;
            if (string.IsNullOrWhiteSpace(login))
                return;

            switch (subType)
            {
                case "channel.update":
                    string? categoryName = ev.TryGetProperty("category_name", out JsonElement categoryElement)
                        ? categoryElement.GetString()
                        : null;
                    string? categoryId = ev.TryGetProperty("category_id", out JsonElement categoryIdElement)
                        ? categoryIdElement.GetString()
                        : null;
                    IReadOnlyList<string> slugs = TwitchHelixChannelParser.BuildCategorySlugs(categoryName, categoryId);
                    _cache.SetCategorySlugs(login, slugs, categoryId);
                    AppLogger.Debug("TwitchEventSub", $"channel.update {login}: category={categoryName} gameId={categoryId}");
                    break;

                case "stream.online":
                    _cache.SetLiveState(login, true);
                    AppLogger.Debug("TwitchEventSub", $"stream.online {login}");
                    _ = _onStreamOnlineAsync(login, CancellationToken.None);
                    break;

                case "stream.offline":
                    _cache.SetLiveState(login, false);
                    AppLogger.Debug("TwitchEventSub", $"stream.offline {login}");
                    break;
            }

            _onCacheUpdated();
        }

        private async Task ResubscribeCurrentWatcherAsync(string sessionId, CancellationToken ct)
        {
            await _watchLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string? broadcasterId = _watchedBroadcasterId;
                if (string.IsNullOrWhiteSpace(broadcasterId))
                    return;

                await SubscribeMinedChannelAsync(broadcasterId, sessionId, ct).ConfigureAwait(false);
            }
            finally
            {
                _watchLock.Release();
            }
        }

        private async Task SubscribeMinedChannelAsync(string broadcasterId, string sessionId, CancellationToken ct)
        {
            // Mined channels are selected while live: offline + category change are enough.
            await TrySubscribeAsync("stream.offline", "1", broadcasterId, sessionId, ct).ConfigureAwait(false);
            await TrySubscribeAsync("channel.update", "2", broadcasterId, sessionId, ct).ConfigureAwait(false);
        }

        private async Task TrySubscribeAsync(
            string type,
            string version,
            string broadcasterId,
            string sessionId,
            CancellationToken ct)
        {
            string? subscriptionId = await _client.CreateEventSubSubscriptionAsync(
                type, version, broadcasterId, sessionId, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(subscriptionId))
            {
                lock (_sync)
                {
                    _subscriptionIds.Add(subscriptionId);
                }
            }
        }

        private async Task UnsubscribeAllAsync(CancellationToken ct)
        {
            List<string> ids;
            lock (_sync)
            {
                ids = [.. _subscriptionIds];
                _subscriptionIds.Clear();
            }

            foreach (string id in ids)
                await _client.DeleteEventSubSubscriptionAsync(id, ct).ConfigureAwait(false);
        }
    }
}