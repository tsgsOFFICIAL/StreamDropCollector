using System.Diagnostics;
using Core.Services;
using System.IO;

namespace Core.Managers
{
    public sealed class UpdateManager
    {
        private static readonly Lazy<UpdateManager> _instance = new(() => new UpdateManager());
        public static UpdateManager Instance => _instance.Value;

        private readonly string _repositoryOwner = "tsgsOFFICIAL";
        private readonly string _repositoryName = "StreamDropCollector";
        private readonly string _folderPath = "UI/bin/Release/net10.0-windows10.0.17763.0/publish/win-x64";

        public event EventHandler<ProgressEventArgs>? DownloadProgress;

        private UpdateManager()
        { }

        /// <summary>
        /// Downloads the latest update for the application from the configured GitHub repository and restarts the
        /// application to apply the update.
        /// </summary>
        /// <remarks>This method initiates an asynchronous download of the update files and then restarts
        /// the application upon successful completion. Any errors encountered during the update process are logged for
        /// debugging purposes. This method should typically be called from the UI thread, as it may cause the
        /// application to exit and restart.</remarks>
        public async Task DownloadUpdate()
        {
            string basePath = Path.Combine(Environment.ExpandEnvironmentVariables("%APPDATA%"), "StreamDropCollector");
            string updatePath = Path.Combine(basePath, "Update");

            using GitHubDirectoryDownloaderService downloader = new GitHubDirectoryDownloaderService(_repositoryOwner, _repositoryName, _folderPath, basePath);
            downloader.ProgressUpdated += OnProgressChanged!;

            try
            {
                await downloader.DownloadDirectoryAsync(updatePath);

                Process.Start(Path.Combine(updatePath, "StreamDropCollector"));
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
        /// <summary>
        /// Raises the event that reports progress updates during an operation.
        /// </summary>
        /// <param name="sender">The source of the event. Typically, this is the object that initiated the progress update.</param>
        /// <param name="e">A ProgressEventArgs object that contains the progress data, such as the percentage completed. Must not be
        /// null.</param>
        private void OnProgressChanged(object sender, ProgressEventArgs e)
        {
            DownloadProgress?.Invoke(this, e);
        }
    }
}