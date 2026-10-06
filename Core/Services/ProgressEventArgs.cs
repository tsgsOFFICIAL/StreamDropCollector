namespace Core.Services
{
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
}