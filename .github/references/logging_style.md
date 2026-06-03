# Logging Style

## Logging Module

The application includes a comprehensive logging module located in `src/DrawingTree/Logging/`:

- **Logger.cs**: Singleton logger instance with thread-safe file operations
- **LogLevel.cs**: Enumeration of log severity levels
- **LogConfig.cs**: Configuration management using simple key=value text format

## Usage Examples

``csharp
// Basic logging
Logger.Instance.Info("Application started successfully");
Logger.Instance.Debug("Processing file: example.pdf");
Logger.Instance.Warning("Disk space running low");
Logger.Instance.Error("Failed to load configuration");

// Logging with exceptions
try 
{
    // Some operation
}
catch (Exception ex)
{
    Logger.Instance.Error("Operation failed", ex);
}

// Configure log settings
Logger.Instance.Config.MinimumLogLevel = LogLevel.DEBUG;
Logger.Instance.Config.LogRetentionDays = 14;
Logger.Instance.SaveConfig();
``

## Configuration

Configuration is stored in `config.txt` in the application directory using simple key=value format:

``
# DrawingTree Application Configuration
# Format: key=value

# Logging Configuration
# MinimumLogLevel: DEBUG, INFO, WARNING, ERROR
MinimumLogLevel=INFO

# Log Retention (days)
LogRetentionDays=7
``

## Logging Levels

- **DEBUG** (0): Detailed information, typically of interest only when diagnosing problems.
- **INFO** (1): Confirmation that things are working as expected.
- **WARNING** (2): An indication that something unexpected happened, or indicative of some problem in the near future (e.g., 'disk space low'). The software is still working as expected.
- **ERROR** (3): Due to a more serious problem, the software has not been able to perform some function.

## Logging Format

Each log entry includes the following information:
- Timestamp (in ISO 8601 format: yyyy-MM-ddTHH:mm:ss.fffzzz)
- Log level
- Source (ClassName.MethodName)
- Message
- Exception details (if applicable)

Sample log output:
``
2024-06-01T12:00:00.000+08:00 [INFO] [MainWindow.OnLoad] - Application started successfully
2024-06-01T12:00:05.123+08:00 [ERROR] [FileProcessor.ProcessFile] - Failed to open file
Exception: FileNotFoundException: File not found: example.pdf
StackTrace: at System.IO.File.Open(...)
``

## Log File Management

- **Location**: Application directory under `Logs/` subfolder
- **Naming**: `log_yyyy-MM-dd.txt` (e.g., `log_2024-06-01.txt`)
- **Rotation**: Automatic daily rotation (new file created each day)
- **Retention**: Configurable retention period (default: 7 days)
- **Cleanup**: Automatic cleanup runs hourly and at application startup

## Features

- Thread-safe file operations
- Automatic log directory creation
- Graceful error handling (falls back to console output)
- Configurable minimum log level filtering
- Automatic caller detection (class and method name)
- Exception stack trace logging
