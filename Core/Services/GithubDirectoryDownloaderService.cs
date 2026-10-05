using System.Text.Json.Serialization;
using System.Security.Authentication;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
using System.IO;
using Core.Logging;

namespace Core.Services
{
    /// <summary>
    /// Downloads a folder of a GitHub repository (the published release binaries) with bounded parallelism, per-file
    /// retries and stall detection, and reports byte-accurate progress plus a human-readable log.
    /// </summary>
    /// <remarks>
    /// Any file that cannot be downloaded after all retries fails the whole download, so a flaky connection can never
    /// produce a half-updated installation. Unchanged files (matching the cached SHA) are copied from the installed copy.
    /// </remarks>
    public partial class GitHubDirectoryDownloaderService : IDisposable
    {
        private const int MaxParallelDownloads = 6;
        private const int MaxAttempts = 4;
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

        private readonly HttpClient _httpClient;
        private readonly string _repositoryOwner;
        private readonly string _repositoryName;
        private readonly string _folderPath;
        private readonly string _basePath;
        private readonly string _shaCacheFile;
        private ConcurrentDictionary<string, string> _fileHashes;

        private long _totalBytes;
        private long _doneBytes;
        private long _lastReportTicks;

        /// <summary>Raised when overall download progress changes (throttled).</summary>
        public event EventHandler<ProgressEventArgs>? ProgressUpdated;

        /// <summary>Raised with a human-readable line describing what the downloader is doing.</summary>
        public event EventHandler<string>? LogMessage;

