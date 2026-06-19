using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.IO;
using Core.Managers;

namespace Core.Logging
{
    /// <summary>
    /// Provides file-based application logging with optional verbose debug output.
    /// </summary>
    public static class AppLogger
    {
        private static readonly object _sync = new();
        private static string? _logFilePath;

        /// <summary>
        /// Gets the directory path where daily log files are stored.
        /// </summary>
        public static string LogDirectoryPath => Path.Combine(Environment.ExpandEnvironmentVariables("%APPDATA%"), "Stream Drop Collector", "logs");

        /// <summary>
        /// Gets the path to the current daily log file, or <see langword="null"/> if the logger has not been initialized.
        /// </summary>
        public static string? LogFilePath => _logFilePath;

        /// <summary>
        /// Initializes the logger and creates the daily log file if it does not already exist.
        /// </summary>
        /// <remarks>Subsequent calls have no effect once initialization has completed.</remarks>
        public static void Initialize()
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(_logFilePath))
                    return;

                string baseDir = LogDirectoryPath;

                Directory.CreateDirectory(baseDir);

                string date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                _logFilePath = Path.Combine(baseDir, $"app-{date}.log");

                Write("INFO", "Logger", $"Logger initialized. File={_logFilePath}");
            }
        }

        /// <summary>
        /// Writes an informational message to the log.
        /// </summary>
        /// <param name="scope">A short category identifying the source of the message.</param>
        /// <param name="message">The message text to record.</param>
        public static void Info(string scope, string message) => Write("INFO", scope, message);

        /// <summary>
        /// Writes a debug message to the log when verbose debug logging is enabled.
        /// </summary>
        /// <param name="scope">A short category identifying the source of the message.</param>
        /// <param name="message">The message text to record.</param>
        public static void Debug(string scope, string message)
        {
            if (!IsVerboseDebugEnabled())
                return;

            Write("DEBUG", scope, message);
        }

        /// <summary>
        /// Writes a warning message to the log.
        /// </summary>
        /// <param name="scope">A short category identifying the source of the message.</param>
        /// <param name="message">The message text to record.</param>
        public static void Warn(string scope, string message) => Write("WARN", scope, message);

        /// <summary>
        /// Writes an error message to the log, optionally including exception details.
        /// </summary>
        /// <param name="scope">A short category identifying the source of the message.</param>
        /// <param name="message">The message text to record.</param>
        /// <param name="ex">An optional exception whose type and message are appended to the log entry.</param>
        public static void Error(string scope, string message, Exception? ex = null)
        {
            if (ex == null)
            {
                Write("ERROR", scope, message);
                return;
            }

            StringBuilder sb = new StringBuilder(message);
            sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            Write("ERROR", scope, sb.ToString());
        }

        private static void Write(string level, string scope, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_logFilePath))
                    Initialize();

                string ts = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
                string line = $"[{ts}] [{level}] [{scope}] {message}";

                lock (_sync)
                {
                    File.AppendAllText(_logFilePath!, line + Environment.NewLine, Encoding.UTF8);
                }

                Trace.WriteLine(line);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[AppLogger-Fallback] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static bool IsVerboseDebugEnabled()
        {
            try
            {
                return UISettingsManager.Instance.VerboseDebugLogging;
            }
            catch
            {
                return false;
            }
        }
    }
}