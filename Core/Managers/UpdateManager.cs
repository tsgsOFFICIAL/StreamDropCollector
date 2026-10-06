using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.IO;
using Microsoft.Win32;
using Core.Services;
using Core.Logging;

namespace Core.Managers
{
    /// <summary>
    /// Downloads application updates from the latest GitHub release and restarts the app to apply them.
    /// </summary>
    /// <remarks>
    /// Installs made with the Setup.exe (detected through the uninstaller Inno Setup leaves next to the exe, or its
    /// registration in the registry) are updated by silently re-running the new Setup.exe, so Windows' Installed apps
    /// entry stays in sync and the uninstaller is kept. Portable (zip) installs are updated by copying the extracted
    /// files over the running app's folder.
    /// </remarks>
    public sealed class UpdateManager
    {
        private static readonly Lazy<UpdateManager> _instance = new(() => new UpdateManager());

        /// <summary>
        /// Gets the singleton instance of the update manager.
        /// </summary>
        public static UpdateManager Instance => _instance.Value;

        private const string RepositoryOwner = "tsgsOFFICIAL";
        private const string RepositoryName = "StreamDropCollector";
        private const string ExecutableName = "Stream Drop Collector.exe";
        private const string UninstallerName = "unins000.exe";
        private const string UninstallRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Stream Drop Collector_is1";

        private const int MaxAttempts = 4;
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

        private static readonly HttpClient Client = CreateClient();

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
        /// Downloads the latest release and, when it is complete and verified, restarts the application to apply it.
        /// </summary>
        /// <remarks>Never partially applies an update: if the download fails the installed copy is left untouched.
        /// On success this method does not return, because the process exits to let the update take over.</remarks>
        /// <param name="cancellationToken">Cancels the download.</param>
        /// <returns><see langword="true"/> when the update was started; <see langword="false"/> when it failed or was cancelled.</returns>
        public async Task<bool> DownloadUpdate(CancellationToken cancellationToken = default)
        {
            if (IsUpdating)
                return false;

            IsUpdating = true;
            string? downloadPath = null;

            try
            {
                Log("Preparing update...");

                bool installerInstall = IsInstallerInstall();
                Log(installerInstall ? "Installed with Setup. The update will run the new installer." : "Portable install detected. The update will replace the files in place.");

                Log("Contacting GitHub for the latest release...");
                ReleaseAsset asset = await GetLatestReleaseAssetAsync(installerInstall, cancellationToken);
                Log($"Found {asset.Name} ({asset.Version}).");

                downloadPath = Path.Combine(Path.GetTempPath(), asset.Name);
                await DownloadFileAsync(asset, downloadPath, cancellationToken);

                if (installerInstall)
                {
                    Log("Download complete. Restarting to run the installer...");
                    await Task.Delay(1500, CancellationToken.None); // let the user read the final log lines
                    ApplyInstallerUpdate(asset, downloadPath);
                }
                else
                {
                    Log("Download complete. Verifying and extracting...");
                    string stagingPath = ExtractPortableUpdate(asset, downloadPath);
                    Log("Update verified. Restarting to apply the update...");
                    await Task.Delay(1500, CancellationToken.None);
                    ApplyPortableUpdate(asset, stagingPath);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                TryDelete(downloadPath);
                Log("Update cancelled. Nothing was changed.");
                return false;
            }
            catch (Exception ex)
            {
                TryDelete(downloadPath);
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

        /// <summary>
        /// Keeps the version Windows shows under Installed apps in line with the running app. Installs that were updated
        /// by copying files (older updater, or a portable-style swap) would otherwise keep showing the version Setup wrote.
        /// </summary>
        /// <remarks>Only touches the registration that belongs to this installation folder. Never throws.</remarks>
        public static void SyncRegisteredVersion()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallRegistryKey, writable: true);
                if (key is null || !IsRegisteredForThisInstall(key))
                    return;

                string version = Utility.GetDisplayVersion();
                if (version == "N/A" || string.Equals(key.GetValue("DisplayVersion") as string, version, StringComparison.Ordinal))
                    return;

                key.SetValue("DisplayVersion", version);
                AppLogger.Info("UpdateManager", $"Updated the registered version to {version}.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("UpdateManager", $"Could not update the registered version: {ex.Message}");
            }
        }

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

        private static string InstallDirectory => Path.GetDirectoryName(Utility.GetExePath()) ?? AppContext.BaseDirectory;

        /// <summary>
        /// Setup always drops its uninstaller next to the exe, and portable zip extracts never have one. If the
        /// uninstaller was lost (older updaters deleted it) the registration pointing at this folder still identifies the
        /// install, and re-running Setup repairs it.
        /// </summary>
        private static bool IsInstallerInstall()
        {
            if (File.Exists(Path.Combine(InstallDirectory, UninstallerName)))
                return true;

            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(UninstallRegistryKey);
                return key is not null && IsRegisteredForThisInstall(key);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("UpdateManager", $"Could not read the installer registration: {ex.Message}");
                return false;
            }
        }

