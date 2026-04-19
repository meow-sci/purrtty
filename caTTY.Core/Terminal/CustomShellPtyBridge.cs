using System.Text;

namespace caTTY.Core.Terminal;

/// <summary>
///     PTY Bridge that connects custom shell implementations to the existing PTY infrastructure.
///     Acts as an adapter between ICustomShell and IProcessManager interfaces.
/// </summary>
public class CustomShellPtyBridge : IProcessManager
{
    private readonly ICustomShell _customShell;
    private readonly TaskCompletionSource<int> _exitCodeSource;
    private readonly object _stateLock = new();
    private bool _isRunning;
    private bool _isDisposed;
    private int? _processId;

    /// <summary>
    ///     Creates a new CustomShellPtyBridge for the specified custom shell.
    /// </summary>
    /// <param name="customShell">The custom shell to bridge</param>
    /// <exception cref="ArgumentNullException">Thrown if customShell is null</exception>
    public CustomShellPtyBridge(ICustomShell customShell)
    {
        _customShell = customShell ?? throw new ArgumentNullException(nameof(customShell));
        _exitCodeSource = new TaskCompletionSource<int>();
        
        // Wire up custom shell events to PTY events
        _customShell.OutputReceived += OnCustomShellOutput;
        _customShell.Terminated += OnCustomShellTerminated;
    }

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _isRunning && !_isDisposed;
            }
        }
    }

    /// <inheritdoc />
    public int? ProcessId
    {
        get
        {
            lock (_stateLock)
            {
                return _isRunning ? _processId : null;
            }
        }
    }

    /// <inheritdoc />
    public int? ExitCode
    {
        get
        {
            return _exitCodeSource.Task.IsCompleted ? _exitCodeSource.Task.Result : null;
        }
    }

    /// <inheritdoc />
    public event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <inheritdoc />
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <inheritdoc />
    public event EventHandler<ProcessErrorEventArgs>? ProcessError;

    /// <inheritdoc />
    public async Task StartAsync(ProcessLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("Custom shell is already running");
            }
        }

        try
        {
            // Convert ProcessLaunchOptions to CustomShellStartOptions
            var customOptions = new CustomShellStartOptions
            {
                InitialWidth = options.InitialWidth,
                InitialHeight = options.InitialHeight,
                WorkingDirectory = options.WorkingDirectory,
                EnvironmentVariables = new Dictionary<string, string>(options.EnvironmentVariables)
            };

            // Start the custom shell - the shell handles output asynchronously through
            // its internal output channel/pump, just like ProcessManager reads from ConPTY.
            // Output events will flow through once the shell's output pump starts running.
            await _customShell.StartAsync(customOptions, cancellationToken);

            lock (_stateLock)
            {
                _isRunning = true;
                // Use current process ID as a placeholder since custom shells don't have real process IDs
                _processId = Environment.ProcessId;
            }
        }
        catch (Exception ex) when (ex is not CustomShellStartException)
        {
            // Wrap non-custom shell exceptions in ProcessStartException for consistency
            var shellId = options.CustomShellId ?? _customShell.Metadata.Name;
            throw new ProcessStartException($"Failed to start custom shell '{shellId}': {ex.Message}", ex, shellId);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        bool wasRunning;
        lock (_stateLock)
        {
            wasRunning = _isRunning;
            _isRunning = false;
        }

        if (wasRunning)
        {
            try
            {
                await _customShell.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Report stop errors but don't throw - we want to ensure cleanup happens
                ProcessError?.Invoke(this, new ProcessErrorEventArgs(ex, $"Error stopping custom shell: {ex.Message}", _processId));
            }
        }
    }

    /// <inheritdoc />
    public void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (!_isRunning)
            {
                throw new InvalidOperationException("Custom shell is not running");
            }
        }

        try
        {
            // Convert span to memory and send to custom shell asynchronously
            // We don't await this to maintain the synchronous interface contract
            _ = _customShell.WriteInputAsync(data.ToArray(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            throw new ProcessWriteException($"Failed to write to custom shell: {ex.Message}", ex, _processId);
        }
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        Write(bytes.AsSpan());
    }

    /// <inheritdoc />
    public void Resize(int width, int height)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (!_isRunning)
            {
                throw new InvalidOperationException("Custom shell is not running");
            }
        }

        try
        {
            _customShell.NotifyTerminalResize(width, height);
        }
        catch (Exception ex)
        {
            ProcessError?.Invoke(this, new ProcessErrorEventArgs(ex, $"Error resizing custom shell terminal: {ex.Message}", _processId));
        }
    }

    /// <summary>
    ///     Handles output from the custom shell and translates it to PTY events.
    /// </summary>
    /// <param name="sender">The event sender</param>
    /// <param name="e">The shell output event arguments</param>
    private void OnCustomShellOutput(object? sender, ShellOutputEventArgs e)
    {
        try
        {
            // Convert custom shell output to PTY data received event
            var isError = e.OutputType == ShellOutputType.Stderr;
            DataReceived?.Invoke(this, new DataReceivedEventArgs(e.Data, isError));
        }
        catch (Exception ex)
        {
            ProcessError?.Invoke(this, new ProcessErrorEventArgs(ex, $"Error processing custom shell output: {ex.Message}", _processId));
        }
    }

    /// <summary>
    ///     Handles termination from the custom shell and translates it to PTY events.
    /// </summary>
    /// <param name="sender">The event sender</param>
    /// <param name="e">The shell terminated event arguments</param>
    private void OnCustomShellTerminated(object? sender, ShellTerminatedEventArgs e)
    {
        try
        {
            lock (_stateLock)
            {
                _isRunning = false;
            }

            // Set the exit code for any waiting tasks
            _exitCodeSource.TrySetResult(e.ExitCode);
            
            // Notify PTY infrastructure of process exit
            ProcessExited?.Invoke(this, new ProcessExitedEventArgs(e.ExitCode, _processId ?? 0));
        }
        catch (Exception ex)
        {
            ProcessError?.Invoke(this, new ProcessErrorEventArgs(ex, $"Error processing custom shell termination: {ex.Message}", _processId));
        }
    }

    /// <summary>
    ///     Throws ObjectDisposedException if the bridge has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(CustomShellPtyBridge));
        }
    }

    /// <summary>
    ///     Sends initial output (banner, prompt, etc.) to the custom shell.
    ///     This should be called AFTER the session is fully initialized and wired up.
    /// </summary>
    public void SendInitialOutput()
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (!_isRunning)
            {
                throw new InvalidOperationException("Custom shell is not running");
            }
        }

        _customShell.SendInitialOutput();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        lock (_stateLock)
        {
            _isDisposed = true;
            _isRunning = false;
        }

        // Unsubscribe from events to prevent memory leaks
        _customShell.OutputReceived -= OnCustomShellOutput;
        _customShell.Terminated -= OnCustomShellTerminated;

        // Dispose the custom shell
        try
        {
            _customShell.Dispose();
        }
        catch (Exception ex)
        {
            // Log disposal errors but don't throw from Dispose
            ProcessError?.Invoke(this, new ProcessErrorEventArgs(ex, $"Error disposing custom shell: {ex.Message}", _processId));
        }

        // Complete the exit code task if it hasn't been completed yet
        _exitCodeSource.TrySetResult(-1);
    }
}