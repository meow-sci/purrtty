namespace caTTY.Core.Terminal;

/// <summary>
///     Exception thrown when a process fails to start.
/// </summary>
public class ProcessStartException : Exception
{
    /// <summary>
    ///     Creates a new ProcessStartException.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="shellPath">The shell path that failed</param>
    public ProcessStartException(string message, string? shellPath = null) : base(message)
    {
        ShellPath = shellPath;
    }

    /// <summary>
    ///     Creates a new ProcessStartException with an inner exception.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    /// <param name="shellPath">The shell path that failed</param>
    public ProcessStartException(string message, Exception innerException, string? shellPath = null)
        : base(message, innerException)
    {
        ShellPath = shellPath;
    }

    /// <summary>
    ///     Gets the shell path that failed to start.
    /// </summary>
    public string? ShellPath { get; }
}

/// <summary>
///     Exception thrown when writing to a process fails.
/// </summary>
public class ProcessWriteException : Exception
{
    /// <summary>
    ///     Creates a new ProcessWriteException.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="processId">The process ID that failed</param>
    public ProcessWriteException(string message, int? processId = null) : base(message)
    {
        ProcessId = processId;
    }

    /// <summary>
    ///     Creates a new ProcessWriteException with an inner exception.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    /// <param name="processId">The process ID that failed</param>
    public ProcessWriteException(string message, Exception innerException, int? processId = null)
        : base(message, innerException)
    {
        ProcessId = processId;
    }

    /// <summary>
    ///     Gets the process ID that failed to write to.
    /// </summary>
    public int? ProcessId { get; }
}
