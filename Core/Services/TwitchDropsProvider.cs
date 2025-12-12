using Core.Enums;
using Core.Interfaces;
using Core.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.Services
{    
    /// <summary>
    /// Provides access to Twitch drops campaigns and related functionality.
    /// </summary>
    /// <remarks>Use this provider to retrieve active Twitch drops campaigns for integration with
    /// drops-enabled applications or services. This class is intended to be used in conjunction with a compatible web
    /// view host to access Twitch campaign data.</remarks>
    public class TwitchDropsProvider(IGqlService gql) : DropsCampaignProviderBase
    {
        /// <summary>
        /// Gets the platform associated with this instance.
        /// </summary>
        public override Platform Platform => Platform.Twitch;

        private readonly IGqlService _gql = gql;

        public override async Task<IReadOnlyList<DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken ct = default)
        {
            //await host.EnsureInitializedAsync();

            //string rawJson = string.Empty;

            //while (string.IsNullOrEmpty(rawJson))
            //    try
            //    {
            //        rawJson = await host.CaptureViewerDropsDashboardResponseAsync(10000, ct);
            //    }
            //    catch (TimeoutException)
            //    {
            //        Debug.WriteLine("[TwitchDrops] Initial attempt to capture drops dashboard response timed out, refreshing and retrying...");
            //        await host.ForceRefreshAsync();
            //    }

            //List<DropsCampaign> campaigns = new List<DropsCampaign>();
            //bool includePaid = false;

            //JsonElement root = JsonDocument.Parse(rawJson).RootElement;

            //// Look in every single operation for rewardCampaignsAvailableToUser
            //foreach (JsonElement op in root.EnumerateArray())
            //{
            //    if (!op.TryGetProperty("data", out JsonElement data))
            //        continue;

            //    if (!data.TryGetProperty("currentUser", out JsonElement user))
            //        continue;

            //    if (data.TryGetProperty("rewardCampaignsAvailableToUser", out JsonElement list))
            //    {
            //        Debug.WriteLine($"[TwitchDrops] Found rewardCampaignsAvailableToUser in {(op.TryGetProperty("extensions", out JsonElement e) && e.TryGetProperty("operationName", out JsonElement n) ? n.GetString() : "unknown operation")}");

            //        foreach (JsonElement camp in list.EnumerateArray())
            //        {
            //            if (!camp.TryGetProperty("unlockRequirements", out JsonElement req))
            //                continue;

            //            int subs = req.GetProperty("subsGoal").GetInt32();
            //            int mins = req.GetProperty("minuteWatchedGoal").GetInt32();

            //            if (subs > 0 && !includePaid)
            //                continue;

            //            DropsReward[] rewards = [.. camp.GetProperty("rewards").EnumerateArray()
            //                .Select(r => new DropsReward(
            //                    Id: r.GetProperty("id").GetString()!,
            //                    Name: r.GetProperty("name").GetString()!,
            //                    ImageUrl: r.GetProperty("thumbnailImage").GetProperty("image1xURL").GetString()!,
            //                    RequiredMinutes: mins
            //                ))];

            //            if (rewards.Length == 0)
            //                continue;

            //            campaigns.Add(new DropsCampaign(
            //                Id: camp.GetProperty("id").GetString()!,
            //                Name: camp.GetProperty("name").GetString()!,
            //                GameName: "Unknown",
            //                GameImageUrl: camp.GetProperty("image").GetProperty("image1xURL").GetString(),
            //                StartsAt: DateTimeOffset.Parse(camp.GetProperty("startsAt").GetString()!),
            //                EndsAt: DateTimeOffset.Parse(camp.GetProperty("endsAt").GetString()!),
            //                Rewards: rewards,
            //                Platform: Platform,
            //                ConnectUrls: []
            //            ));
            //        }
            //    }
            //}

            //string rawProgress = string.Empty;

            //while (string.IsNullOrEmpty(rawProgress))
            //    try
            //    {
            //        rawProgress = await host.CaptureViewerDropsProgressResponseAsync(10000, ct);
            //    }
            //    catch (TimeoutException)
            //    {
            //        Debug.WriteLine("[TwitchDrops] Initial attempt to capture drops progress response timed out, refreshing and retrying...");
            //        await host.ForceRefreshAsync();
            //    }

            //if (!string.IsNullOrWhiteSpace(rawProgress) && rawProgress.Trim() != "null")
            //{
            //    try
            //    {
            //        JsonElement rootArray = JsonDocument.Parse(rawProgress).RootElement;

            //        foreach (JsonElement operation in rootArray.EnumerateArray())
            //        {
            //            if (!operation.TryGetProperty("data", out JsonElement data))
            //                continue;

            //            if (!data.TryGetProperty("currentUser", out JsonElement currentUser))
            //                continue;

            //            if (!currentUser.TryGetProperty("inventory", out JsonElement inventory))
            //                continue;

            //            // This is the array that contains all claimed + in-progress rewards
            //            if (!inventory.TryGetProperty("gameEventDrops", out JsonElement gameEventDrops) ||
            //                gameEventDrops.ValueKind == JsonValueKind.Null)
            //                continue;

            //            foreach (JsonElement reward in gameEventDrops.EnumerateArray())
            //            {
            //                string rewardId = reward.GetProperty("id").GetString()!;
            //                string rewardName = reward.GetProperty("name").GetString()!;
            //                string imageUrl = reward.GetProperty("imageURL").GetString()!;
            //                bool isClaimed = reward.TryGetProperty("lastAwardedAt", out _); // has lastAwardedAt → claimed, TODO VERIFY

            //                // Find any campaign that contains a reward with this exact ID
            //                foreach (DropsCampaign campaign in campaigns)
            //                {
            //                    DropsReward? matchingReward = campaign.Rewards.FirstOrDefault(r => r.Id == rewardId);
            //                    if (matchingReward == null)
            //                        continue;

            //                    DropsReward updatedReward = matchingReward with
            //                    {
            //                        Name = rewardName,
            //                        ImageUrl = imageUrl,
            //                        IsClaimed = isClaimed,
            //                        ProgressMinutes = isClaimed ? matchingReward.RequiredMinutes : matchingReward.ProgressMinutes
            //                    };

            //                    List<DropsReward> newList = [.. campaign.Rewards];
            //                    int idx = newList.IndexOf(matchingReward);
            //                    newList[idx] = updatedReward;

            //                    DropsCampaign updatedCampaign = campaign with { Rewards = newList.AsReadOnly() };
            //                    int campIdx = campaigns.IndexOf(campaign);
            //                    campaigns[campIdx] = updatedCampaign;

            //                    break; // reward found and updated → no need to check other campaigns
            //                }
            //            }
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        Debug.WriteLine($"[TwitchDrops] Failed to parse progress payload: {ex.Message}");
            //    }
            //}

            //Debug.WriteLine($"[TwitchDrops] FINAL: {campaigns.Count} campaigns loaded (progress + claimed status merged)");
            //return campaigns.AsReadOnly();
            await host.EnsureInitializedAsync();

            // This triggers everything: headers, auth, hash discovery
            JsonObject variables = new JsonObject
            {
                ["fetchRewardCampaigns"] = true
            };

            JsonObject response = await _gql.QueryAsync("ViewerDropsDashboard", variables, ct);

            JsonObject? data = response["data"]?.AsObject();
            JsonObject? currentUser = data?["currentUser"]?.AsObject();
            JsonArray? dropCampaigns = currentUser?["dropCampaigns"]?.AsArray();

            if (dropCampaigns == null || dropCampaigns.Count == 0)
                return [];

            List<DropsCampaign> result = new();

            foreach (JsonObject? campaignNode in dropCampaigns.OfType<JsonObject>())
            {
                string id = campaignNode["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString();
                string name = campaignNode["name"]?.GetValue<string>() ?? "Unknown Campaign";

                JsonObject? game = campaignNode["game"]?.AsObject();
                string gameName = game?["displayName"]?.GetValue<string>() ?? "Unknown Game";
                string? gameImageUrl = game?["boxArtURL"]?.GetValue<string>();

                DateTimeOffset startsAt = DateTimeOffset.Parse(campaignNode["startAt"]?.GetValue<string>() ?? DateTimeOffset.UtcNow.ToString("o"));
                DateTimeOffset endsAt = DateTimeOffset.Parse(campaignNode["endAt"]?.GetValue<string>() ?? startsAt.AddDays(1).ToString("o"));

                // Some campaigns have timeBasedDrops, others don't (e.g. "Connect Account" only)
                JsonArray? timeBasedDrops = campaignNode["timeBasedDrops"]?.AsArray();

                List<DropsReward> rewards = new();

                if (timeBasedDrops != null && timeBasedDrops.Count > 0)
                {
                    foreach (JsonObject? drop in timeBasedDrops.OfType<JsonObject>())
                    {
                        int requiredMinutes = drop["requiredMinutesWatched"]?.GetValue<int>() ?? 0;

                        // Progress comes from the first drop's "self" edge (Twitch always nests it there)
                        JsonObject? selfEdge = drop["self"]?.AsObject();
                        int currentMinutes = selfEdge?["currentMinutesWatched"]?.GetValue<int>() ?? 0;
                        bool isClaimed = selfEdge?["isClaimed"]?.GetValue<bool>() ?? false;
                        string? dropInstanceId = selfEdge?["dropInstanceID"]?.GetValue<string>();

                        // Reward name can be in benefitEdges or directly in drop
                        string rewardName = "Unknown Reward";
                        string rewardImage = "";

                        JsonObject? benefit = drop["benefitEdges"]?.AsArray()
                            .FirstOrDefault()?["benefit"]?.AsObject();

                        if (benefit != null)
                        {
                            rewardName = benefit["name"]?.GetValue<string>() ?? rewardName;
                            rewardImage = benefit["image"]?["url1x"]?.GetValue<string>() ?? "";
                        }
                        else if (drop.ContainsKey("name"))
                        {
                            rewardName = drop["name"]?.GetValue<string>() ?? rewardName;
                        }

                        rewards.Add(new DropsReward(
                            Id: drop["id"]?.GetValue<string>() ?? "",
                            Name: rewardName,
                            ImageUrl: rewardImage,
                            RequiredMinutes: requiredMinutes,
                            ProgressMinutes: currentMinutes,
                            IsClaimed: isClaimed
                        ));
                    }
                }

                if (rewards.Count > 0)
                {
                    result.Add(new DropsCampaign(
                        Id: id,
                        Name: name,
                        GameName: gameName,
                        GameImageUrl: gameImageUrl ?? "",
                        StartsAt: startsAt,
                        EndsAt: endsAt,
                        Rewards: rewards.AsReadOnly(),
                        Platform: Platform.Twitch,
                        ConnectUrls: [.. new[] { campaignNode["accountLinkURL"]?.GetValue<string>() ?? "" }.Where(url => !string.IsNullOrEmpty(url))]
                    ));
                }
            }

            Debug.WriteLine($"[TwitchDrops] Loaded {result.Count} active campaigns (including connect-only)");
            return result.AsReadOnly();
        }
    }
}