using Core.Helpers;
using Core.Interfaces;
using Core.Logging;
using Core.Models;

namespace Core.Services.Mining.Twitch
{
    /// <summary>
    /// Twitch live-channel API using in-page WebView <c>fetch</c> when a real endpoint exists; stub fallback until then.
    /// </summary>
    public sealed class TwitchLiveChannelApi : ITwitchLiveChannelApi
    {
        private readonly Func<IWebViewHost?> _hostProvider;

        public TwitchLiveChannelApi(Func<IWebViewHost?> hostProvider)
        {
            _hostProvider = hostProvider ?? throw new ArgumentNullException(nameof(hostProvider));
        }

        public async Task<LiveChannelSnapshot?> GetChannelAsync(string channelLogin, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(channelLogin))
                return null;

            LiveChannelSnapshot? fetched = await TryFetchChannelAsync(channelLogin, ct);
            if (fetched != null)
                return fetched;

            return CreateStubSnapshot(channelLogin);
        }

        public async Task<string?> SelectBestLiveLoginAsync(DropsCampaign campaign, string? preferredLogin, CancellationToken ct = default)
        {
            string? selected = await LiveChannelSelector.SelectBestLiveLoginAsync(
                campaign,
                preferredLogin,
                GetChannelAsync,
                "TwitchSelection",
                ct);

            if (selected != null)
                return selected;

            return await SelectBestWithStubFallbackAsync(campaign, preferredLogin, ct);
        }

        public async Task<bool> IsChannelEligibleAsync(string channelLogin, string gameSlug, CancellationToken ct = default)
        {
            LiveChannelSnapshot? snapshot = await GetChannelAsync(channelLogin, ct);
            bool eligible = IsEligibleWithStubFallback(snapshot, gameSlug);

            AppLogger.Debug(
                "TwitchSelection",
                $"IsChannelEligible login={channelLogin}, slug={gameSlug}, live={snapshot?.IsLive == true}, stubFallback={eligible && !LiveChannelEligibility.IsEligible(snapshot, gameSlug)} -> {eligible}");

            return eligible;
        }

        private Task<LiveChannelSnapshot?> TryFetchChannelAsync(string channelLogin, CancellationToken ct)
        {
            if (_hostProvider() == null)
                return Task.FromResult<LiveChannelSnapshot?>(null);

            // Real Twitch channel endpoint + parser pending; WebViewJsonFetch.GetAsync(host, url, TwitchOrigin) when ready.
            _ = channelLogin;
            _ = ct;
            return Task.FromResult<LiveChannelSnapshot?>(null);
        }

        private async Task<string?> SelectBestWithStubFallbackAsync(
            DropsCampaign campaign,
            string? preferredLogin,
            CancellationToken ct)
        {
            IReadOnlyList<string> logins = EligibleStreamerParser.ParseChannelLogins(campaign);
            if (logins.Count == 0)
                return null;

            IEnumerable<string> ordered = string.IsNullOrWhiteSpace(preferredLogin)
                ? logins
                : new[] { preferredLogin }.Concat(logins.Where(l => !string.Equals(l, preferredLogin, StringComparison.OrdinalIgnoreCase)));

            foreach (string login in ordered)
            {
                ct.ThrowIfCancellationRequested();
                LiveChannelSnapshot? snapshot = await GetChannelAsync(login, ct);
                if (IsEligibleWithStubFallback(snapshot, campaign.Slug))
                {
                    AppLogger.Debug("TwitchSelection", $"Stub-selected login '{login}' for '{campaign.Name}' (real Twitch API pending).");
                    return login;
                }
            }

            return null;
        }

        private static LiveChannelSnapshot CreateStubSnapshot(string channelLogin) =>
            new(
                channelLogin.Trim(),
                IsLive: true,
                CategorySlugs: [],
                ProfileImageUrl: null,
                DisplayName: channelLogin.Trim());

        private static bool IsEligibleWithStubFallback(LiveChannelSnapshot? snapshot, string? gameSlug)
        {
            if (LiveChannelEligibility.IsEligible(snapshot, gameSlug))
                return true;

            return snapshot is { IsLive: true, CategorySlugs.Count: 0 }
                   && !string.IsNullOrWhiteSpace(snapshot.Login);
        }
    }
}