        /// <summary>Creates a downloader for a repository folder.</summary>
        /// <param name="repositoryOwner">GitHub account that owns the repository.</param>
        /// <param name="repositoryName">Repository name.</param>
        /// <param name="folderPath">Folder inside the repository to download.</param>
        /// <param name="basePath">Installation data folder (holds the SHA cache and the currently installed files).</param>
        public GitHubDirectoryDownloaderService(string repositoryOwner, string repositoryName, string folderPath, string basePath)
        {
            HttpClientHandler httpClientHandler = new()
            {
                AllowAutoRedirect = true,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            };

            _httpClient = new HttpClient(httpClientHandler) { Timeout = TimeSpan.FromMinutes(2) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubDirectoryDownloaderService");

            // Use a token if one exists locally (development purposes only) to avoid API rate limits.
            string githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
            if (!string.IsNullOrEmpty(githubToken))
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("token", githubToken);

            _repositoryOwner = repositoryOwner;
            _repositoryName = repositoryName;
            _folderPath = folderPath.Trim('/');
            _basePath = basePath;
            _shaCacheFile = Path.Combine(_basePath, "sha_cache.tsgs");
            _fileHashes = new ConcurrentDictionary<string, string>(LoadShaHashes() ?? new Dictionary<string, string>());
        }

        /// <summary>
        /// Downloads every file of the folder into <paramref name="downloadPath"/>.
        /// </summary>
        /// <param name="downloadPath">Destination folder (created if needed).</param>
        /// <param name="cancellationToken">Cancels the download.</param>
        /// <exception cref="OperationCanceledException">The download was cancelled.</exception>
        /// <exception cref="InvalidOperationException">A file could not be downloaded, or GitHub's rate limit was hit.</exception>
        public async Task DownloadDirectoryAsync(string downloadPath, CancellationToken cancellationToken = default)
        {
            try
            {
                Log("Contacting GitHub for the latest files...");
                List<GitHubContent> files = [];
                await ListFilesAsync($"https://api.github.com/repos/{_repositoryOwner}/{_repositoryName}/contents/{_folderPath}", files, cancellationToken);

                _totalBytes = files.Sum(f => (long)(f.Size ?? 0));
                _doneBytes = 0;
                Log($"Found {files.Count} files ({FormatBytes(_totalBytes)}).");
                Report(force: true);

                Directory.CreateDirectory(downloadPath);

                int skipped = 0;
                int completed = 0;
                Exception? firstFailure = null;
                object failureLock = new();
                using SemaphoreSlim gate = new(MaxParallelDownloads);
                using CancellationTokenSource failFast = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                async Task ProcessAsync(GitHubContent item)
                {
                    await gate.WaitAsync(failFast.Token);
                    try
                    {
                        string relative = item.Path![(_folderPath.Length)..].TrimStart('/');
                        string destination = Path.Combine(downloadPath, relative.Replace('/', Path.DirectorySeparatorChar));
                        string installed = Path.Combine(_basePath, relative.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                        if (_fileHashes.TryGetValue(item.Path!, out string? knownSha) && knownSha == item.Sha && TryCopyInstalled(installed, destination))
                        {
                            Interlocked.Add(ref _doneBytes, item.Size ?? 0);
                            Interlocked.Increment(ref skipped);
                            Report();
                            return;
                        }

                        await DownloadFileWithRetriesAsync(item, destination, failFast.Token);
                        _fileHashes[item.Path!] = item.Sha!;

                        int n = Interlocked.Increment(ref completed);
                        Log($"[{n}] {relative} ({FormatBytes(item.Size ?? 0)})");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        lock (failureLock)
                            firstFailure ??= ex;

                        failFast.Cancel(); // stop the other downloads as soon as one file has permanently failed
                        throw;
                    }
                    finally
                    {
                        gate.Release();
                    }
                }

                try
                {
                    await Task.WhenAll(files.Select(ProcessAsync));
                }
                catch (Exception) when (firstFailure != null)
                {
                    // Siblings of a failed file are cancelled; report the real failure instead of their cancellation.
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (Interlocked.Read(ref _doneBytes) < _totalBytes)
                    throw new InvalidOperationException("The download did not complete. Check your connection and try again.");

                SaveShaHashes(files.Select(f => f.Path!).ToHashSet());
                Log($"Downloaded {completed} file(s), reused {skipped} unchanged file(s).");
                Report(force: true);
            }
            catch (OperationCanceledException)
            {
                Log("Download cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Error("GitHubDownloader", "DownloadDirectoryAsync failed.", ex);
                throw;
            }
        }

        private async Task ListFilesAsync(string apiUrl, List<GitHubContent> files, CancellationToken ct)
        {
            string json = await GetStringWithRetriesAsync(apiUrl, ct);
            GitHubContent[] contents = JsonSerializer.Deserialize<GitHubContent[]>(json) ?? [];

            foreach (GitHubContent item in contents.Where(c => c.Type == "file"))
                files.Add(item);

            foreach (GitHubContent folder in contents.Where(c => c.Type == "dir"))
                await ListFilesAsync(folder.Url!, files, ct);
        }

        private async Task<string> GetStringWithRetriesAsync(string url, CancellationToken ct)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
                    string body = await response.Content.ReadAsStringAsync(ct);

                    if (body.Contains("API rate limit exceeded", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(BuildRateLimitMessage(response));

                    response.EnsureSuccessStatusCode();
                    return body;
                }
                catch (InvalidOperationException)
                {
                    throw; // rate limit: retrying immediately cannot help
                }
                catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested)
                {
                    Log($"Could not list files ({ex.Message}). Retrying ({attempt}/{MaxAttempts - 1})...");
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
            }
        }

        private string BuildRateLimitMessage(HttpResponseMessage response)
        {
            string? resetValue = response.Headers.TryGetValues("x-ratelimit-reset", out IEnumerable<string>? values) ? values.FirstOrDefault() : null;
            string message = long.TryParse(resetValue, out long epoch)
                ? $"GitHub API rate limit exceeded. Try again after {DateTimeOffset.FromUnixTimeSeconds(epoch).LocalDateTime:T}."
                : "GitHub API rate limit exceeded. Try again later.";

            AppLogger.Warn("GitHubDownloader", message);
            return message;
        }

        private async Task DownloadFileWithRetriesAsync(GitHubContent item, string destination, CancellationToken ct)
        {
            string partial = destination + ".part";

            for (int attempt = 1; ; attempt++)
            {
                long countedForFile = 0;

                try
                {
                    using CancellationTokenSource stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    stall.CancelAfter(StallTimeout);

                    using HttpResponseMessage response = await _httpClient.GetAsync(item.DownloadUrl!, HttpCompletionOption.ResponseHeadersRead, stall.Token);
                    response.EnsureSuccessStatusCode();

                    await using Stream source = await response.Content.ReadAsStreamAsync(stall.Token);
                    await using (FileStream target = File.Create(partial))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = await source.ReadAsync(buffer, stall.Token)) > 0)
                        {
                            stall.CancelAfter(StallTimeout); // data is flowing; reset the stall timer
                            await target.WriteAsync(buffer.AsMemory(0, read), stall.Token);
                            countedForFile += read;
                            Interlocked.Add(ref _doneBytes, read);
                            Report();
                        }
                    }

                    File.Move(partial, destination, overwrite: true);
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Interlocked.Add(ref _doneBytes, -countedForFile); // this attempt's bytes are void
                    TryDelete(partial);

                    string reason = ex is OperationCanceledException ? "connection stalled" : ex.Message;
                    if (attempt >= MaxAttempts)
                        throw new InvalidOperationException($"Failed to download {item.Name} after {MaxAttempts} attempts ({reason}).", ex);

                    Log($"{item.Name}: {reason}. Retrying ({attempt}/{MaxAttempts - 1})...");
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                }
            }
        }

