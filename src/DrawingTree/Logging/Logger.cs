/// <summary>
/// Logger Core Module (Logger.cs)
/// Provides centralized logging functionality for the application.
/// Implements singleton pattern to ensure single instance across application.
/// </summary>
/// <remarks>
/// Features:
/// - Singleton pattern for global access
/// - Thread-safe file writing
/// - Automatic log rotation by date (log_yyyy-MM-dd.txt)
/// - Automatic cleanup of old log files
/// - Configurable log levels
/// 
/// Usage:
///   Logger.Instance.Info("Application started");
///   Logger.Instance.Error("Failed to load file", exception);
/// </remarks>

using System.IO;
using System.Diagnostics;

namespace DrawingTree.Logging
{
    /// <summary>
    /// Centralized logging system with automatic file management.
    /// </summary>
    public sealed class Logger
    {
        private static readonly Lazy<Logger> _instance = new Lazy<Logger>(() => new Logger());
        private readonly object _lockObject = new object();
        private readonly string _logDirectory;
        private readonly LogConfig _config;
        private DateTime _lastCleanupCheck = DateTime.MinValue;

        /// <summary>
        /// Gets the singleton instance of the Logger.
        /// </summary>
        public static Logger Instance => _instance.Value;

        /// <summary>
        /// Gets the configuration manager.
        /// </summary>
        public LogConfig Config => _config;

        /// <summary>
        /// Private constructor for singleton pattern.
        /// Initializes log directory and configuration.
        /// </summary>
        private Logger()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _logDirectory = Path.Combine(appDirectory, "Logs");

            // Create log directory if it doesn't exist
            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            // Load configuration
            _config = new LogConfig();
            _config.LoadConfig();

            // Perform initial cleanup
            CleanupOldLogs();
        }

        /// <summary>
        /// Writes a log entry with the specified level.
        /// </summary>
        /// <param name="level">Log severity level</param>
        /// <param name="message">Log message</param>
        /// <param name="exception">Optional exception to log</param>
        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            // Check if this log level should be recorded
            if (level < _config.MinimumLogLevel)
            {
                return;
            }

            lock (_lockObject)
            {
                try
                {
                    // Get current timestamp
                    DateTime now = DateTime.Now;
                    string timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"); // ISO 8601 format

                    // Get caller information
                    string source = GetCallerInfo();

                    // Build log entry
                    string logEntry = $"{timestamp} [{level}] [{source}] - {message}";

                    // Add exception details if present
                    if (exception != null)
                    {
                        logEntry += $"\nException: {exception.GetType().Name}: {exception.Message}";
                        logEntry += $"\nStackTrace: {exception.StackTrace}";
                    }

                    // Get log file path for today
                    string logFileName = $"log_{now:yyyy-MM-dd}.txt";
                    string logFilePath = Path.Combine(_logDirectory, logFileName);

                    // Write to file
                    File.AppendAllText(logFilePath, logEntry + Environment.NewLine);

                    // Periodically check for old logs to clean up
                    if ((now - _lastCleanupCheck).TotalHours >= 1)
                    {
                        CleanupOldLogs();
                        _lastCleanupCheck = now;
                    }
                }
                catch (Exception ex)
                {
                    // If logging fails, write to console as fallback
                    Console.WriteLine($"Logger error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Logs a DEBUG level message.
        /// </summary>
        /// <param name="message">Log message</param>
        public void Debug(string message)
        {
            Log(LogLevel.DEBUG, message);
        }

        /// <summary>
        /// Logs an INFO level message.
        /// </summary>
        /// <param name="message">Log message</param>
        public void Info(string message)
        {
            Log(LogLevel.INFO, message);
        }

        /// <summary>
        /// Logs a WARNING level message.
        /// </summary>
        /// <param name="message">Log message</param>
        public void Warning(string message)
        {
            Log(LogLevel.WARNING, message);
        }

        /// <summary>
        /// Logs a WARNING level message with exception.
        /// </summary>
        /// <param name="message">Log message</param>
        /// <param name="exception">Exception to log</param>
        public void Warning(string message, Exception exception)
        {
            Log(LogLevel.WARNING, message, exception);
        }

        /// <summary>
        /// Logs an ERROR level message.
        /// </summary>
        /// <param name="message">Log message</param>
        public void Error(string message)
        {
            Log(LogLevel.ERROR, message);
        }

        /// <summary>
        /// Logs an ERROR level message with exception.
        /// </summary>
        /// <param name="message">Log message</param>
        /// <param name="exception">Exception to log</param>
        public void Error(string message, Exception exception)
        {
            Log(LogLevel.ERROR, message, exception);
        }

        /// <summary>
        /// Cleans up log files older than the retention period.
        /// </summary>
        private void CleanupOldLogs()
        {
            try
            {
                DateTime cutoffDate = DateTime.Now.AddDays(-_config.LogRetentionDays);
                DirectoryInfo logDir = new DirectoryInfo(_logDirectory);
                FileInfo[] logFiles = logDir.GetFiles("log_*.txt");

                foreach (FileInfo file in logFiles)
                {
                    // Parse date from filename (log_yyyy-MM-dd.txt)
                    string fileName = Path.GetFileNameWithoutExtension(file.Name);
                    if (fileName.StartsWith("log_") && fileName.Length >= 14)
                    {
                        string dateString = fileName.Substring(4, 10); // Extract yyyy-MM-dd
                        if (DateTime.TryParseExact(dateString, "yyyy-MM-dd", 
                            null, System.Globalization.DateTimeStyles.None, out DateTime fileDate))
                        {
                            if (fileDate < cutoffDate)
                            {
                                file.Delete();
                                Info($"Deleted old log file: {file.Name}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets caller information from the stack trace.
        /// </summary>
        /// <returns>Caller information in format "ClassName.MethodName"</returns>
        private string GetCallerInfo()
        {
            try
            {
                System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace(3, false);
                System.Diagnostics.StackFrame? frame = stackTrace.GetFrame(0);

                if (frame != null)
                {
                    var method = frame.GetMethod();
                    if (method != null && method.DeclaringType != null)
                    {
                        return $"{method.DeclaringType.Name}.{method.Name}";
                    }
                }
            }
            catch
            {
                // If we can't get caller info, return unknown
            }

            return "Unknown";
        }

        /// <summary>
        /// Gets the log directory path.
        /// </summary>
        /// <returns>Full path to log directory</returns>
        public string GetLogDirectory()
        {
            return _logDirectory;
        }

        /// <summary>
        /// Reloads configuration from disk.
        /// </summary>
        public void ReloadConfig()
        {
            lock (_lockObject)
            {
                _config.LoadConfig();
            }
        }

        /// <summary>
        /// Saves current configuration to disk.
        /// </summary>
        public void SaveConfig()
        {
            lock (_lockObject)
            {
                _config.SaveConfig();
            }
        }
    }
}
