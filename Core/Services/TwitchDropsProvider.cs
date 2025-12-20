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

        public override async Task<IReadOnlyList<DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken ct = default)
        {
            await host.EnsureInitializedAsync();

            JsonObject dashboard = await gql.QueryFullDropsDashboardAsync(ct);

            JsonArray? campaigns = dashboard["data"]?["currentUser"]?["dropCampaigns"]?.AsArray();

            string userId = dashboard["data"]?["currentUser"]?["id"]?.GetValue<string>() ?? "";

            campaigns?.RemoveAll(campaign =>
            {
                if (campaign is not JsonObject campaignObj)
                    return true; // Remove

                // Remove if status != "ACTIVE"
                if (!campaignObj.TryGetPropertyValue("status", out JsonNode? statusNode) ||
                    statusNode?.GetValue<string>() != "ACTIVE")
                    return true;

                // Remove if not connected
                if (!campaignObj.TryGetPropertyValue("self", out JsonNode? selfNode) ||
                    selfNode is not JsonObject selfObj ||
                    !selfObj.TryGetPropertyValue("isAccountConnected", out JsonNode? connectedNode) ||
                    connectedNode?.GetValue<bool>() != true)
                    return true; // Remove

                return false; // Keep
            });

            if (campaigns == null || campaigns.Count == 0)
                return [];

            List<(string dropID, string channelLogin)> requests = new List<(string dropID, string channelLogin)>();

            foreach (JsonNode? campaign in campaigns)
            {
                if (campaign is not JsonObject campObj)
                    continue;

                string? dropId = campObj.TryGetPropertyValue("id", out JsonNode? idNode) ? idNode?.GetValue<string>() : null;
                if (string.IsNullOrEmpty(dropId))
                    continue;

                requests.Add((dropId, userId)); // Use your fetched channelLogin
            }

            if (requests.Count == 0)
            {
                Debug.WriteLine("[Drops] No valid dropIDs to query.");
                return [];
            }

            // Fetch the full details in batches
            Dictionary<string, JsonObject> campaignDetails = await gql.QueryDropCampaignDetailsBatchAsync(requests, ct);

            Debug.WriteLine($"[Drops] Successfully fetched detailed data for {campaignDetails.Count} campaigns.");
            foreach (KeyValuePair<string, JsonObject> kvp in campaignDetails)
            {
                string dropId = kvp.Key;
                JsonObject details = kvp.Value;

                // Example parsing of useful data
                Debug.WriteLine($"Campaign {dropId}:");
                // Add your specific extraction here (rewards, progress, etc.)
            }

            List<DropsCampaign> result = new List<DropsCampaign>();
            foreach (JsonObject camp in campaignDetails.Values)
            {
                result.Add(ParseCampaignFromDetails(camp));
            }

            return result.AsReadOnly();
        }

        /// <summary>
        /// Creates a new instance of the DropsCampaign class by extracting campaign details from the specified JSON
        /// object.
        /// </summary>
        /// <remarks>If required fields are missing or invalid in the JSON object, default values are used
        /// for those fields. The method assumes the input follows the expected structure for campaign details as
        /// provided by the data source.</remarks>
        /// <param name="detailedData">A JsonObject containing detailed information about the campaign, including identifiers, game data, time
        /// frames, rewards, and channel information. Must not be null and is expected to follow the expected schema for
        /// campaign details.</param>
        /// <returns>A DropsCampaign object populated with data parsed from the provided JSON object. The returned object
        /// contains campaign metadata, associated rewards, and relevant channel URLs.</returns>
        private static DropsCampaign ParseCampaignFromDetails(JsonObject detailedData)
        {
            string id = detailedData["id"]?.GetValue<string>() ?? "";
            string name = detailedData["name"]?.GetValue<string>() ?? "Unknown Campaign";

            JsonObject? game = detailedData["game"]?.AsObject();
            string gameName = game?["displayName"]?.GetValue<string>() ?? "Unknown Game";
            string? gameImage = detailedData?["imageURL"]?.GetValue<string>();

            DateTimeOffset startsAt = DateTimeOffset.Parse(detailedData?["startAt"]?.GetValue<string>() ?? DateTimeOffset.UtcNow.ToString("o"));
            DateTimeOffset endsAt = DateTimeOffset.Parse(detailedData?["endAt"]?.GetValue<string>() ?? startsAt.AddDays(7).ToString("o"));

            List<string> connectUrls = new List<string>();

            JsonObject? allow = detailedData?["allow"]?.AsObject();
            JsonArray? channels = allow?["channels"]?.AsArray();

            if (channels != null)
            {
                foreach (JsonObject channel in channels.OfType<JsonObject>())
                {
                    string? url = channel?["name"]?.GetValue<string>();
                    url = url != null ? $"https://www.twitch.tv/{url}" : null;

                    if (url != null)
                        connectUrls.Add(url);
                }
            }

            List<DropsReward> rewards = new List<DropsReward>();
            JsonArray? timeBasedDrops = detailedData?["timeBasedDrops"]?.AsArray();

            if (timeBasedDrops != null && timeBasedDrops.Count > 0)
            {
                foreach (JsonObject drop in timeBasedDrops.OfType<JsonObject>())
                {
                    string dropId = drop["id"]?.GetValue<string>() ?? "";
                    int requiredMinutes = drop["requiredMinutesWatched"]?.GetValue<int>() ?? 0;
                    int requiredSubs = drop["requiredSubs"]?.GetValue<int>() ?? 0;

                    int currentMinutes = 0;
                    bool isClaimed = false;

                    JsonArray? benefitEdges = drop["benefitEdges"]?.AsArray();
                    if (benefitEdges == null || benefitEdges.Count == 0)
                        continue;

                    foreach (JsonObject benefitEdge in benefitEdges.OfType<JsonObject>())
                    {
                        JsonObject? benefit = benefitEdge["benefit"]?.AsObject();
                        if (benefit == null)
                            continue;

                        string? benefitId = benefit["id"]?.GetValue<string>();
                        if (benefitId == null)
                            continue;

                        string rewardName = benefit["name"]?.GetValue<string>()
                                            ?? drop["name"]?.GetValue<string>()
                                            ?? "Unknown Reward";

                        string rewardImage = benefit["imageAssetURL"]?.GetValue<string>() ?? "";

                        rewards.Add(new DropsReward(
                            Id: dropId,
                            Name: rewardName,
                            ImageUrl: rewardImage,
                            RequiredMinutes: requiredMinutes,
                            ProgressMinutes: currentMinutes,
                            IsClaimed: isClaimed,
                            DropInstanceId: benefitId
                        ));
                    }
                }
            }

            return new DropsCampaign(
                Id: id,
                Name: name,
                GameName: gameName,
                GameImageUrl: gameImage,
                StartsAt: startsAt,
                EndsAt: endsAt,
                Rewards: rewards.AsReadOnly(),
                Platform: Platform.Twitch,
                ConnectUrls: connectUrls
            );
        }
    }
}