        private static bool IsRegisteredForThisInstall(RegistryKey key)
        {
            string? location = key.GetValue("InstallLocation") as string ?? key.GetValue("Inno Setup: App Path") as string;
            if (string.IsNullOrWhiteSpace(location))
                return false;

            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(InstallDirectory)),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Self-contained publishes bundle the runtime next to the exe; framework-dependent ones rely on a system-wide .NET.
        /// </summary>
        private static bool IsSelfContained() => File.Exists(Path.Combine(InstallDirectory, "System.Private.CoreLib.dll"));

        private async Task<ReleaseAsset> GetLatestReleaseAssetAsync(bool installerInstall, CancellationToken cancellationToken)
        {
            string url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using HttpResponseMessage response = await Client.GetAsync(url, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                    string tag = document.RootElement.GetProperty("tag_name").GetString() ?? "";
                    string version = tag.TrimStart('v');

                    string suffix = installerInstall
                        ? "-Setup.exe"
                        : IsSelfContained() ? "-self-contained.zip" : "-framework-dependent.zip";

                    foreach (JsonElement asset in document.RootElement.GetProperty("assets").EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string downloadUrl = asset.GetProperty("browser_download_url").GetString()
                            ?? throw new InvalidOperationException($"Release asset '{name}' has no download URL.");
                        long size = asset.TryGetProperty("size", out JsonElement sizeElement) ? sizeElement.GetInt64() : 0;

                        return new ReleaseAsset(version, name, downloadUrl, size);
                    }

                    throw new InvalidOperationException($"Release {tag} has no '*{suffix}' file to download.");
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancellationToken.IsCancellationRequested)
                {
                    if (attempt >= MaxAttempts)
                        throw new InvalidOperationException($"Could not reach GitHub ({ex.Message}).", ex);

                    Log($"Could not get the latest release ({ex.Message}). Retrying ({attempt}/{MaxAttempts - 1})...");
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                }
            }
        }

