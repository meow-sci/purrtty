namespace caTTY.Core.Terminal;

/// <summary>
///     Handles logging for session lifecycle events.
///     Uses quiet operation principles - only logs when explicitly enabled or for errors.
/// </summary>
internal class SessionLogging
{
    /// <summary>
    ///     Logs session lifecycle events for debugging and monitoring.
    ///     Uses quiet operation principles - only logs when explicitly enabled or for errors.
    /// </summary>
    /// <param name="message">The log message</param>
    /// <param name="exception">Optional exception to log</param>
    public static void LogSessionLifecycleEvent(string message, Exception? exception = null)
    {
        // Follow quiet operation requirements - only log errors or when debug is enabled
        // Normal session operations should produce no output
        if (exception != null)
        {
            // Always log errors for debugging
            Console.WriteLine($"SessionManager Error: {message}");
            if (exception != null)
            {
                Console.WriteLine($"SessionManager Exception: {exception.Message}");
            }
        }
        // For non-error events, only log if debug mode is enabled
        // This could be controlled by a configuration setting in the future
        else if (IsDebugLoggingEnabled())
        {
            Console.WriteLine($"SessionManager: {message}");
        }
    }

    /// <summary>
    ///     Determines if debug logging is enabled for session lifecycle events.
    ///     Currently always returns false to maintain quiet operation.
    ///     Can be enhanced with configuration in the future.
    /// </summary>
    /// <returns>True if debug logging should be enabled</returns>
    public static bool IsDebugLoggingEnabled()
    {
        // For now, maintain quiet operation by default
        // This could be controlled by environment variables or configuration
        return false;
    }
}
