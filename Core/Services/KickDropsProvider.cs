using System.Text.Json;
using Core.Interfaces;
using Core.Enums;
using Models;

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
        /// Asynchronously retrieves a list of currently active Drops campaigns from the Kick platform.
        /// </summary>
        /// <remarks>This method navigates the provided web view host to the Kick Drops campaigns page and
        /// extracts campaign data by executing JavaScript. Only campaigns with a status of "active" are included in the
        /// result.</remarks>
        /// <param name="host">The web view host used to navigate and execute scripts against the Kick Drops campaigns page. Must be
        /// initialized before calling this method.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A read-only list of active Drops campaigns. Returns an empty list if no active campaigns are found.</returns>
        public override async Task<IReadOnlyList<DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken ct = default)
        {
            await host.EnsureInitializedAsync();
            await host.NavigateAsync("https://web.kick.com/api/v1/drops/campaigns");

            // Grab the json from <body><pre>

            string rawJson = await host.ExecuteScriptAsync("document.body.innerText");
            string json = rawJson.Trim('"').Replace("\\n", "").Replace("\\\"", "\"");

            if (json == "null" || string.IsNullOrWhiteSpace(json))
                return [];

            JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out JsonElement dataArray) ||
                dataArray.ValueKind != JsonValueKind.Array)
                return [];

            List<DropsCampaign> campaigns = new List<DropsCampaign>();

            foreach (JsonElement camp in dataArray.EnumerateArray())
            {
                if (camp.GetProperty("status").GetString() != "active")
                    continue;

                JsonElement category = camp.GetProperty("category");

                List<DropsReward> rewards = [.. camp.GetProperty("rewards")
                    .EnumerateArray()
                    .Select(r => new DropsReward(
                        Id: r.GetProperty("id").GetString()!,
                        Name: r.GetProperty("name").GetString()!,
                        ImageUrl: "https://files.kick.com/" + r.GetProperty("image_url").GetString(),
                        RequiredMinutes: r.GetProperty("required_units").GetInt32()
                    ))];

                campaigns.Add(new DropsCampaign(
                    Id: camp.GetProperty("id").GetString()!,
                    Name: camp.GetProperty("name").GetString()!,
                    GameName: category.GetProperty("name").GetString()!,
                    GameImageUrl: category.GetProperty("image_url").GetString(),
                    StartsAt: DateTimeOffset.Parse(camp.GetProperty("starts_at").GetString()!),
                    EndsAt: DateTimeOffset.Parse(camp.GetProperty("ends_at").GetString()!),
                    Rewards: rewards,
                    ConnectUrl: camp.TryGetProperty("connect_url", out JsonElement url) ? url.GetString() : null
                ));
            }

            return campaigns;
        }
    }
}