        private async Task DownloadFileAsync(ReleaseAsset asset, string destination, CancellationToken cancellationToken)
        {
            for (int attempt = 1; ; attempt++)
            {
                Exception failure;

                try
                {
                    using CancellationTokenSource stallToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    stallToken.CancelAfter(StallTimeout);

                    using HttpResponseMessage response = await Client.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, stallToken.Token);
                    response.EnsureSuccessStatusCode();

                    long total = response.Content.Headers.ContentLength ?? asset.Size;
                    Log($"Downloading {asset.Name} ({FormatBytes(total)})...");

                    await using Stream source = await response.Content.ReadAsStreamAsync(stallToken.Token);
                    await using FileStream target = new(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                    byte[] buffer = new byte[81920];
                    long done = 0;
                    long lastReport = 0;

                    while (true)
                    {
                        stallToken.CancelAfter(StallTimeout); // restart the stall timer on every successful read
                        int read = await source.ReadAsync(buffer, stallToken.Token);
                        if (read == 0)
                            break;

                        await target.WriteAsync(buffer.AsMemory(0, read), stallToken.Token);
                        done += read;

                        long now = Stopwatch.GetTimestamp();
                        if (now - lastReport >= Stopwatch.Frequency / 10)
                        {
                            lastReport = now;
                            ReportProgress(done, total);
                        }
                    }

                    ReportProgress(done, total);

                    if (total > 0 && done != total)
                        throw new IOException($"The download ended early ({FormatBytes(done)} of {FormatBytes(total)}).");

                    return;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    failure = new TimeoutException("The download stalled.");
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    failure = ex;
                }

                if (attempt >= MaxAttempts)
                    throw failure;

                Log($"Download failed ({failure.Message}). Retrying ({attempt}/{MaxAttempts - 1})...");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
        }

        private void ReportProgress(long done, long total)
        {
            int percent = total > 0 ? (int)Math.Clamp(done * 100 / total, 0, 100) : 0;
            DownloadProgress?.Invoke(this, new ProgressEventArgs(percent, string.Empty, done, total));
        }

        /// <summary>Extracts the portable zip to a staging folder and makes sure it really contains the app.</summary>
        private static string ExtractPortableUpdate(ReleaseAsset asset, string zipPath)
        {
            string stagingPath = Path.Combine(Path.GetTempPath(), "StreamDropCollector-Update", asset.Version);
            if (Directory.Exists(stagingPath))
                Directory.Delete(stagingPath, recursive: true);

            Directory.CreateDirectory(stagingPath);
            ZipFile.ExtractToDirectory(zipPath, stagingPath, overwriteFiles: true);
            File.Delete(zipPath);

            if (!File.Exists(Path.Combine(stagingPath, ExecutableName)))
                throw new FileNotFoundException("The downloaded update is incomplete (the application executable is missing).", ExecutableName);

            return stagingPath;
        }

        /// <summary>
        /// Runs the new Setup.exe silently, then relaunches the app once Setup exits. Setup's own restart handling only
        /// restarts processes it saw running, and this process exits right away, so a helper script waits on Setup's
        /// PID and relaunches explicitly.
        /// </summary>
        private static void ApplyInstallerUpdate(ReleaseAsset asset, string installerPath)
        {
            string exePath = Utility.GetExePath();

            Process installer = Process.Start(new ProcessStartInfo(installerPath, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS")
            {
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("The installer could not be started.");

            string scriptPath = Path.Combine(Path.GetTempPath(), $"StreamDropCollector-Update-{asset.Version}.ps1");
            string script = $$"""
                Wait-Process -Id {{installer.Id}} -ErrorAction SilentlyContinue
                Start-Process -FilePath {{Quote(exePath)}} -ArgumentList '--updated'
                Remove-Item -LiteralPath {{Quote(installerPath)}} -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath {{Quote(scriptPath)}} -Force -ErrorAction SilentlyContinue
                """;

            RunHelperScript(scriptPath, script);
        }

        /// <summary>
        /// Hands off to a helper script that waits for this process to exit, copies the new files over the install folder
        /// and relaunches. A separate process is required because the running exe holds its own files locked.
        /// </summary>
        private static void ApplyPortableUpdate(ReleaseAsset asset, string stagingPath)
        {
            string exePath = Utility.GetExePath();

            string scriptPath = Path.Combine(Path.GetTempPath(), $"StreamDropCollector-Update-{asset.Version}.ps1");
            string script = $$"""
                Wait-Process -Id {{Environment.ProcessId}} -ErrorAction SilentlyContinue
                for ($i = 0; $i -lt 10; $i++) {
                    try {
                        Copy-Item -Path (Join-Path {{Quote(stagingPath)}} '*') -Destination {{Quote(InstallDirectory)}} -Recurse -Force -ErrorAction Stop
                        break
                    } catch {
                        Start-Sleep -Seconds 1
                    }
                }
                Remove-Item -LiteralPath {{Quote(stagingPath)}} -Recurse -Force -ErrorAction SilentlyContinue
                Start-Process -FilePath {{Quote(exePath)}} -ArgumentList '--updated'
                Remove-Item -LiteralPath {{Quote(scriptPath)}} -Force -ErrorAction SilentlyContinue
                """;

            RunHelperScript(scriptPath, script);
        }

        private static void RunHelperScript(string scriptPath, string script)
        {
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Environment.Exit(0);
        }

        /// <summary>Wraps a value in a PowerShell single-quoted literal.</summary>
        private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

        private static void TryDelete(string? path)
        {
            try
            {
                if (path is not null && File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            { }
            catch (UnauthorizedAccessException)
            { }
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new() { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StreamDropCollector", null));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            // Use a token if one exists locally (development purposes only) to avoid API rate limits.
            string githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
            if (!string.IsNullOrEmpty(githubToken))
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", githubToken);

            return client;
        }

        private void Log(string message)
        {
            AppLogger.Info("UpdateManager", message);
            LogMessage?.Invoke(this, message);
        }

        private sealed record ReleaseAsset(string Version, string Name, string Url, long Size);
    }
}