        private static bool TryCopyInstalled(string installed, string destination)
        {
            try
            {
                if (!File.Exists(installed))
                    return false;

                File.Copy(installed, destination, true);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        private void Report(bool force = false)
        {
            long now = Stopwatch.GetTimestamp();
            if (!force && now - Interlocked.Read(ref _lastReportTicks) < Stopwatch.Frequency / 10)
                return;

            Interlocked.Exchange(ref _lastReportTicks, now);

            long total = Math.Max(_totalBytes, 1);
            long done = Math.Clamp(Interlocked.Read(ref _doneBytes), 0, total);
            ProgressUpdated?.Invoke(this, new ProgressEventArgs((int)(done * 100 / total), string.Empty, done, _totalBytes));
        }

        private void Log(string message) => LogMessage?.Invoke(this, message);

        /// <summary>Formats a byte count for display (for example "12.3 MB").</summary>
        public static string FormatBytes(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
        }

        private void SaveShaHashes(HashSet<string> currentFiles)
        {
            Dictionary<string, string> kept = _fileHashes
                .Where(kv => currentFiles.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            File.WriteAllText(_shaCacheFile, JsonSerializer.Serialize(kept));
        }

        private Dictionary<string, string>? LoadShaHashes()
        {
            try
            {
                return File.Exists(_shaCacheFile)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_shaCacheFile))
                    : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                AppLogger.Warn("GitHubDownloader", $"Ignoring unreadable SHA cache: {ex.Message}");
                return null;
            }
        }

        /// <summary>Releases the HTTP client.</summary>
        public void Dispose()
        {
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Describes download progress.</summary>
    public class ProgressEventArgs : EventArgs
    {
        /// <summary>Gets or sets the overall percentage (0-100).</summary>
        public int Progress { get; set; }

        /// <summary>Gets or sets an optional status message.</summary>
        public string Status { get; set; }

        /// <summary>Gets or sets the number of bytes downloaded so far.</summary>
        public long DownloadedBytes { get; set; }

        /// <summary>Gets or sets the total number of bytes to download.</summary>
        public long TotalBytes { get; set; }

        /// <summary>Creates progress arguments.</summary>
        public ProgressEventArgs(int progress, string status = "", long downloadedBytes = 0, long totalBytes = 0)
        {
            Progress = progress;
            Status = status;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
        }
    }

    /// <summary>An entry returned by the GitHub contents API.</summary>
    public class GitHubContent
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("path")]
        public string? Path { get; set; }
        [JsonPropertyName("sha")]
        public string? Sha { get; set; }
        [JsonPropertyName("size")]
        public int? Size { get; set; }
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
        [JsonPropertyName("git_url")]
        public string? GitUrl { get; set; }
        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        [JsonPropertyName("_links")]
        public GitHubContentLinks? Links { get; set; }

        public class GitHubContentLinks
        {
            [JsonPropertyName("self")]
            public string? Self { get; set; }
            [JsonPropertyName("git")]
            public string? Git { get; set; }
            [JsonPropertyName("html")]
            public string? Html { get; set; }
        }
    }
}
