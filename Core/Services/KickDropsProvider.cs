using System.Diagnostics;
using System.Text.Json;
using Core.Interfaces;
using Core.Models;
using Core.Enums;

namespace Core.Services
{
    /// <summary>
    /// Provides an implementation of a drops campaign provider for the Kick platform.
    /// </summary>
    /// <remarks>Use this class to retrieve active drops campaigns available on Kick. Inherits common
    /// functionality from DropsCampaignProviderBase and specializes it for the Kick platform.</remarks>
    public class KickDropsProvider : DropsCampaignProviderBase
    {
        /// <summary>
        /// Gets the streaming platform associated with this instance.
        /// </summary>
        public override Platform Platform => Platform.Kick;
        /// <summary>
        /// Asynchronously retrieves the list of currently active drops campaigns from Kick, including associated
        /// channels, rewards, and the user's progress and claim status for each reward.
        /// </summary>
        /// <remarks>This method combines campaign metadata with the user's progress and claim status for
        /// each reward. The returned campaigns reflect the current state as seen on Kick. The operation may take
        /// several seconds to complete, depending on network conditions and site responsiveness.</remarks>
        /// <param name="host">The web view host used to interact with the Kick website and capture campaign and progress data. Must be
        /// initialized before calling this method.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A read-only list of active drops campaigns, each containing campaign details, available channels, rewards,
        /// and the user's progress and claim status. Returns an empty list if no active campaigns are found.</returns>
        public override async Task<IReadOnlyList<DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken ct = default)
        {
            await host.EnsureInitializedAsync();

            // 1. Get full campaign data (Channels, Rewards, etc.)
            await host.NavigateAsync("https://web.kick.com/api/v1/drops/campaigns");

            string rawJson = await host.ExecuteScriptAsync("document.body.innerText");
            string json = rawJson.Trim('"').Replace("\\n", "").Replace("\\\"", "\"");
            
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return Array.Empty<DropsCampaign>().AsReadOnly();

            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out JsonElement dataArray))
                return Array.Empty<DropsCampaign>().AsReadOnly();

            List<DropsCampaign> campaigns = new List<DropsCampaign>();

            foreach (JsonElement campaign in dataArray.EnumerateArray())
            {
                if (!campaign.TryGetProperty("status", out JsonElement status) || status.GetString() != "active")
                    continue;

                JsonElement category = campaign.GetProperty("category");

                DropsReward[] rewards = [.. campaign.GetProperty("rewards")
                .EnumerateArray()
                .Select(r => new DropsReward(
                    Id: r.GetProperty("id").GetString()!,
                    Name: r.GetProperty("name").GetString()!,
                    ImageUrl: "https://ext.cdn.kick.com/" + r.GetProperty("image_url").GetString(),
                    RequiredMinutes: r.GetProperty("required_units").GetInt32()
                ))];

                if (rewards.Length == 0)
                    continue;

                // All the available channels for this campaign
                List<string> connectUrls = new List<string>();

                if (campaign.TryGetProperty("channels", out JsonElement channels) && channels.GetArrayLength() > 0)
                {
                    foreach (JsonElement channel in channels.EnumerateArray())
                    {
                        string? username = channel.TryGetProperty("slug", out JsonElement slug)
                            ? slug.GetString()
                            : channel.GetProperty("user").GetProperty("username").GetString();

                        if (!string.IsNullOrEmpty(username))
                            connectUrls.Add($"https://kick.com/{username.ToLowerInvariant()}");
                    }
                }

                bool general = false;

                // General drops = watch ANYONE in category
                if (connectUrls.Count == 0)
                {
                    string slug = category.GetProperty("slug").GetString()!;
                    connectUrls.Add($"https://kick.com/category/{slug}/drops");
                    general = true;
                }

                // Remove duplicates + sort by preference (optional)
                connectUrls = [.. connectUrls.Distinct()];

                campaigns.Add(new DropsCampaign(
                    Id: campaign.GetProperty("id").GetString()!,
                    Name: campaign.GetProperty("name").GetString()!,
                    GameName: category.GetProperty("name").GetString()!,
                    GameImageUrl: category.GetProperty("image_url").GetString(),
                    StartsAt: DateTimeOffset.Parse(campaign.GetProperty("starts_at").GetString()!),
                    EndsAt: DateTimeOffset.Parse(campaign.GetProperty("ends_at").GetString()!),
                    Rewards: rewards,
                    Platform: Platform,
                    ConnectUrls: connectUrls.AsReadOnly(),
                    IsGeneralDrop: general
                ));
            }

            // 2. Get progress + claimed status
            string rawProgress = string.Empty;

            while (string.IsNullOrEmpty(rawProgress))
                try
                {
                    rawProgress = await host.CaptureProgressResponseAsync(10000, ct);
                }
                catch (TimeoutException)
                {
                    Debug.WriteLine("[Kick Drops] Initial attempt to capture /progress response timed out, refreshing and retrying...");
                    await host.ForceRefreshAsync();
                }

            // 3. Merge progress into campaigns
            JsonDocument progressDoc = JsonDocument.Parse(rawProgress);

            if (progressDoc.RootElement.TryGetProperty("data", out JsonElement progressArray))
            {
                foreach (JsonElement item in progressArray.EnumerateArray())
                {
                    string campaignId = item.GetProperty("id").GetString()!;

                    foreach (JsonElement reward in item.GetProperty("rewards").EnumerateArray())
                    {
                        string rewardId = reward.GetProperty("id").GetString()!;

                        DropsCampaign? campaign = campaigns.FirstOrDefault(c => c.Id == campaignId);
                        if (campaign == null)
                            continue;

                        DropsReward? targetReward = campaign.Rewards.FirstOrDefault(r => r.Id == rewardId);
                        if (targetReward == null)
                            continue;

                        // UPDATE IN-PLACE
                        targetReward = targetReward with
                        {
                            ProgressMinutes = item.GetProperty("progress_units").GetInt32(),
                            IsClaimed = reward.GetProperty("claimed").GetBoolean()
                        };

                        // Replace in list (records are immutable)
                        List<DropsReward> list = [.. campaign.Rewards];
                        int index = list.IndexOf(campaign.Rewards.First(r => r.Id == rewardId));
                        list[index] = targetReward;

                        // Replace in campaign
                        DropsCampaign updatedCampaign = campaign with { Rewards = list.AsReadOnly() };
                        int campIndex = campaigns.IndexOf(campaign);
                        campaigns[campIndex] = updatedCampaign;
                    }
                }
            }

            Debug.WriteLine($"[Kick Drops] LOADED {campaigns.Count} campaigns with progress");
            return campaigns.AsReadOnly();
        }        
    }
}