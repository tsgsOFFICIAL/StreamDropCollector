using System.Text.Json.Serialization;
using System.Security.Authentication;
using System.Diagnostics;
using System.Text.Json;
using System.Net.Http;
using System.Net;
using System.IO;

namespace Core.Services
{
    public partial class GitHubDirectoryDownloaderService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _repositoryOwner;
        private readonly string _repositoryName;
        private readonly string _folderPath;
        private readonly string _basePath;
        private long _totalFileSize = 0;
        private long _downloadedFileSize = 0;
        private object _lockTotalSize = new object();
        private object _lockDownloadedSize = new object();
        private List<Task> _downloadTasks;
        private List<Task> _subfolderTasks;
        private Dictionary<string, string> _fileHashes; // For storing file paths and SHA values
        private readonly string _shaCacheFile;
        private HashSet<string> _currentFiles;
        public event EventHandler<ProgressEventArgs>? ProgressUpdated;

        private readonly SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(25); // Anywhere from 5 to 50 concurrent downloads should be fine.

        /// <summary>
        /// Initializes a new instance of the GitHubDirectoryDownloaderService class for downloading the contents of a specific
        /// directory from a GitHub repository to a local path.
        /// </summary>
        /// <remarks>This constructor configures the downloader to access the specified repository and
        /// directory, and prepares the local environment for file downloads and SHA caching. The downloader uses a
        /// custom HttpClient with a five-minute timeout and a user agent header for GitHub API requests.</remarks>
        /// <param name="repositoryOwner">The owner of the GitHub repository. Cannot be null or empty.</param>
        /// <param name="repositoryName">The name of the GitHub repository. Cannot be null or empty.</param>
        /// <param name="folderPath">The path to the directory within the repository to download. The path should use forward slashes ('/') and
        /// be relative to the repository root.</param>
        /// <param name="basePath">The local base directory where the downloaded files and cache will be stored. Cannot be null or empty.</param>
        public GitHubDirectoryDownloaderService(string repositoryOwner, string repositoryName, string folderPath, string basePath)
        {
            #region HttpClient Settings
            HttpClientHandler httpClientHandler = new HttpClientHandler()
            {
                AllowAutoRedirect = true,
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            };

            //httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) =>
            //{
            //    return true;
            //};

            _httpClient = new HttpClient(httpClientHandler);
            _httpClient.Timeout = new TimeSpan(0, 5, 0);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "GitHubDirectoryDownloaderService");

            AppContext.SetSwitch("System.Net.Http.UseSocketsHttpHandler", true); // default anyway
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", false);

            _httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            _httpClient.DefaultRequestVersion = HttpVersion.Version11;
            #endregion

