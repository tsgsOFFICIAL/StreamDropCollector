using System.Text.Json.Nodes;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
using Core.Interfaces;
using System.Net;

namespace Core.Services
{
    public sealed class TwitchGqlService : IGqlService
    {
        private readonly IWebViewHost _host;
        private readonly HttpClient _httpClient;

        private string? _clientId;
        private string? _integrityToken;
        private string? _deviceId;
        private string? _accessToken;

        public TwitchGqlService(IWebViewHost host, HttpClient? httpClient = null)
        {
            _host = host;
            _httpClient = httpClient ?? new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36"
            );
        }
        /// <summary>
        /// Asynchronously refreshes the required HTTP headers by navigating to the Twitch campaigns page and capturing
        /// the latest values.
        /// </summary>
        /// <remarks>This method updates internal header values used for authenticated requests to Twitch
        /// services. It should be called whenever header values may have changed or need to be refreshed.</remarks>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the Client-Integrity token cannot be captured from the HTTP headers.</exception>
        private async Task RefreshHeadersAsync(CancellationToken ct = default)
        {
            const int maxAttempts = 10;
            const int baseDelayMs = 5 * 1000; // Start with 5s delay on retry

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Debug.WriteLine($"[RefreshHeaders] Attempt {attempt}/{maxAttempts} – Navigating to drops/campaigns");

                    // Fresh navigation every attempt (important for clean integrity token)
                    await _host.NavigateAsync($"https://www.twitch.tv/drops/campaigns?t={DateTimeOffset.Now.ToUnixTimeMilliseconds()}");

                    // Give page time to load and fire GQL requests
                    await Task.Delay(2000, ct);

                    // Parallel capture tasks
                    Task<string> clientIdTask = _host.CaptureRequestHeaderAsync("Client-ID", "gql.twitch.tv", 10000, ct);
                    Task<string> integrityTask = _host.CaptureRequestHeaderAsync("Client-Integrity", "gql.twitch.tv", 10000, ct);
                    Task<string> deviceIdTask = _host.CaptureRequestHeaderAsync("X-Device-Id", "gql.twitch.tv", 10000, ct);
                    Task<string> authTokenTask = GetAuthTokenFromCookieAsync();

                    string[] results = await Task.WhenAll(clientIdTask, integrityTask, deviceIdTask, authTokenTask);

                    string clientId = results[0];
                    string integrityToken = results[1];
                    string deviceId = results[2];
                    string accessToken = results[3];

                    // Basic validation
                    if (string.IsNullOrEmpty(integrityToken))
                    {
                        throw new InvalidOperationException("Captured Client-Integrity token was null or empty");
                    }

                    if (string.IsNullOrEmpty(clientId))
                    {
                        throw new InvalidOperationException("Captured Client-ID was null or empty");
                    }

                    // Success! Assign and exit
                    _clientId = clientId;
                    _integrityToken = integrityToken;
                    _deviceId = deviceId ?? _deviceId; // Device-ID is optional – keep old if missing
                    _accessToken = accessToken;

                    Debug.WriteLine($"[RefreshHeaders] Success on attempt {attempt} – Got fresh headers");
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    Debug.WriteLine($"[RefreshHeaders] Attempt {attempt} failed: {ex.Message}. Retrying in {baseDelayMs * attempt}ms...");

                    // Exponential backoff: 5s -> 10s -> 15s
                    await Task.Delay(baseDelayMs * attempt, ct);
                }
            }

            // All attempts failed
            throw new InvalidOperationException($"Failed to refresh headers after {maxAttempts} attempts. Last capture likely poisoned or page didn't trigger GQL requests.");
        }
        /// <summary>
        /// Retrieves the Twitch authentication token from the browser cookie asynchronously.
        /// </summary>
        /// <returns>A string containing the OAuth authentication token in the format "OAuth {token}". The token is returned in
        /// lowercase.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the "auth-token" cookie is not found for the Twitch domain.</exception>
        private async Task<string> GetAuthTokenFromCookieAsync()
        {
            string? token = await _host.GetCookieValueAsync("https://twitch.tv", "auth-token");
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("auth-token cookie not found");

            return "OAuth " + token.ToLower();
        }
        /// <summary>
        /// Asynchronously retrieves the SHA-256 hash of a persisted GraphQL query for the specified operation name.
        /// </summary>
        /// <param name="operationName">The name of the GraphQL operation for which to retrieve the persisted query hash. Cannot be null or empty.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the SHA-256 hash of the
        /// persisted query associated with the specified operation name.</returns>
        /// <exception cref="InvalidOperationException">Thrown if a persisted query hash cannot be found for the specified operation name.</exception>
        private async Task<string> GetPersistedQueryHashAsync(string operationName, CancellationToken ct = default, string? urlOverride = null)
        {
            if (!string.IsNullOrEmpty(urlOverride))
                await _host.NavigateAsync($"{urlOverride}?t={DateTimeOffset.Now.ToUnixTimeMilliseconds()}");
            else
                await _host.NavigateAsync($"https://www.twitch.tv/drops/campaigns?t={DateTimeOffset.Now.ToUnixTimeMilliseconds()}");

            string payload = await _host.CaptureGqlRequestBodyContainingAsyncWithRetry(operationName, 5000, 10, ct: ct);

            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            IEnumerable<JsonElement> operations = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray()
                : Enumerable.Repeat(root, 1);

            foreach (JsonElement operation in operations)
            {
                if (!operation.TryGetProperty("operationName", out JsonElement opNameElement) ||
                    opNameElement.GetString() != operationName)
                    continue;

                if (operation.TryGetProperty("extensions", out JsonElement extensions) &&
                    extensions.TryGetProperty("persistedQuery", out JsonElement persistedQuery) &&
                    persistedQuery.TryGetProperty("sha256Hash", out JsonElement hashElement))
                {
                    return hashElement.GetString()!;
                }
            }

            throw new InvalidOperationException($"Persisted query hash not found for operation: {operationName}");
        }
        /// <summary>
        /// Sends an asynchronous GraphQL query or batch request to the Twitch API and returns the parsed JSON response.
        /// </summary>
        /// <remarks>If the response contains errors or an unsuccessful status code, the method will
        /// automatically refresh authentication headers and retry the request once. The caller is responsible for
        /// interpreting the returned JSON structure according to the expected GraphQL response format.</remarks>
        /// <param name="payload">The query payload to send. This can be an operation name string, a tuple of operation name and variables, a
        /// pre-built JsonObject for a single request, or a JsonArray for batched requests.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the request.</param>
        /// <returns>A JsonNode containing the parsed response from the Twitch GraphQL API. Returns a JsonObject for single
        /// requests or a JsonArray for batched requests.</returns>
        /// <exception cref="ArgumentException">Thrown if the payload is not a supported type: string, (string, object) tuple, JsonObject, or JsonArray.</exception>
        public async Task<JsonNode> QueryAsync(object payload, CancellationToken ct = default)
        {
            if (_clientId == null || _integrityToken == null)
                await RefreshHeadersAsync(ct);

            JsonNode payloadNode;

            if (payload is string operationName)
            {
                object variables = new { };

                if (payload is ValueTuple<string, object> tuple)
                {
                    operationName = tuple.Item1;
                    variables = tuple.Item2 ?? new { };
                }

                string hash = await GetPersistedQueryHashAsync(operationName, ct);

                payloadNode = new JsonObject
                {
                    ["operationName"] = operationName,
                    ["variables"] = JsonSerializer.SerializeToNode(variables),
                    ["extensions"] = new JsonObject
                    {
                        ["persistedQuery"] = new JsonObject
                        {
                            ["version"] = 1,
                            ["sha256Hash"] = hash
                        }
                    }
                };
            }
            else if (payload is JsonArray arrayPayload)
            {
                string json = arrayPayload.ToJsonString();
                payloadNode = JsonNode.Parse(json)!;
            }
            else if (payload is JsonObject singlePayload)
            {
                string json = singlePayload.ToJsonString();
                payloadNode = JsonNode.Parse(json)!;
            }
            else
            {
                throw new ArgumentException("Payload must be operationName string, (string, object) tuple, JsonObject, or JsonArray");
            }

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
            {
                Content = JsonContent.Create(payloadNode)
            };

            request.Headers.TryAddWithoutValidation("Client-ID", _clientId);
            request.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
            request.Headers.TryAddWithoutValidation("Authorization", _accessToken);

            if (!string.IsNullOrEmpty(_deviceId))
                request.Headers.TryAddWithoutValidation("X-Device-Id", _deviceId);

            HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
            string jsonText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode || jsonText.Contains("\"errors\""))
            {
                await RefreshHeadersAsync(ct);

                request.Headers.Remove("Client-Integrity");
                request.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
                response = await _httpClient.SendAsync(request, ct);
                jsonText = await response.Content.ReadAsStringAsync(ct);
            }

            response.EnsureSuccessStatusCode();

            JsonNode? result = JsonNode.Parse(jsonText);

            if (payloadNode is JsonArray)
                return result!.AsArray();

            return result!.AsObject();
        }
        /// <summary>
        /// Queries the Twitch Drops dashboard and returns the full dashboard data as a JSON object.
        /// </summary>
        /// <remarks>This method performs multiple GraphQL queries to retrieve both inventory and
        /// dashboard information in a single request. The returned JSON object corresponds to the
        /// 'ViewerDropsDashboard' response. If authentication headers are invalid or expired, the method automatically
        /// refreshes them and retries the request.</remarks>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A JSON object containing the data from the Twitch Drops dashboard. The object includes information about the
        /// user's drops campaigns and inventory.</returns>
        public async Task<JsonArray> QueryFullDropsDashboardAsync(CancellationToken ct = default)
        {
            if (_clientId == null || _integrityToken == null)
                await RefreshHeadersAsync(ct);

            // Fetch hashes
            string dashboardHash = await GetPersistedQueryHashAsync("ViewerDropsDashboard", ct);
            string inventoryHash = await GetPersistedQueryHashAsync("Inventory", ct, "https://www.twitch.tv/drops/inventory");

            JsonArray payload = new JsonArray
            {
                new JsonObject
                {
                    ["operationName"] = "Inventory",
                    ["variables"] = new JsonObject { ["fetchRewardCampaigns"] = true },
                    ["extensions"] = new JsonObject
                    {
                        ["persistedQuery"] = new JsonObject
                        {
                            ["version"] = 1,
                            ["sha256Hash"] = inventoryHash
                        }
                    }
                },
                new JsonObject
                {
                    ["operationName"] = "ViewerDropsDashboard",
                    ["variables"] = new JsonObject { ["fetchRewardCampaigns"] = true },
                    ["extensions"] = new JsonObject
                    {
                        ["persistedQuery"] = new JsonObject
                        {
                            ["version"] = 1,
                            ["sha256Hash"] = dashboardHash
                        }
                    }
                }
            };

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
            {
                Content = JsonContent.Create(payload)
            };

            request.Headers.TryAddWithoutValidation("Client-ID", _clientId);
            request.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
            request.Headers.TryAddWithoutValidation("Authorization", _accessToken);

            if (!string.IsNullOrEmpty(_deviceId))
                request.Headers.TryAddWithoutValidation("X-Device-Id", _deviceId);

            HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
            string jsonText = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode || jsonText.Contains("\"errors\""))
            {
                await RefreshHeadersAsync(ct);

                using HttpRequestMessage newRequest = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
                {
                    Content = JsonContent.Create(payload)
                };

                newRequest.Headers.TryAddWithoutValidation("Client-ID", _clientId);
                newRequest.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
                newRequest.Headers.TryAddWithoutValidation("Authorization", _accessToken);

                if (!string.IsNullOrEmpty(_deviceId))
                    newRequest.Headers.TryAddWithoutValidation("X-Device-Id", _deviceId);

                response = await _httpClient.SendAsync(newRequest, ct);
                jsonText = await response.Content.ReadAsStringAsync(ct);

                if (jsonText.Contains("\"errors\""))
                    throw new InvalidOperationException("Failed integrity, please wait a while and try again.");
            }

            response.EnsureSuccessStatusCode();

            JsonArray responseArray = JsonNode.Parse(jsonText)!.AsArray();
            return responseArray;
        }
        /// <summary>
        /// Retrieves detailed information for multiple Twitch drop campaigns in a single batch operation.
        /// </summary>
        /// <remarks>The method processes requests in batches to optimize network usage. If authentication
        /// headers are invalid or expired, they are refreshed automatically. The returned dictionary may contain fewer
        /// entries than requested if some campaigns are not found or accessible.</remarks>
        /// <param name="requests">A read-only list of tuples, each containing a drop campaign ID and the associated channel login for which to
        /// retrieve details.</param>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains a dictionary mapping each drop
        /// campaign ID to its corresponding details as a JsonObject. If a campaign is not found, it will not be
        /// included in the dictionary.</returns>
        public async Task<Dictionary<string, JsonObject>> QueryDropCampaignDetailsBatchAsync(IReadOnlyList<(string dropID, string channelLogin)> requests, CancellationToken ct = default)
        {
            if (_clientId == null || _integrityToken == null)
                await RefreshHeadersAsync(ct);

            // 1. Get the REAL current hash
            string liveHash = await GetCurrentDropCampaignDetailsHashAsync(ct);

            Dictionary<string, JsonObject> results = new Dictionary<string, JsonObject>();
            const int batchSize = 20;

            for (int i = 0; i < requests.Count; i += batchSize)
            {
                List<(string dropID, string channelLogin)> batch = requests.Skip(i).Take(batchSize).ToList();

                JsonArray payload = new();

                foreach ((string? dropID, string? channelLogin) in batch)
                {
                    payload.Add(new JsonObject
                    {
                        ["operationName"] = "DropCampaignDetails",
                        ["variables"] = new JsonObject
                        {
                            ["dropID"] = dropID,
                            ["channelLogin"] = channelLogin
                        },
                        ["extensions"] = new JsonObject
                        {
                            ["persistedQuery"] = new JsonObject
                            {
                                ["version"] = 1,
                                ["sha256Hash"] = liveHash
                            }
                        }
                    });
                }

                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://gql.twitch.tv/gql")
                {
                    Content = JsonContent.Create(payload)
                };

                request.Headers.TryAddWithoutValidation("Client-ID", _clientId);
                request.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
                request.Headers.TryAddWithoutValidation("Authorization", _accessToken);
                if (!string.IsNullOrEmpty(_deviceId))
                    request.Headers.TryAddWithoutValidation("X-Device-Id", _deviceId);

                HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
                string jsonText = await response.Content.ReadAsStringAsync(ct);

                // print the payload as json text for debugging
                Debug.WriteLine(request.Content != null
                    ? await request.Content.ReadAsStringAsync(ct)
                    : "No request content");

                // Auto-retry on integrity fail
                if (!response.IsSuccessStatusCode || jsonText.Contains("\"errors\""))
                {
                    await RefreshHeadersAsync(ct);
                    request.Headers.Remove("Client-Integrity");
                    request.Headers.TryAddWithoutValidation("Client-Integrity", _integrityToken);
                    response = await _httpClient.SendAsync(request, ct);
                    jsonText = await response.Content.ReadAsStringAsync(ct);
                }

                response.EnsureSuccessStatusCode();
                JsonArray batchResponse = JsonNode.Parse(jsonText)!.AsArray();

                // Map responses back to dropID
                for (int j = 0; j < batch.Count; j++)
                {
                    JsonObject? data = batchResponse[j]?["data"]?["user"]?["dropCampaign"]?.AsObject();
                    if (data != null)
                        results[batch[j].dropID] = data;
                }
            }

            Debug.WriteLine($"[GQL] Fetched {results.Count} campaigns with full details");
            return results;
        }
        /// <summary>
        /// Asynchronously retrieves the SHA-256 hash of the current Twitch Drop campaign details by simulating user
        /// interaction with the Twitch Drops campaigns page.
        /// </summary>
        /// <remarks>This method navigates to the Twitch Drops campaigns page, triggers the loading of
        /// campaign details, and captures the associated GraphQL request to extract the hash. The operation may take
        /// several seconds to complete due to required page loading and interaction delays.</remarks>
        /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>A string containing the SHA-256 hash of the current DropCampaignDetails GraphQL request.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the DropCampaignDetails hash cannot be found on the page.</exception>
        public async Task<string> GetCurrentDropCampaignDetailsHashAsync(CancellationToken ct = default)
        {
            // 1. Go to drops page
            await _host.NavigateAsync($"https://www.twitch.tv/drops/campaigns?t={DateTimeOffset.Now.ToUnixTimeMilliseconds()}");

            string clickScript = @"
                (async () => {
                    // Wait a bit more for React to render
                    await new Promise(r => setTimeout(r, 3000));

                    // Find all campaign containers
                    const parentContainer = document.querySelector("".Layout-sc-1xcs6mc-0.gJZZlL.drops-root__content"")
                    const containers = parentContainer.children[4].querySelectorAll('.Layout-sc-1xcs6mc-0.iSIERH');
            
                    if (containers.length === 0) {
                        console.log('No campaign container not found');
                        return;
                    }

                    // Find the first <button> inside the first container and click it
                    const firstButton = containers[0].querySelector('button');
                    if (firstButton) {
                        console.log('Clicking first campaign button');
                        firstButton.click();
                    } else {
                        console.log('No button found in first container');
                    }
                })();
            ";

            // 3. Capture the real payload
            string payloadJson = await _host.CaptureGqlRequestBodyContainingAsyncWithRetry("DropCampaignDetails", 5000, 10, clickScript, ct);

            // 4. Parse just the hash
            JsonArray payload = JsonNode.Parse(payloadJson)!.AsArray();

            string? hash = payload
                .OfType<JsonObject>()
                .Where(op => op["operationName"]?.GetValue<string>() == "DropCampaignDetails")
                .Select(op => op["extensions"]?["persistedQuery"]?["sha256Hash"]?.GetValue<string>())
                .FirstOrDefault(h => h != null);

            if (string.IsNullOrEmpty(hash))
                throw new InvalidOperationException("DropCampaignDetails hash not found — try again");

            Debug.WriteLine($"[GQL] Live DropCampaignDetails hash captured: {hash}");
            return hash!;
        }

        public void Dispose() => _httpClient.Dispose();
    }
}