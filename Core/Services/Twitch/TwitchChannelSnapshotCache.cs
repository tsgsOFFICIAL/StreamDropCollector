using System.Collections.Concurrent;
using Core.Models;

namespace Core.Services.Twitch
{
    /// <summary>
    /// Thread-safe cache of Twitch <see cref="LiveChannelSnapshot"/> entries keyed by login.
    /// </summary>
    internal sealed class TwitchChannelSnapshotCache
    {
        private readonly ConcurrentDictionary<string, LiveChannelSnapshot> _snapshots =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, LiveChannelSnapshot> Snapshots =>
            _snapshots.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string login, out LiveChannelSnapshot? snapshot) =>
            _snapshots.TryGetValue(login, out snapshot);

        public void Upsert(LiveChannelSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Login))
                return;

            _snapshots[snapshot.Login] = snapshot;
        }

        public void SetLiveState(string login, bool isLive, IReadOnlyList<string>? categorySlugs = null)
        {
            if (string.IsNullOrWhiteSpace(login))
                return;

            _snapshots.AddOrUpdate(
                login,
                _ => new LiveChannelSnapshot(login, isLive, categorySlugs ?? [], null, login, null),
                (_, existing) => new LiveChannelSnapshot(
                    existing.Login,
                    isLive,
                    categorySlugs ?? existing.CategorySlugs,
                    existing.ProfileImageUrl,
                    existing.DisplayName,
                    existing.GameId));
        }

        public void SetCategorySlugs(string login, IReadOnlyList<string> categorySlugs, string? gameId = null)
        {
            if (string.IsNullOrWhiteSpace(login))
                return;

            _snapshots.AddOrUpdate(
                login,
                _ => new LiveChannelSnapshot(login, false, categorySlugs, null, login, gameId),
                (_, existing) => new LiveChannelSnapshot(
                    existing.Login,
                    existing.IsLive,
                    categorySlugs,
                    existing.ProfileImageUrl,
                    existing.DisplayName,
                    gameId ?? existing.GameId));
        }
    }
}