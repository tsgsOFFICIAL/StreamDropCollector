using System.Text.Json.Nodes;
using Core.Interfaces;
using Core.Logging;
using Core.Mining;
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
    public class TwitchDropsProvider(IGqlService gql) : DropsCampaignProviderBase
    {
        /// <summary>
        /// Gets the platform associated with this instance.
        /// </summary>
        public override Platform Platform => Platform.Twitch;

        public override async Task<IReadOnlyList<DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken ct = default)
        {
            try
            {
                AppLogger.Debug("TwitchDrops", "Fetching active campaigns started.");
                await host.EnsureInitializedAsync();

                JsonArray dashboard = await gql.QueryFullDropsDashboardAsync(ct);

                JsonObject ongoingCampaigns = dashboard[0]!.AsObject();
                JsonObject activeCampaigns = dashboard[1]!.AsObject();

                JsonArray? campaigns = activeCampaigns["data"]?["currentUser"]?["dropCampaigns"]?.AsArray();

                string userId = activeCampaigns["data"]?["currentUser"]?["id"]?.GetValue<string>() ?? "";
                gql.UserId = userId;

                KnownGames = CollectKnownGames(activeCampaigns["data"]);

                // "Open reward campaigns" are a separate Twitch system; they are shown but never mined or claimed.
                List<DropsCampaign> rewardCampaigns = await GetRewardCampaignsAsync(activeCampaigns, ct);

                campaigns?.RemoveAll(campaign =>
                {
                    if (campaign is not JsonObject campaignObj)
                        return true; // Remove

                    string campaignLabel = $"{campaignObj["name"]} ({campaignObj["id"]})";

                    // Remove if status != "ACTIVE"
                    if (!campaignObj.TryGetPropertyValue("status", out JsonNode? statusNode) ||
                        statusNode?.GetValue<string>() != "ACTIVE")
                    {
                        AppLogger.Debug("TwitchDrops", $"Filtered out '{campaignLabel}': status={statusNode}");
                        return true;
                    }

                    // Remove if not connected (campaigns that link to Twitch itself, e.g. chat badges, need no external account)
                    if (!campaignObj.TryGetPropertyValue("self", out JsonNode? selfNode) ||
                        selfNode is not JsonObject selfObj ||
                        !selfObj.TryGetPropertyValue("isAccountConnected", out JsonNode? connectedNode) ||
                        (connectedNode?.GetValue<bool>() != true && !LinksToTwitchItself(campaignObj)))
                    {
                        AppLogger.Debug("TwitchDrops", $"Filtered out '{campaignLabel}': self={selfNode?.ToJsonString() ?? "null"}");
                        return true; // Remove
                    }

                    return false; // Keep
                });

                AppLogger.Debug("TwitchDrops", $"Campaigns after status/account filter: count={campaigns?.Count ?? 0}");

                if (campaigns == null || campaigns.Count == 0)
                {
                    AppLogger.Warn("TwitchDrops", "No Twitch campaigns remained after filtering.");
                    return rewardCampaigns.AsReadOnly();
                }

                List<(string dropID, string channelLogin)> requests = new List<(string dropID, string channelLogin)>();

                foreach (JsonNode? campaign in campaigns)
                {
                    if (campaign is not JsonObject campObj)
                        continue;

                    string? dropId = campObj.TryGetPropertyValue("id", out JsonNode? idNode) ? idNode?.GetValue<string>() : null;
                    if (string.IsNullOrEmpty(dropId))
                        continue;

                    requests.Add((dropId, userId));
                }

                if (requests.Count == 0)
                {
                    AppLogger.Debug("TwitchDrops", "No valid dropIDs to query.");
                    AppLogger.Warn("TwitchDrops", "No valid drop IDs available for details query.");
                    return rewardCampaigns.AsReadOnly();
                }

                // Fetch the full details in batches
                Dictionary<string, JsonObject> campaignDetails = await gql.QueryDropCampaignDetailsBatchAsync(requests, ct);

                AppLogger.Debug("TwitchDrops", $"Successfully fetched detailed data for {campaignDetails.Count} campaigns.");
                AppLogger.Debug("TwitchDrops", $"Campaign details fetched. requested={requests.Count}, received={campaignDetails.Count}");

                List<DropsCampaign> result = new List<DropsCampaign>();
                foreach (JsonObject camp in campaignDetails.Values)
                {
                    DropsCampaign? dropCampaign = ParseCampaignFromDetails(camp);

                    if (dropCampaign != null)
                        result.Add(dropCampaign);
                }

                JsonArray dropCampaignsInProgress = ongoingCampaigns["data"]?["currentUser"]?["inventory"]?["dropCampaignsInProgress"]?.AsArray() ?? new JsonArray();

                // Already finished/claimed drops. "gameEventDropsConnection" now permanently returns a GQL
                // "service error" (null) on Twitch's backend, so already-claimed rewards with no in-progress
                // record (e.g. lower tiers auto-claimed alongside a higher one) must come from
                // "earnedDropRewards" instead - its edge nodes carry item.id (== the reward's benefit id,
                // i.e. DropInstanceId) and a "CLAIMED" status.
                JsonArray earnedDropRewardEdges = ongoingCampaigns["data"]?["currentUser"]?["inventory"]?["earnedDropRewards"]?["edges"]?.AsArray() ?? new JsonArray();

                // Benefit ids of everything ever claimed ("gameEventDropsConnection", paged by the GQL service). Only
                // used for badges, which are one-per-account, so a past claim under another campaign still counts.
                HashSet<string> claimedBenefitIds = (ongoingCampaigns["data"]?["currentUser"]?["inventory"]?["gameEventDropsConnection"]?["edges"]?.AsArray() ?? [])
                    .Select(edge => edge?["node"]?["benefit"]?["id"]?.GetValue<string>())
                    .OfType<string>()
                    .ToHashSet();

                // Create a new list with updated campaigns
                List<DropsCampaign> updatedResult = new List<DropsCampaign>();

                foreach (DropsCampaign dropCampaign in result)
                {
                    // Find matching progress for this campaign (in-progress data)
                    JsonObject? matchingProgress = dropCampaignsInProgress.OfType<JsonObject>().FirstOrDefault(c => c["id"]?.GetValue<string>() == dropCampaign.Id);

                    JsonArray? timeBasedDropsProgress = matchingProgress?["timeBasedDrops"]?.AsArray();

                    // Prepare updated rewards list for this campaign
                    List<DropsReward> updatedRewardsForThisCampaign = new List<DropsReward>();

                    foreach (DropsReward reward in dropCampaign.Rewards)
                    {
                        DropsReward updatedReward = reward; // Start with original
                        bool hasInProgressRecord = false;

                        // 1. Apply in-progress data if available (progress minutes + claim status)
                        if (timeBasedDropsProgress != null)
                        {
                            JsonObject? matchingDropProgress = timeBasedDropsProgress.OfType<JsonObject>()
                                .FirstOrDefault(d => d["id"]?.GetValue<string>() == reward.Id);

                            if (matchingDropProgress != null)
                            {
                                hasInProgressRecord = true;
                                int progressMinutes = matchingDropProgress["self"]?["currentMinutesWatched"]?.GetValue<int>() ?? reward.ProgressMinutes;
                                bool isClaimed = matchingDropProgress["self"]?["isClaimed"]?.GetValue<bool>() ?? reward.IsClaimed;

                                updatedReward = updatedReward with
                                {
                                    ProgressMinutes = progressMinutes,
                                    IsClaimed = isClaimed
                                };
                            }
                        }

                        // 2. Apply earnedDropRewards (completed drops) - these mark rewards as claimed via DropInstanceId
                        JsonObject? matchingEarnedDrop = earnedDropRewardEdges.OfType<JsonObject>()
                            .Select(edge => edge["node"]?.AsObject())
                            .FirstOrDefault(node =>
                                node?["item"]?["id"]?.GetValue<string>() == reward.DropInstanceId &&
                                node?["status"]?.GetValue<string>() == "CLAIMED");

                        // Diagnostics only (no behaviour change): record which earned entry the benefit id matched and
                        // whether it belongs to this campaign, so recurring benefits that look claimed can be traced.
                        AppLogger.Debug(
                            "TwitchDrops",
                            $"Reward id check. campaignId={dropCampaign.Id} campaign='{dropCampaign.Name}' dropId={reward.Id} " +
                            $"benefitId={reward.DropInstanceId} inProgressClaimed={updatedReward.IsClaimed} " +
                            $"earnedMatch={(matchingEarnedDrop != null)}");

                        if (matchingEarnedDrop != null &&
                            EarnedRewardMatcher.IsClaimedByEarnedEntries(reward, dropCampaign.Id, dropCampaign.Rewards, earnedDropRewardEdges, hasInProgressRecord))
                        {
                            string? earnedCampaignId = matchingEarnedDrop["campaign"]?["id"]?.GetValue<string>();
                            if (!string.Equals(earnedCampaignId, dropCampaign.Id, StringComparison.Ordinal))
                            {
                                AppLogger.Warn(
                                    "TwitchDrops",
                                    $"Earned entry matched by benefit id but belongs to a different campaign. " +
                                    $"currentCampaignId={dropCampaign.Id} campaign='{dropCampaign.Name}' dropId={reward.Id} benefitId={reward.DropInstanceId} " +
                                    $"earnedCampaignId={earnedCampaignId ?? "null"} earnedAt={matchingEarnedDrop["earnedAt"]?.ToString() ?? "null"} " +
                                    $"inProgressClaimedBefore={updatedReward.IsClaimed} progressBefore={updatedReward.ProgressMinutes}/{reward.RequiredMinutes}");
                            }

                            // This reward has been fully claimed via a game event drop
                            updatedReward = updatedReward with
                            {
                                IsClaimed = true,
                                ProgressMinutes = reward.RequiredMinutes // Mark as fully completed
                            };
                        }

                        if (reward.IsBadge && !updatedReward.IsClaimed && claimedBenefitIds.Contains(reward.DropInstanceId))
                        {
                            updatedReward = updatedReward with
                            {
                                IsClaimed = true,
                                ProgressMinutes = reward.RequiredMinutes
                            };
                        }

                        updatedRewardsForThisCampaign.Add(updatedReward);
                    }

                    // Create new campaign with updated rewards
                    DropsCampaign updatedCampaign = dropCampaign with
                    {
                        Rewards = updatedRewardsForThisCampaign.AsReadOnly()
                    };

                    updatedResult.Add(updatedCampaign);
                }

                updatedResult.AddRange(rewardCampaigns);

                // Return the new list
                AppLogger.Debug("TwitchDrops", $"Active campaigns fetched successfully. count={updatedResult.Count} (rewardCampaigns={rewardCampaigns.Count})");
                return updatedResult.AsReadOnly();
            }
            catch (Exception ex)
            {
                AppLogger.Error("TwitchDrops", "Fetching active campaigns failed.", ex);
                return [];
            }
        }

        // Repeat counts per reward group id, learned once per session; they do not change while a campaign runs.
        private readonly Dictionary<string, int> _repeatTimesByGroupId = [];
        private bool _repeatRulesAttempted;

        /// <summary>
        /// Builds the viewer's open reward campaigns from the dashboard's <c>rewardCampaignsAvailableToUser</c> and the
        /// <c>ViewerRewardDropInventory</c> progress. Only watch-time reward groups are listed; groups that need a paid
        /// subscription can't be earned by watching and are left out. Failures return an empty list.
        /// </summary>
        private async Task<List<DropsCampaign>> GetRewardCampaignsAsync(JsonObject dashboardResponse, CancellationToken ct)
        {
            try
            {
                JsonArray? available = dashboardResponse["data"]?["rewardCampaignsAvailableToUser"]?.AsArray();
                if (available == null || available.Count == 0)
                    return [];

                JsonObject? progress = await gql.QueryRewardCampaignProgressAsync(ct);
                JsonArray inProgress = progress?["data"]?["currentUser"]?["inventory"]?["viewerRewardDropCampaignsInProgress"]?.AsArray() ?? [];

                await LearnRepeatRulesAsync(available, inProgress, ct);

                List<DropsCampaign> result = [];
                foreach (JsonObject campaign in available.OfType<JsonObject>())
                {
                    DropsCampaign? parsed = ParseRewardCampaign(
                        campaign,
                        inProgress.OfType<JsonObject>().FirstOrDefault(c => c["id"]?.GetValue<string>() == campaign["id"]?.GetValue<string>()),
                        _repeatTimesByGroupId);

                    if (parsed != null)
                        result.Add(parsed);
                }

                AppLogger.Debug("TwitchDrops", $"Reward campaigns: available={available.Count}, listed={result.Count}, inProgress={inProgress.Count}");
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warn("TwitchDrops", $"Reading reward campaigns failed; continuing without them. {ex.Message}");
                return [];
            }
        }

        /// <summary>
        /// A campaign the viewer has not started carries no repeat rules (a "20 minutes on 3 days" reward would look like
        /// a plain 20 minute one), so Twitch's own rules lookup is replayed once per session when a watch-time group lacks them.
        /// </summary>
        private async Task LearnRepeatRulesAsync(JsonArray available, JsonArray inProgress, CancellationToken ct)
        {
            if (_repeatRulesAttempted)
                return;

            HashSet<string?> startedCampaignIds = inProgress.OfType<JsonObject>().Select(c => c["id"]?.GetValue<string>()).ToHashSet();
            bool needsRules = available.OfType<JsonObject>().Any(c =>
                !startedCampaignIds.Contains(c["id"]?.GetValue<string>()) &&
                (c["rewardGroups"]?.AsArray() ?? []).OfType<JsonObject>().Any(g =>
                    (g["unlockRequirements"]?["minuteWatchedGoal"]?.GetValue<int>() ?? 0) > 0 &&
                    (g["unlockRequirements"]?["subsGoal"]?.GetValue<int>() ?? 0) == 0));

            if (!needsRules)
                return;

            _repeatRulesAttempted = true;
            JsonObject? rules = await gql.QueryRewardCampaignRulesAsync(ct);
            if (rules == null)
                return;

            foreach (JsonObject campaign in rules.Select(p => p.Value).OfType<JsonObject>())
            {
                foreach (JsonObject group in (campaign["rewardGroups"]?.AsArray() ?? []).OfType<JsonObject>())
                {
                    string? groupId = group["id"]?.GetValue<string>();
                    int times = group["progressCriteria"]?["repeatableConfig"]?["repeatableTimes"]?.GetValue<int>() ?? 0;
                    if (groupId != null && times > 1)
                        _repeatTimesByGroupId[groupId] = times;
                }
            }

            AppLogger.Debug("TwitchDrops", $"Learned repeat rules for {_repeatTimesByGroupId.Count} reward group(s).");
        }

        private static DropsCampaign? ParseRewardCampaign(JsonObject campaign, JsonObject? progress, IReadOnlyDictionary<string, int> learnedRepeatTimes)
        {
            string id = campaign["id"]?.GetValue<string>() ?? "";
            string name = campaign["name"]?.GetValue<string>() ?? "Reward campaign";

            if (!DateTimeOffset.TryParse(campaign["startsAt"]?.GetValue<string>(), out DateTimeOffset startsAt) ||
                !DateTimeOffset.TryParse(campaign["endsAt"]?.GetValue<string>(), out DateTimeOffset endsAt))
                return null;

            // The progress response carries each group's viewer state and, once started, its repeat rules.
            Dictionary<string, JsonObject> progressGroups = (progress?["rewardGroups"]?.AsArray() ?? [])
                .OfType<JsonObject>()
                .Where(g => g["id"] != null)
                .ToDictionary(g => g["id"]!.GetValue<string>(), g => g);

            List<DropsReward> rewards = [];
            foreach (JsonObject group in (campaign["rewardGroups"]?.AsArray() ?? []).OfType<JsonObject>())
            {
                JsonObject? requirements = group["unlockRequirements"]?.AsObject();
                int minutes = requirements?["minuteWatchedGoal"]?.GetValue<int>() ?? 0;
                int subs = requirements?["subsGoal"]?.GetValue<int>() ?? 0;
                int turboSubs = requirements?["turboSubsGoal"]?.GetValue<int>() ?? 0;
                int events = requirements?["eventTriggerGoal"]?.GetValue<int>() ?? 0;

                // Only plain watch-time groups can be earned by watching.
                if (minutes <= 0 || subs > 0 || turboSubs > 0 || events > 0)
                    continue;

                string groupId = group["id"]?.GetValue<string>() ?? "";
                progressGroups.TryGetValue(groupId, out JsonObject? progressGroup);
                JsonObject? self = progressGroup?["self"]?.AsObject();
                JsonObject? repeat = progressGroup?["progressCriteria"]?["repeatableConfig"]?.AsObject();

                JsonObject? reward = group["rewards"]?.AsArray().OfType<JsonObject>().FirstOrDefault();
                string rewardName = reward?["name"]?.GetValue<string>() ?? name;
                int repeatTimes = repeat?["repeatableTimes"]?.GetValue<int>() ?? learnedRepeatTimes.GetValueOrDefault(groupId);
                if (repeatTimes > 1)
                    rewardName += $" (x{repeatTimes} days)";

                bool claimed = self?["status"]?.GetValue<string>() == "CLAIMED";
                int watched = self?["currentMinutesWatched"]?.GetValue<int>() ?? 0;

                rewards.Add(new DropsReward(
                    Id: groupId,
                    Name: rewardName,
                    ImageUrl: reward?["thumbnailImage"]?["image1xURL"]?.GetValue<string>() ?? reward?["bannerImage"]?["image1xURL"]?.GetValue<string>(),
                    RequiredMinutes: minutes,
                    ProgressMinutes: claimed ? minutes : Math.Min(watched, minutes),
                    IsClaimed: claimed));
            }

            if (rewards.Count == 0)
            {
                AppLogger.Debug("TwitchDrops", $"Skipped reward campaign '{name}' ({id}): no watch-time rewards.");
                return null;
            }

            JsonObject? game = campaign["game"]?.AsObject();
            string brand = campaign["brand"]?.GetValue<string>() ?? "";

            return new DropsCampaign(
                Id: id,
                Name: name,
                Slug: game?["slug"]?.GetValue<string>() ?? "",
                GameId: game?["id"]?.GetValue<string>(),
                GameName: game?["displayName"]?.GetValue<string>() ?? (string.IsNullOrWhiteSpace(brand) ? "Twitch" : brand),
                GameImageUrl: campaign["image"]?["image1xURL"]?.GetValue<string>(),
                StartsAt: startsAt,
                EndsAt: endsAt,
                Rewards: rewards.AsReadOnly(),
                Platform: Platform.Twitch,
                ConnectUrls: [],
                IsGeneralDrop: false,
                IsRewardCampaign: true);
        }

        /// <summary>
        /// True when the campaign's account link points at twitch.tv itself, meaning no external game account is needed.
        /// </summary>
        private static bool LinksToTwitchItself(JsonObject campaign)
        {
            string? linkUrl = campaign["accountLinkURL"]?.GetValue<string>();

            // Only the bare site counts; subdomains such as id.twitch.tv are OAuth sign-ins for a game's own site.
            return Uri.TryCreate(linkUrl, UriKind.Absolute, out Uri? uri) &&
                   (uri.Host.Equals("twitch.tv", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host.Equals("www.twitch.tv", StringComparison.OrdinalIgnoreCase)) &&
                   uri.AbsolutePath.Trim('/').Length == 0;
        }

        /// <summary>
        /// Collects every game the dashboard response mentions, wherever it sits (drop campaigns, reward campaigns,
        /// benefits), so games Twitch only lists outside <c>dropCampaigns</c> are still offered in the game filter.
        /// </summary>
        private static List<(string Slug, string Name)> CollectKnownGames(JsonNode? dashboard)
        {
            List<(string Slug, string Name)> games = [];
            CollectGames(dashboard, games);
            return games;
        }

        private static void CollectGames(JsonNode? node, List<(string Slug, string Name)> games)
        {
            switch (node)
            {
                case JsonObject obj:
                    if (obj["__typename"]?.GetValue<string>() == "Game")
                        AddGame(obj, games);

                    foreach (KeyValuePair<string, JsonNode?> property in obj)
                    {
                        if (property.Key == "game" && property.Value is JsonObject game)
                            AddGame(game, games);

                        CollectGames(property.Value, games);
                    }
                    break;

                case JsonArray array:
                    foreach (JsonNode? item in array)
                        CollectGames(item, games);
                    break;
            }
        }

        private static void AddGame(JsonObject game, List<(string Slug, string Name)> games)
        {
            string? name = game["displayName"]?.GetValue<string>() ?? game["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
                return;

            string slug = game["slug"]?.GetValue<string>() ?? Core.Services.Twitch.Helix.TwitchGameSlugHelper.Slugify(name);
            if (!string.IsNullOrWhiteSpace(slug))
                games.Add((slug.Trim().ToLowerInvariant(), name));
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
        private static DropsCampaign? ParseCampaignFromDetails(JsonObject detailedData)
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

            bool isGeneralDrop = false;
            string slug = game?["slug"]?.GetValue<string>() ?? "Unknown Game";
            string? gameId = game?["id"]?.GetValue<string>();

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
            else
            {
                connectUrls.Add($"https://www.twitch.tv/directory/category/{slug}?filter=drops&sort=VIEWER_COUNT");
                isGeneralDrop = true;
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

                        if (requiredMinutes > 0)
                            rewards.Add(new DropsReward(
                                Id: dropId,
                                Name: rewardName,
                                ImageUrl: rewardImage,
                                RequiredMinutes: requiredMinutes,
                                ProgressMinutes: currentMinutes,
                                IsClaimed: isClaimed,
                                DropInstanceId: benefitId,
                                IsBadge: benefit["distributionType"]?.GetValue<string>() == "BADGE",
                                IsEmote: benefit["distributionType"]?.GetValue<string>() == "EMOTE"
                            ));
                    }
                }
            }

            DropsCampaign dropCampaign = new DropsCampaign(
                Id: id,
                Name: name,
                Slug: slug,
                GameId: gameId,
                GameName: gameName,
                GameImageUrl: gameImage,
                StartsAt: startsAt,
                EndsAt: endsAt,
                Rewards: rewards.AsReadOnly(),
                Platform: Platform.Twitch,
                ConnectUrls: connectUrls,
                IsGeneralDrop: isGeneralDrop
            );

            // Return null if no rewards
            if (dropCampaign.Rewards.Count == 0)
            {
                AppLogger.Debug("TwitchDrops", $"Skipped '{name}' ({id}): no rewards with required minutes. drops={timeBasedDrops?.Count ?? 0}");
                return null;
            }

            return dropCampaign;
        }
    }
}