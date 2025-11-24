using System.Diagnostics;
using System.Text.Json;
using Core.Interfaces;
using Core.Models;
using Core.Enums;

namespace Core.Services
{
    /// <summary>
    /// Provides access to Twitch drops campaigns and related functionality.
    /// </summary>
    /// <remarks>Use this provider to retrieve active Twitch drops campaigns for integration with
    /// drops-enabled applications or services. This class is intended to be used in conjunction with a compatible web
    /// view host to access Twitch campaign data.</remarks>
    public class TwitchDropsProvider : DropsCampaignProviderBase
    {
        /// <summary>
        /// Gets the platform associated with this instance.
        /// </summary>
        public override Platform Platform => Platform.Twitch;

        /// <summary>
        /// Asynchronously retrieves a list of active Twitch Drops campaigns available to the current user.
        /// </summary>
        /// <remarks>This method navigates the provided web view host to the Twitch Drops campaigns
        /// dashboard and captures the relevant campaign data. Only campaigns with available rewards are included in the
        /// result. Paid campaigns requiring subscriptions are excluded unless configured otherwise. The operation may
        /// retry automatically if the initial data capture times out.</remarks>
        /// <param name="host">The web view host used to interact with the Twitch Drops dashboard and capture campaign data. Must be
        /// initialized before calling this method.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A read-only list of <see cref="DropsCampaign"/> objects representing the active campaigns. The list will be
        /// empty if no campaigns are available.</returns>
        public override async Task<IReadOnlyList<DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken ct = default)
        {
            await host.EnsureInitializedAsync();
            await host.NavigateAsync("https://twitch.tv/drops/campaigns?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());
            string rawJson = string.Empty;

            while (string.IsNullOrEmpty(rawJson))
                try
                {
                    rawJson = await host.CaptureViewerDropsDashboardResponseAsync(10000, ct);
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine("[TwitchDrops] Initial attempt to capture drops dashboard response timed out, refreshing and retrying...");
                    await host.ForceRefreshAsync();
                }

            List<DropsCampaign> campaigns = new List<DropsCampaign>();
            bool includePaid = true;

            JsonElement root = JsonDocument.Parse(rawJson).RootElement;

            // Look in every single operation for rewardCampaignsAvailableToUser
            foreach (JsonElement op in root.EnumerateArray())
            {
                if (!op.TryGetProperty("data", out JsonElement data))
                    continue;

                if (!data.TryGetProperty("currentUser", out JsonElement user))
                    continue;

                if (data.TryGetProperty("rewardCampaignsAvailableToUser", out JsonElement list))
                {
                    Debug.WriteLine($"[TwitchDrops] Found rewardCampaignsAvailableToUser in {(op.TryGetProperty("extensions", out JsonElement e) && e.TryGetProperty("operationName", out JsonElement n) ? n.GetString() : "unknown operation")}");

                    foreach (JsonElement camp in list.EnumerateArray())
                    {
                        if (!camp.TryGetProperty("unlockRequirements", out JsonElement req))
                            continue;
                        int subs = req.GetProperty("subsGoal").GetInt32();
                        int mins = req.GetProperty("minuteWatchedGoal").GetInt32();

                        if (subs > 0 && !includePaid)
                            continue;

                        DropsReward[] rewards = [.. camp.GetProperty("rewards").EnumerateArray()
                            .Select(r => new DropsReward(
                                Id: r.GetProperty("id").GetString()!,
                                Name: r.GetProperty("name").GetString()!,
                                ImageUrl: r.GetProperty("thumbnailImage").GetProperty("image1xURL").GetString()!,
                                RequiredMinutes: mins
                            ))];

                        if (rewards.Length == 0)
                            continue;

                        campaigns.Add(new DropsCampaign(
                            Id: camp.GetProperty("id").GetString()!,
                            Name: camp.GetProperty("name").GetString()!,
                            GameName: "Unknown",
                            GameImageUrl: camp.GetProperty("image").GetProperty("image1xURL").GetString(),
                            StartsAt: DateTimeOffset.Parse(camp.GetProperty("startsAt").GetString()!),
                            EndsAt: DateTimeOffset.Parse(camp.GetProperty("endsAt").GetString()!),
                            Rewards: rewards,
                            Platform: Platform,
                            ConnectUrls: []
                        ));
                    }
                }
            }

            Debug.WriteLine($"[TwitchDrops] FINAL: {campaigns.Count} campaigns loaded");
            return campaigns.AsReadOnly();
        }
    }
}