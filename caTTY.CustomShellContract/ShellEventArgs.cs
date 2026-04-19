using System.Text;

namespace caTTY.Core.Terminal;

/// <summary>
///     Output type enumeration for shell output events.
/// </summary>
public enum ShellOutputType
{
    /// <summary>
    ///     Standard output stream.
    /// </summary>
    Stdout,

    /// <summary>
    ///     Standard error stream.
    /// </summary>
    Stderr
}

/// <summary>
///     Event arguments for custom shell output notifications.
/// </summary>
public class ShellOutputEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new shell output event arguments.
    /// </summary>
    /// <param name="data">The output data</param>
    /// <param name="outputType">The type of output (stdout or stderr)</param>
    public ShellOutputEventArgs(ReadOnlyMemory<byte> data, ShellOutputType outputType = ShellOutputType.Stdout)
    {
        Data = data;
        OutputType = outputType;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Creates new shell output event arguments from string data.
    /// </summary>
    /// <param name="text">The output text (will be converted to UTF-8)</param>
    /// <param name="outputType">The type of output (stdout or stderr)</param>
    public ShellOutputEventArgs(string text, ShellOutputType outputType = ShellOutputType.Stdout)
    {
        Data = Encoding.UTF8.GetBytes(text);
        OutputType = outputType;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the output data from the shell.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    ///     Gets the type of output (stdout or stderr).
    /// </summary>
    public ShellOutputType OutputType { get; }

    /// <summary>
    ///     Gets the time when the output was generated.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for custom shell termination notifications.
/// </summary>
public class ShellTerminatedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new shell terminated event arguments.
    /// </summary>
    /// <param name="exitCode">The exit code</param>
    /// <param name="reason">Optional reason for termination</param>
    public ShellTerminatedEventArgs(int exitCode, string? reason = null)
    {
        ExitCode = exitCode;
        Reason = reason;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the exit code of the shell.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    ///     Gets the reason for termination, if provided.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    ///     Gets the time when the shell terminated.
    /// </summary>
    public DateTime Timestamp { get; }
}
