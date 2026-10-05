using System.Diagnostics;
using Core.Services;
using Core.Logging;
using System.IO;

namespace Core.Managers
{
    /// <summary>
    /// Downloads application updates from GitHub and restarts the app to apply them.
    /// </summary>
    public sealed class UpdateManager
    {
        private static readonly Lazy<UpdateManager> _instance = new(() => new UpdateManager());

        /// <summary>
        /// Gets the singleton instance of the update manager.
        /// </summary>
        public static UpdateManager Instance => _instance.Value;

        private readonly string _repositoryOwner = "tsgsOFFICIAL";
        private readonly string _repositoryName = "StreamDropCollector";
        private readonly string _folderPath = "UI/bin/Release/net10.0-windows10.0.17763.0/publish/win-x64";
        private const string ExecutableName = "Stream Drop Collector.exe";

        /// <summary>
        /// Occurs when update download progress changes.
        /// </summary>
        public event EventHandler<ProgressEventArgs>? DownloadProgress;

        /// <summary>
        /// Occurs with a human-readable line describing the current update step (for display in a log view).
        /// </summary>
        public event EventHandler<string>? LogMessage;

        /// <summary>
        /// Gets a value indicating whether an update is currently being downloaded.
        /// </summary>
        public bool IsUpdating { get; private set; }

        private UpdateManager()
        { }

        /// <summary>
        /// Downloads the latest update and, when it is complete and verified, restarts the application to apply it.
        /// </summary>
        /// <remarks>Never partially applies an update: if any file cannot be downloaded the installed copy is left untouched.
        /// On success this method does not return, because the process exits to let the updater take over.</remarks>
        /// <param name="cancellationToken">Cancels the download.</param>
        /// <returns><see langword="true"/> when the update was started; <see langword="false"/> when it failed or was cancelled.</returns>
        public async Task<bool> DownloadUpdate(CancellationToken cancellationToken = default)
        {
            if (IsUpdating)
                return false;

            IsUpdating = true;
            string basePath = Path.Combine(Environment.ExpandEnvironmentVariables("%APPDATA%"), "Stream Drop Collector");
            string updatePath = Path.Combine(basePath, "Update");

            try
            {
                Log("Preparing update...");
                Directory.CreateDirectory(basePath);

                // Stale files from an earlier attempt would otherwise be copied over the installation.
                if (Directory.Exists(updatePath))
                    Directory.Delete(updatePath, recursive: true);

                using GitHubDirectoryDownloaderService downloader = new(_repositoryOwner, _repositoryName, _folderPath, basePath);
                downloader.ProgressUpdated += (_, e) => DownloadProgress?.Invoke(this, e);
                downloader.LogMessage += (_, message) => Log(message);

                await downloader.DownloadDirectoryAsync(updatePath, cancellationToken);

                string newExecutable = Path.Combine(updatePath, ExecutableName);
                if (!File.Exists(newExecutable))
                    throw new FileNotFoundException("The downloaded update is incomplete (the application executable is missing).", newExecutable);

                Log("Download complete and verified. Restarting to apply the update...");
                await Task.Delay(1500, CancellationToken.None); // let the user read the final log lines

                Process.Start(newExecutable, "--updating");
                Environment.Exit(0);
                return true;
            }
            catch (OperationCanceledException)
            {
                Log("Update cancelled. Nothing was changed.");
                return false;
            }
            catch (Exception ex)
            {
                AppLogger.Error("UpdateManager", "DownloadUpdate failed.", ex);
                Log($"Update failed: {ex.Message}");
                Log("Your installed version was not modified. You can retry.");
                return false;
            }
            finally
            {
                IsUpdating = false;
            }
        }

        private void Log(string message)
        {
            AppLogger.Info("UpdateManager", message);
            LogMessage?.Invoke(this, message);
        }
    }
}
