using System.Collections.Concurrent;

namespace Core.Mining
{
    /// <summary>
    /// In-memory record of Kick rewards whose claim was rejected because the required game account
    /// (e.g. Riot) isn't linked. Not persisted, so every app restart retries once.
    /// </summary>
    public static class KickUnlinkedClaims
    {
        private static readonly ConcurrentDictionary<string, string?> Blocked = new();

        private static string Key(string accountId, string campaignId, string rewardId) => $"{accountId}:{campaignId}:{rewardId}";

        /// <summary>Marks a reward as needing a manual account link before it can be claimed.</summary>
        /// <returns><see langword="true"/> if newly marked; <see langword="false"/> if it was already marked.</returns>
        public static bool Mark(string accountId, string campaignId, string rewardId, string? connectUrl)
            => Blocked.TryAdd(Key(accountId, campaignId, rewardId), connectUrl);

        /// <summary>Returns whether auto-claim should skip this reward.</summary>
        public static bool IsBlocked(string accountId, string campaignId, string rewardId)
            => Blocked.ContainsKey(Key(accountId, campaignId, rewardId));
    }
}