            _repositoryOwner = repositoryOwner;
            _repositoryName = repositoryName;
            _folderPath = folderPath;
            _downloadTasks = new List<Task>();
            _subfolderTasks = new List<Task>();
            _basePath = basePath;
            _shaCacheFile = Path.Combine(_basePath, "sha_cache.tsgs");
            _currentFiles = new HashSet<string>();
            _fileHashes = LoadSHAHashes() ?? new Dictionary<string, string>(); // Load the SHA cache or initialize a new one
        }

        /// <summary>
        /// Updates the SHA hash cache to include only the specified files and saves the updated cache to disk.
        /// </summary>
        /// <remarks>This method removes any cached SHA hashes for files that are no longer present in the
        /// provided set and persists the updated cache to the configured cache file. Existing cache entries for files
        /// not in the set are discarded.</remarks>
        /// <param name="currentFiles">A set of file paths representing the files to retain in the SHA hash cache. Only hashes for these files will
        /// be preserved.</param>
        private void SaveSHAHashes(HashSet<string> currentFiles)
        {
            // Remove stale entries from the SHA cache
            _fileHashes = _fileHashes
                .Where(kv => currentFiles.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            string cacheJson = JsonSerializer.Serialize(_fileHashes);
            File.WriteAllText(_shaCacheFile, cacheJson);
        }
        /// <summary>
        /// Loads a dictionary of SHA hash values from the cache file if it exists.
        /// </summary>
        /// <returns>A dictionary containing SHA hash values loaded from the cache file; or null if the cache file does not exist
        /// or cannot be deserialized.</returns>
        private Dictionary<string, string>? LoadSHAHashes()
        {
            if (File.Exists(_shaCacheFile))
            {
                string cacheJson = File.ReadAllText(_shaCacheFile);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(cacheJson);
            }

            return null;
        }
        /// <summary>
        /// Asynchronously downloads all files and subdirectories to the specified local directory.
        /// </summary>
        /// <param name="downloadPath">The full path to the local directory where the downloaded files and subdirectories will be saved. Must not
        /// be null or empty.</param>
        /// <returns>A task that represents the asynchronous download operation.</returns>
        public async Task DownloadDirectoryAsync(string downloadPath)
        {
            try
            {
                await StartDownloadAsync(downloadPath);

                Debug.WriteLine("waiting for tasks to complete");
                await Task.WhenAll(_downloadTasks);
                await Task.WhenAll(_subfolderTasks);

                SaveSHAHashes(_currentFiles);
            }
            catch (Exception)
            {
                throw;
            }
        }
        /// <summary>
        /// Asynchronously downloads the contents of a GitHub repository folder to the specified local directory,
        /// retrieving files and subfolders recursively.
        /// </summary>
        /// <remarks>This method processes both files and subdirectories recursively, ensuring that only
        /// changed or new files are downloaded. If the API rate limit is reached, the operation is aborted and progress
        /// is reported with the reset time, if available.</remarks>
        /// <param name="downloadPath">The full path to the local directory where the downloaded files and folders will be saved. If the directory
        /// does not exist, it will be created.</param>
        /// <param name="apiUrl">The GitHub API URL to retrieve the folder contents. If not specified, a default URL is constructed based on
        /// the repository and folder settings.</param>
        /// <returns>A task that represents the asynchronous download operation.</returns>
        /// <exception cref="Exception">Thrown if the GitHub API rate limit is exceeded during the download process.</exception>
        private async Task StartDownloadAsync(string downloadPath, string apiUrl = null!)
        {
            // Construct the API url
            apiUrl ??= $"https://api.github.com/repos/{_repositoryOwner}/{_repositoryName}/contents/{_folderPath}";

            HttpResponseMessage response = await _httpClient.GetAsync(apiUrl);
            string responseMessage = await response.Content.ReadAsStringAsync();

            // Rate limit was reached.
            if (responseMessage.Contains("API rate limit exceeded"))
            {
                // Get the rate limit reset time (epoch seconds)
                string? resetTimeString = response.Headers.GetValues("x-ratelimit-reset").FirstOrDefault();
                DateTime resetLocal = DateTime.Now.AddHours(1);

                if (long.TryParse(resetTimeString, out long resetEpoch))
                {
                    // Convert epoch seconds to DateTime in UTC
                    DateTime resetUtc = DateTimeOffset.FromUnixTimeSeconds(resetEpoch).UtcDateTime;

                    // Convert UTC to local time
                    resetLocal = resetUtc.ToLocalTime();

                    Debug.WriteLine($"Rate limit resets at: {resetLocal}");
                    OnProgressChanged(new ProgressEventArgs(0, $"API rate limit exceeded, try again after {resetLocal:T}"));
                }
                else
                {
                    Debug.WriteLine("Failed to parse x-ratelimit-reset header.");
                    OnProgressChanged(new ProgressEventArgs(0, $"API rate limit exceeded, try again later"));
                }

                Dispose();
                throw new Exception($"API rate limit exceeded\nRate limit resets at: {resetLocal}");
            }

            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                GitHubContent[] contents = JsonSerializer.Deserialize<GitHubContent[]>(json) ?? Array.Empty<GitHubContent>();

                if (!Directory.Exists(downloadPath))
                {
                    Directory.CreateDirectory(downloadPath);
                }

                lock (_lockTotalSize)
                {
                    _totalFileSize += contents.Sum(content => content.Size ?? 0);
                }

                foreach (GitHubContent item in contents)
                {
                    switch (item.Type)
                    {
                        case "file":
                            string downloadUrl = item.DownloadUrl!;
                            string localFilePath = Path.Combine(downloadPath, item.Name!);
                            string oldFilePath = Path.Combine(downloadPath.Replace("\\Update", ""), item.Name!);

                            // Check if file needs to be downloaded
                            string? localSha = _fileHashes.ContainsKey(item.Path!) ? _fileHashes[item.Path!] : null;
                            if (localSha != item.Sha)
                            {
                                lock (_downloadTasks)
                                {
                                    _downloadTasks.Add(DownloadFileAsync(item, localFilePath));
                                }
                            }
                            else
                            {
                                // Move file manually if it's already downloaded, from the base path to the download path
                                Debug.WriteLine($"Skipping unchanged file: {item.Name}");
                                File.Copy(oldFilePath, localFilePath, true);
                                lock (_lockDownloadedSize)
                                {
                                    _downloadedFileSize += (long)item.Size!;
                                    OnProgressChanged(new ProgressEventArgs((int)(((double)_downloadedFileSize / _totalFileSize) * 100)));
                                }
                            }
                            break;
                        case "dir":
                            string subFolderPath = item.Path!;
                            string subfolderDownloadDirectory = Path.Combine(downloadPath, item.Name!);

                            lock (_subfolderTasks)
                            {
                                _subfolderTasks.Add(StartDownloadAsync(subfolderDownloadDirectory, apiUrl.Replace(_folderPath, subFolderPath)));
                            }
                            break;
                    }
                }

                _currentFiles.UnionWith(contents.Where(c => c.Type == "file").Select(c => c.Path!));
            }
            else
            {
                OnProgressChanged(new ProgressEventArgs(0, "Something went wrong"));
                Dispose();
            }
        }
        /// <summary>
        /// Asynchronously downloads a file from the specified GitHub content item and saves it to the given file path.
        /// </summary>
        /// <param name="item">The GitHub content item representing the file to download. Must not be null and must contain a valid
        /// download URL.</param>
        /// <param name="filePath">The full path, including the file name, where the downloaded file will be saved. If the file exists, it will
        /// be overwritten.</param>
        /// <returns>A task that represents the asynchronous download operation.</returns>
        private async Task DownloadFileAsync(GitHubContent item, string filePath)
        {
            Debug.WriteLine($"[QUEUE] Waiting for slot to download: {item.Name}");
            await _downloadSemaphore.WaitAsync(); // Limit concurrent downloads
            try
            {
                Debug.WriteLine($"[START] Downloading file: {item.Name} | Size: {item.Size ?? -1} bytes");

                Stopwatch sw = Stopwatch.StartNew();
                using HttpResponseMessage response = await _httpClient.GetAsync(item.DownloadUrl!);
                sw.Stop();
                Debug.WriteLine($"[HEADERS DONE] {item.Name} in {sw.ElapsedMilliseconds} ms | Status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    using FileStream fileStream = File.Create(filePath);
                    sw.Restart();
                    await response.Content.CopyToAsync(fileStream);
                    sw.Stop();
                    Debug.WriteLine($"[DOWNLOAD DONE] {item.Name} ({fileStream.Length} bytes) in {sw.ElapsedMilliseconds} ms");

                    _fileHashes[item.Path!] = item.Sha!;

                    lock (_lockDownloadedSize)
                    {
                        _downloadedFileSize += fileStream.Length;
                        OnProgressChanged(new ProgressEventArgs((int)(((double)_downloadedFileSize / _totalFileSize) * 100)));
                    }
                }
                else
                {
                    string error = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[FAIL] {item.Name}: {response.StatusCode} | {error}");
                }
            }
            catch (TaskCanceledException tcex)
            {
                Debug.WriteLine($"[TIMEOUT/CANCEL] {item.Name}: {tcex.Message} (IsTimeout: {!tcex.CancellationToken.IsCancellationRequested})");
                // Optional: retry logic here
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ERROR] {item.Name}: {ex}");
            }
            finally
            {
                _downloadSemaphore.Release(); // Always release the slot
                Debug.WriteLine($"[SLOT RELEASED] for {item.Name} | Current count: {_downloadSemaphore.CurrentCount}");
            }
        }
        /// <summary>
        /// Raises the progress changed event to notify subscribers of progress updates.
        /// </summary>
        /// <remarks>Override this method in a derived class to provide custom handling when progress
        /// changes. This method invokes the ProgressUpdated event if there are any subscribers.</remarks>
        /// <param name="e">The event data containing information about the progress update.</param>
        protected virtual void OnProgressChanged(ProgressEventArgs e)
        {
            ProgressUpdated?.Invoke(this, e);
        }
        /// <summary>
        /// Releases all resources used by the current instance of the class.
        /// </summary>
        /// <remarks>Call this method when you are finished using the instance to free unmanaged resources
        /// and perform other cleanup operations. After calling this method, the instance should not be used.</remarks>
        public void Dispose()
        {
            _httpClient.Dispose();
            GC.SuppressFinalize(this);
        }
    }
    /// <summary>
    /// Provides data for events that report progress updates, including the current progress percentage and status
    /// information.
    /// </summary>
    /// <remarks>Use this class with events that need to communicate progress information, such as
    /// long-running operations or background tasks. The progress value typically represents a percentage from 0 to 100,
    /// and the status can be used to convey additional context or state.</remarks>
    public class ProgressEventArgs : EventArgs
    {
        /// <summary>
        /// Gets or sets the current progress value.
        /// </summary>
        public int Progress { get; set; }
        /// <summary>
        /// Gets or sets the current status message.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Initializes a new instance of the ProgressEventArgs class with the specified progress value and status
        /// message.
        /// </summary>
        /// <param name="progress">The current progress value, typically expressed as a percentage. Must be between 0 and 100.</param>
        /// <param name="status">An optional status message that describes the current progress. If not specified, an empty string is used.</param>
        public ProgressEventArgs(int progress, string status = "")
        {
            Progress = progress;
            Status = status;
        }
    }

    /// <summary>
    /// Represents a content item in a GitHub repository, such as a file or directory, as returned by the GitHub API.
    /// </summary>
    /// <remarks>This class provides properties that map to the fields of the GitHub content API response,
    /// including file metadata, URLs for accessing the content, and related links. It can be used to deserialize
    /// responses from endpoints such as the GitHub repository contents API. For more information, see the GitHub REST
    /// API documentation.</remarks>
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

        /// <summary>
        /// Represents the set of URLs associated with a GitHub content resource, including API, Git, and HTML links.
        /// </summary>
        /// <remarks>This class is typically used to deserialize the 'links' object returned by the GitHub
        /// API for content resources. Each property corresponds to a specific type of link provided by GitHub for
        /// accessing the content in different formats or contexts.</remarks>
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
