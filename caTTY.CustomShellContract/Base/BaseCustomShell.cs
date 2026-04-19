namespace caTTY.Core.Terminal;

/// <summary>
///     Base class for custom shell implementations providing foundational lifecycle and state management.
/// </summary>
public abstract class BaseCustomShell : ICustomShell
{
    /// <summary>
    ///     Lock object for thread-safe access to shell state.
    /// </summary>
    protected readonly object _lock = new();

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    ///     Gets the shell metadata including name, description, and version.
    /// </summary>
    public abstract CustomShellMetadata Metadata { get; }

    /// <summary>
    ///     Gets whether the shell is currently running.
    ///     This property is thread-safe.
    /// </summary>
    public bool IsRunning
    {
        get { lock (_lock) { return _isRunning; } }
        protected set { lock (_lock) { _isRunning = value; } }
    }

    /// <summary>
    ///     Event raised when the shell produces output data.
    /// </summary>
    public event EventHandler<ShellOutputEventArgs>? OutputReceived;

    /// <summary>
    ///     Event raised when the shell terminates.
    /// </summary>
    public event EventHandler<ShellTerminatedEventArgs>? Terminated;

    /// <summary>
    ///     Raises the OutputReceived event with the specified data.
    /// </summary>
    /// <param name="data">The output data to send</param>
    /// <param name="outputType">The type of output (stdout or stderr)</param>
    protected void RaiseOutputReceived(ReadOnlyMemory<byte> data, ShellOutputType outputType = ShellOutputType.Stdout)
    {
        OutputReceived?.Invoke(this, new ShellOutputEventArgs(data, outputType));
    }

    /// <summary>
    ///     Raises the Terminated event with the specified exit code and reason.
    /// </summary>
    /// <param name="exitCode">The exit code</param>
    /// <param name="reason">Optional reason for termination</param>
    protected void RaiseTerminated(int exitCode, string? reason = null)
    {
        Terminated?.Invoke(this, new ShellTerminatedEventArgs(exitCode, reason));
    }

    /// <summary>
    ///     Starts the custom shell with the specified options.
    /// </summary>
    /// <param name="options">Start options for the custom shell</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the shell has started</returns>
    /// <exception cref="InvalidOperationException">Thrown if the shell is already running</exception>
    /// <exception cref="CustomShellStartException">Thrown if the shell fails to start</exception>
    public abstract Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the custom shell gracefully.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the shell has stopped</returns>
    public abstract Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends input data to the shell.
    /// </summary>
    /// <param name="data">The input data to send</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the input has been processed</returns>
    /// <exception cref="InvalidOperationException">Thrown if the shell is not running</exception>
    public abstract Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Notifies the shell of terminal size changes.
    ///     Default implementation does nothing - override to handle resize events.
    /// </summary>
    /// <param name="width">New terminal width in columns</param>
    /// <param name="height">New terminal height in rows</param>
    public virtual void NotifyTerminalResize(int width, int height)
    {
        // Default: no-op
    }

    /// <summary>
    ///     Requests graceful cancellation of long-running operations.
    ///     Default implementation does nothing - override to handle cancellation.
    /// </summary>
    public virtual void RequestCancellation()
    {
        // Default: no-op
    }

    /// <summary>
    ///     Sends initial output (banner, prompt, etc.) to the shell.
    ///     This is called AFTER the shell is fully initialized and wired up to the terminal,
    ///     ensuring the terminal is ready to process output before any data is sent.
    ///     Default implementation does nothing - override to send initial output.
    /// </summary>
    public virtual void SendInitialOutput()
    {
        // Default: no-op
    }

    /// <summary>
    ///     Disposes resources used by the custom shell.
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
