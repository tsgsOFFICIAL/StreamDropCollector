using Core.Enums;
using Core.Interfaces;
using Microsoft.Web.WebView2.Core;
using Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
        private readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            UseCookies = false
        })
        {
            BaseAddress = new Uri("https://gql.twitch.tv/")
        };

        /// <summary>
        /// Gets the platform associated with this instance.
        /// </summary>
        public override Platform Platform => Platform.Twitch;

        /// <summary>
        /// Asynchronously retrieves a list of currently active Drops campaigns from the specified web view host.
        /// </summary>
        /// <remarks>This method communicates with the web view host to obtain campaign information. The
        /// returned campaigns include only those with an active status. The operation may be canceled by providing a
        /// cancellation token.</remarks>
        /// <param name="host">The web view host used to access and retrieve campaign data.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns>A read-only list of active Drops campaigns. The list is empty if no active campaigns are found.</returns>
        public override async Task<IReadOnlyList<DropsCampaign>> GetActiveCampaignsAsync(IWebViewHost host, CancellationToken ct = default)
        {
            await host.EnsureInitializedAsync();

            // 1. Get auth token from httpOnly cookie (universal)
            string? rawToken = await host.GetCookieValueAsync("https://twitch.tv", "auth-token");
            if (string.IsNullOrEmpty(rawToken))
                return [];

            string authHeader = "OAuth " + rawToken.ToLower();

            // 2. Trigger real traffic
            await host.NavigateAsync("https://twitch.tv/drops/campaigns?t=" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            // 3. Capture LIVE Client-ID using the new universal method
            string clientId = await host.CaptureRequestHeaderAsync(
                headerName: "Client-ID",
                urlContains: "gql.twitch.tv",
                timeoutMs: 5 * 1000,
                ct);

            Debug.WriteLine($"[TwitchDrops] Live Client-ID captured: {clientId}");

            string dashboardHash = await GetViewerDropsDashboardHashAsync(host, ct);

            Debug.WriteLine($"[TwitchDrops] Live ViewerDropsDashboard hash captured: {dashboardHash}");

            // Arm the HttpClient
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Client-ID", clientId);
            _httpClient.DefaultRequestHeaders.Add("Authorization", authHeader);
            _httpClient.DefaultRequestHeaders.Add("X-Device-Id", Guid.NewGuid().ToString("N"));

            // Profit
            return [];// await QueryDropsInventoryAsync(ct);
        }

        private static async Task<string> GetViewerDropsDashboardHashAsync(IWebViewHost host, CancellationToken ct = default)
        {
            await host.NavigateAsync("https://www.twitch.tv/drops/campaigns?" + DateTimeOffset.Now.ToUnixTimeMilliseconds());

            string gqlPayload = await host.CaptureGqlRequestBodyContainingAsync(
                triggerText: "ViewerDropsDashboard",
                timeoutMs: 5 * 1000,
                ct);

            JsonDocument doc = JsonDocument.Parse(gqlPayload);
            string? hash = null;

            foreach (JsonElement operation in doc.RootElement.EnumerateArray())
            {
                if (operation.TryGetProperty("operationName", out JsonElement opName) &&
                    opName.GetString() == "ViewerDropsDashboard" &&
                    operation.TryGetProperty("extensions", out JsonElement extensions) &&
                    extensions.TryGetProperty("persistedQuery", out JsonElement pq) &&
                    pq.TryGetProperty("sha256Hash", out JsonElement hashElement))
                {
                    hash = hashElement.GetString();
                    break;
                }
            }

            if (hash == null)
                throw new InvalidOperationException("ViewerDropsDashboard operation not found or missing hash in payload");

            Debug.WriteLine($"LIVE & CORRECT ViewerDropsDashboard HASH → {hash}");
            return hash;
        }
    }
}