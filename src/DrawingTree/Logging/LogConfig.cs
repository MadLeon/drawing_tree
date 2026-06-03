/// <summary>
/// Configuration Manager (LogConfig.cs)
/// Manages application configuration settings stored in a simple key=value text file.
/// Handles reading and writing configuration parameters for logging and other settings.
/// </summary>
/// <remarks>
/// Configuration File Format:
/// - Plain text file with key=value pairs (one per line)
/// - Comments start with # or //
/// - Empty lines are ignored
/// - Configuration file: config.txt in application directory
/// 
/// Usage:
/// - Call LoadConfig() to read settings from disk
/// - Call SaveConfig() to persist changes to disk
/// - Modify properties as needed
/// </remarks>

using System.IO;

namespace DrawingTree.Logging
{
    /// <summary>
    /// Manages application configuration settings.
    /// </summary>
    public class LogConfig
    {
        private readonly string _configFilePath;
        private readonly object _lockObject = new object();

        /// <summary>
        /// Gets or sets the minimum log level to record.
        /// </summary>
        public LogLevel MinimumLogLevel { get; set; } = LogLevel.INFO;

        /// <summary>
        /// Gets or sets the number of days to retain log files.
        /// </summary>
        public int LogRetentionDays { get; set; } = 7;

        /// <summary>
        /// Initializes a new instance of the LogConfig class.
        /// </summary>
        public LogConfig()
        {
            string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _configFilePath = Path.Combine(appDirectory, "config.txt");
        }

        /// <summary>
        /// Loads configuration from the config file.
        /// Creates default configuration if file doesn't exist.
        /// </summary>
        public void LoadConfig()
        {
            lock (_lockObject)
            {
                try
                {
                    if (!File.Exists(_configFilePath))
                    {
                        SaveConfig();
                        return;
                    }

                    string[] lines = File.ReadAllLines(_configFilePath);
                    foreach (string line in lines)
                    {
                        // Skip empty lines and comments
                        string trimmedLine = line.Trim();
                        if (string.IsNullOrEmpty(trimmedLine) || 
                            trimmedLine.StartsWith("#") || 
                            trimmedLine.StartsWith("//"))
                        {
                            continue;
                        }

                        // Parse key=value
                        int separatorIndex = trimmedLine.IndexOf('=');
                        if (separatorIndex <= 0)
                        {
                            continue;
                        }

                        string key = trimmedLine.Substring(0, separatorIndex).Trim();
                        string value = trimmedLine.Substring(separatorIndex + 1).Trim();

                        // Apply configuration
                        switch (key.ToLower())
                        {
                            case "loglevel":
                            case "minimumloglevel":
                                if (Enum.TryParse<LogLevel>(value, true, out LogLevel level))
                                {
                                    MinimumLogLevel = level;
                                }
                                break;

                            case "logretentiondays":
                            case "retentiondays":
                                if (int.TryParse(value, out int days) && days > 0)
                                {
                                    LogRetentionDays = days;
                                }
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If config loading fails, use defaults
                    Console.WriteLine($"Failed to load config: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Saves current configuration to the config file.
        /// </summary>
        public void SaveConfig()
        {
            lock (_lockObject)
            {
                try
                {
                    string[] lines = new string[]
                    {
                        "# DrawingTree Application Configuration",
                        "# Format: key=value",
                        "",
                        "# Logging Configuration",
                        "# MinimumLogLevel: DEBUG, INFO, WARNING, ERROR",
                        $"MinimumLogLevel={MinimumLogLevel}",
                        "",
                        "# Log Retention (days)",
                        $"LogRetentionDays={LogRetentionDays}",
                        ""
                    };

                    File.WriteAllLines(_configFilePath, lines);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to save config: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the full path to the configuration file.
        /// </summary>
        /// <returns>Configuration file path</returns>
        public string GetConfigFilePath()
        {
            return _configFilePath;
        }
    }
}
