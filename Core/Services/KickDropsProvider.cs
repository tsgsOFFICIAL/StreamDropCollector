using System.Text.Json;
using Core.Logging;
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
            try
            {
                AppLogger.Info("KickDrops", "Fetching active campaigns started.");
                await host.EnsureInitializedAsync();

                // 1. Get full campaign data (Channels, Rewards, etc.)
                await host.NavigateAsync("https://web.kick.com/api/v1/drops/campaigns");

                string rawJson = await host.ExecuteScriptAsync("document.body.innerText");

                // Step 1: decode the JSON string safely
                string? json = JsonSerializer.Deserialize<string>(rawJson);

                if (string.IsNullOrWhiteSpace(json))
                {
                    AppLogger.Warn("KickDrops", "Kick campaign API returned empty payload.");
                    return Array.Empty<DropsCampaign>().AsReadOnly();
                }

                // Step 2: parse actual JSON
                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out JsonElement dataArray))
                {
                    AppLogger.Warn("KickDrops", "Kick campaign API payload missing 'data' property.");
                    return Array.Empty<DropsCampaign>().AsReadOnly();
                }

                List<DropsCampaign> campaigns = new List<DropsCampaign>();

                foreach (JsonElement campaign in dataArray.EnumerateArray())
                {
                    if (!campaign.TryGetProperty("status", out JsonElement status) || status.GetString() != "active")
                        continue;

                    campaign.TryGetProperty("category", out JsonElement category);

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

                    // Category-less campaigns (e.g. Watch ANYONE, in any category)
                    if (category.ValueKind == JsonValueKind.Undefined && connectUrls.Count == 0)
                    {
                        connectUrls.Add("https://kick.com/browse?sort=viewers_high_to_low");
                        general = true;
                    }

                    // General drops = watch ANYONE in category
                    if (connectUrls.Count == 0 && category.ValueKind != JsonValueKind.Undefined)
                    {
                        string slug = category.GetProperty("slug").GetString()!;
                        connectUrls.Add($"https://kick.com/category/{slug}/drops");
                        general = true;
                    }

                    // Remove duplicates
                    connectUrls = [.. connectUrls.Distinct()];

                    if (category.ValueKind == JsonValueKind.Undefined)
                    {
                        JsonElement organization = campaign.GetProperty("organization");

                        campaigns.Add(new DropsCampaign(
                            Id: campaign.GetProperty("id").GetString()!,
                            Name: campaign.GetProperty("name").GetString()!,
                            Slug: "",
                            GameName: organization.GetProperty("name").GetString()!,
                            GameImageUrl: organization.GetProperty("logo_url").GetString(),
                            StartsAt: DateTimeOffset.Parse(campaign.GetProperty("starts_at").GetString()!),
                            EndsAt: DateTimeOffset.Parse(campaign.GetProperty("ends_at").GetString()!),
                            Rewards: rewards,
                            Platform: Platform,
                            ConnectUrls: connectUrls.AsReadOnly(),
                            IsGeneralDrop: general
                        ));
                    }
                    else
                    {
                        campaigns.Add(new DropsCampaign(
                            Id: campaign.GetProperty("id").GetString()!,
                            Name: campaign.GetProperty("name").GetString()!,
                            Slug: category.GetProperty("slug").GetString()!,
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
                }

                // 2. Get progress + claimed status
                string rawProgress = string.Empty;
                const int maxProgressAttempts = 3;

                for (int attempt = 1; attempt <= maxProgressAttempts && string.IsNullOrEmpty(rawProgress); attempt++)
                {
                    try
                    {
                        rawProgress = await host.CaptureProgressResponseAsync(10000, ct);
                    }
                    catch (TimeoutException ex)
                    {
                        bool willRetry = attempt < maxProgressAttempts;
                        AppLogger.Warn("KickDrops", $"Progress capture timed out (attempt {attempt}/{maxProgressAttempts}); {(willRetry ? "forcing refresh and retrying." : "continuing without progress.")} {ex.Message}");
                        if (willRetry)
                            await host.ForceRefreshAsync();
                    }
                }

                // 3. Merge progress into campaigns (skip if no payload was captured).
                if (!string.IsNullOrEmpty(rawProgress))
                {
                    using JsonDocument progressDoc = JsonDocument.Parse(rawProgress);

                    if (progressDoc.RootElement.TryGetProperty("data", out JsonElement progressArray))
                    {
                        foreach (JsonElement item in progressArray.EnumerateArray())
                        {
                            string campaignId = item.GetProperty("id").GetString()!;
                            int campaignProgressUnits = item.GetProperty("progress_units").GetInt32();

                            DropsCampaign? campaign = campaigns.FirstOrDefault(c => c.Id == campaignId);
                            if (campaign == null)
                                continue;

                            // Build per-reward claimed status + progress lookup from the API
                            Dictionary<string, (bool isClaimed, int progressUnits)> rewardData
                                = new Dictionary<string, (bool, int)>();

                            foreach (JsonElement apiReward in item.GetProperty("rewards").EnumerateArray())
                            {
                                string rid = apiReward.GetProperty("id").GetString()!;
                                bool claimed = apiReward.GetProperty("claimed").GetBoolean();

                                if (apiReward.TryGetProperty("progress_units", out JsonElement perRewardProgress))
                                {
                                    rewardData[rid] = (claimed, perRewardProgress.GetInt32());
                                }
                                else
                                {
                                    // Fallback: use campaign-level total, capped at RequiredMinutes
                                    DropsReward? existing = campaign.Rewards.FirstOrDefault(r => r.Id == rid);
                                    int capped = existing != null
                                        ? Math.Min(campaignProgressUnits, existing.RequiredMinutes)
                                        : campaignProgressUnits;
                                    rewardData[rid] = (claimed, capped);
                                }
                            }

                            // Rebuild ALL rewards at once, then update the campaign ONCE
                            // to avoid the IndexOutOfRange crash when a campaign has multiple rewards
                            // (the old code replaced the campaign in the list per-reward, but records
                            // use value equality, so IndexOf failed on the second iteration).
                            List<DropsReward> mergedRewards = new List<DropsReward>(campaign.Rewards.Count);
                            foreach (DropsReward reward in campaign.Rewards)
                            {
                                if (rewardData.TryGetValue(reward.Id, out var data))
                                {
                                    mergedRewards.Add(reward with
                                    {
                                        ProgressMinutes = data.progressUnits,
                                        IsClaimed = data.isClaimed
                                    });
                                }
                                else
                                {
                                    mergedRewards.Add(reward);
                                }
                            }

                            int campIndex = campaigns.IndexOf(campaign);
                            campaigns[campIndex] = campaign with { Rewards = mergedRewards.AsReadOnly() };
                        }
                    }
                }

                AppLogger.Debug("KickDrops", $"LOADED {campaigns.Count} campaigns with progress");
                AppLogger.Info("KickDrops", $"Active campaigns fetched successfully. count={campaigns.Count}");
                return campaigns.AsReadOnly();
            }
            catch (Exception ex)
            {
                AppLogger.Error("KickDrops", "Fetching active campaigns failed.", ex);
                return [];
            }
        }
    }
}