/// <summary>
/// Log Level Enumeration (LogLevel.cs)
/// Defines the severity levels for logging messages.
/// Each level represents increasing severity from DEBUG to ERROR.
/// </summary>
/// <remarks>
/// Log Levels:
/// - DEBUG: Detailed diagnostic information for troubleshooting
/// - INFO: General informational messages about application flow
/// - WARNING: Warnings about potential issues that don't stop execution
/// - ERROR: Error events that prevent normal operation
/// </remarks>
namespace DrawingTree.Logging
{
    /// <summary>
    /// Represents the severity level of a log message.
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// Detailed information for diagnosing problems.
        /// </summary>
        DEBUG = 0,

        /// <summary>
        /// Informational messages confirming expected behavior.
        /// </summary>
        INFO = 1,

        /// <summary>
        /// Warning messages indicating potential issues.
        /// </summary>
        WARNING = 2,

        /// <summary>
        /// Error messages indicating failures.
        /// </summary>
        ERROR = 3
    }
}
