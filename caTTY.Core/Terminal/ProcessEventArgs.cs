using System.Text;

namespace caTTY.Core.Terminal;

/// <summary>
///     Event arguments for data received from a shell process.
/// </summary>
public class DataReceivedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new data received event arguments.
    /// </summary>
    /// <param name="data">The data received</param>
    /// <param name="isError">Whether the data is from stderr</param>
    public DataReceivedEventArgs(ReadOnlyMemory<byte> data, bool isError = false)
    {
        Data = data;
        IsError = isError;
    }

    /// <summary>
    ///     Creates new data received event arguments from string data.
    /// </summary>
    /// <param name="text">The text received (will be converted to UTF-8)</param>
    /// <param name="isError">Whether the data is from stderr</param>
    public DataReceivedEventArgs(string text, bool isError = false)
    {
        Data = Encoding.UTF8.GetBytes(text);
        IsError = isError;
    }

    /// <summary>
    ///     Gets the data received from the process.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    ///     Gets whether the data came from stderr (true) or stdout (false).
    /// </summary>
    public bool IsError { get; }
}

/// <summary>
///     Event arguments for process exit notifications.
/// </summary>
public class ProcessExitedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new process exited event arguments.
    /// </summary>
    /// <param name="exitCode">The exit code</param>
    /// <param name="processId">The process ID</param>
    public ProcessExitedEventArgs(int exitCode, int processId)
    {
        ExitCode = exitCode;
        ProcessId = processId;
        ExitTime = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the exit code of the process.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    ///     Gets the process ID that exited.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    ///     Gets the time when the process exited.
    /// </summary>
    public DateTime ExitTime { get; }
}

/// <summary>
///     Event arguments for process error notifications.
/// </summary>
public class ProcessErrorEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new process error event arguments.
    /// </summary>
    /// <param name="error">The error that occurred</param>
    /// <param name="message">The error message</param>
    /// <param name="processId">The process ID, if applicable</param>
    public ProcessErrorEventArgs(Exception error, string message, int? processId = null)
    {
        Error = error;
        Message = message;
        ProcessId = processId;
    }

    /// <summary>
    ///     Gets the error that occurred.
    /// </summary>
    public Exception Error { get; }

    /// <summary>
    ///     Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the process ID associated with the error, if any.
    /// </summary>
    public int? ProcessId { get; }
}
