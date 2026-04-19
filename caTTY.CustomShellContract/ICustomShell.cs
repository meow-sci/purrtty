namespace caTTY.Core.Terminal;

/// <summary>
///     Interface for custom shell implementations that integrate with the PTY infrastructure.
///     Custom shells provide shell-like behavior through C# code rather than external processes.
/// </summary>
public interface ICustomShell : IDisposable
{
    /// <summary>
    ///     Gets the shell metadata including name, description, and version.
    /// </summary>
    CustomShellMetadata Metadata { get; }
    
    /// <summary>
    ///     Gets whether the shell is currently running.
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    ///     Event raised when the shell produces output data.
    /// </summary>
    event EventHandler<ShellOutputEventArgs>? OutputReceived;
    
    /// <summary>
    ///     Event raised when the shell terminates.
    /// </summary>
    event EventHandler<ShellTerminatedEventArgs>? Terminated;
    
    /// <summary>
    ///     Starts the custom shell with the specified options.
    /// </summary>
    /// <param name="options">Start options for the custom shell</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the shell has started</returns>
    /// <exception cref="InvalidOperationException">Thrown if the shell is already running</exception>
    /// <exception cref="CustomShellStartException">Thrown if the shell fails to start</exception>
    Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Stops the custom shell gracefully.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the shell has stopped</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Sends input data to the shell.
    /// </summary>
    /// <param name="data">The input data to send</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the input has been processed</returns>
    /// <exception cref="InvalidOperationException">Thrown if the shell is not running</exception>
    Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    
    /// <summary>
    ///     Notifies the shell of terminal size changes.
    /// </summary>
    /// <param name="width">New terminal width in columns</param>
    /// <param name="height">New terminal height in rows</param>
    void NotifyTerminalResize(int width, int height);
    
    /// <summary>
    ///     Requests graceful cancellation of long-running operations.
    /// </summary>
    void RequestCancellation();

    /// <summary>
    ///     Sends initial output (banner, prompt, etc.) to the shell.
    ///     This is called AFTER the shell is fully initialized and wired up to the terminal,
    ///     ensuring the terminal is ready to process output before any data is sent.
    /// </summary>
    void SendInitialOutput();
}