namespace caTTY.Core.Terminal;

/// <summary>
///     Interface for managing shell processes and their lifecycle.
///     Provides bidirectional data flow between terminal emulator and shell process.
/// </summary>
public interface IProcessManager : IDisposable
{
    /// <summary>
    ///     Gets whether a shell process is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     Gets the process ID of the running shell, or null if no process is running.
    /// </summary>
    int? ProcessId { get; }

    /// <summary>
    ///     Gets the exit code of the last process, or null if no process has exited.
    /// </summary>
    int? ExitCode { get; }

    /// <summary>
    ///     Starts a new shell process with the specified options.
    /// </summary>
    /// <param name="options">Launch options for the shell process</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the process has started</returns>
    /// <exception cref="InvalidOperationException">Thrown if a process is already running</exception>
    /// <exception cref="ProcessStartException">Thrown if the process fails to start</exception>
    Task StartAsync(ProcessLaunchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the currently running shell process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the process has stopped</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Writes data to the shell process stdin.
    /// </summary>
    /// <param name="data">The data to write</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    /// <exception cref="ProcessWriteException">Thrown if writing to the process fails</exception>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>
    ///     Writes string data to the shell process stdin.
    /// </summary>
    /// <param name="text">The text to write (will be converted to UTF-8)</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    /// <exception cref="ProcessWriteException">Thrown if writing to the process fails</exception>
    void Write(string text);

    /// <summary>
    ///     Resizes the shell process terminal dimensions.
    /// </summary>
    /// <param name="width">New width in columns</param>
    /// <param name="height">New height in rows</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    void Resize(int width, int height);

    /// <summary>
    ///     Event raised when data is received from the shell process stdout/stderr.
    /// </summary>
    event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <summary>
    ///     Event raised when the shell process exits.
    /// </summary>
    event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <summary>
    ///     Event raised when an error occurs during process operations.
    /// </summary>
    event EventHandler<ProcessErrorEventArgs>? ProcessError;
}
