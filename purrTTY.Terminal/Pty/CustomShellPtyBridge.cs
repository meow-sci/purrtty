using System.Text;

namespace purrTTY.Core.Terminal;

/// <summary>
///     PTY Bridge that connects custom shell implementations to the existing PTY infrastructure.
///     Acts as an adapter between ICustomShell and IProcessManager interfaces.
/// </summary>
public class CustomShellPtyBridge : IProcessManager
{
    private readonly ICustomShell _customShell;
    private readonly object _stateLock = new();

    // Per-run: replaced with a fresh source at the top of each StartAsync.
    // GameConsoleShell raises Terminated from its stop hook, so a stop/restart
    // cycle completes this source — reusing the completed one would make the
    // next start look already-terminated (input silently dropped, no initial
    // output) and pin ExitCode to the previous run. Read/replace under _stateLock.
    private TaskCompletionSource<int> _exitCodeSource;
    private bool _isRunning;
    private bool _starting;
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
            TaskCompletionSource<int> exitSource;
            lock (_stateLock)
            {
                exitSource = _exitCodeSource;
            }

            return exitSource.Task.IsCompleted ? exitSource.Task.Result : null;
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
            // The _starting guard closes the check-then-act window across the
            // await below, same as the real PTY managers (gotcha 20 / B9).
            if (_isRunning || _starting)
            {
                throw new InvalidOperationException("Custom shell is already running");
            }

            _starting = true;

            // Fresh exit-code source for this run (see the field doc). A prior
            // run's Terminated is delivered before its StopAsync returns
            // (BaseChannelOutputShell drains the pump first), so it cannot land
            // on this new source.
            if (_exitCodeSource.Task.IsCompleted)
            {
                _exitCodeSource = new TaskCompletionSource<int>();
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

            bool startedAlive;
            lock (_stateLock)
            {
                // A shell that terminated synchronously inside its StartAsync has
                // already fired Terminated and completed the exit-code source;
                // setting _isRunning here would resurrect a dead shell's flag.
                startedAlive = !_exitCodeSource.Task.IsCompleted;
                if (startedAlive)
                {
                    _isRunning = true;
                }

                // Use current process ID as a placeholder since custom shells don't have real process IDs
                _processId = Environment.ProcessId;
            }

            if (startedAlive)
            {
                // A real PTY shell emits its startup output (banner/prompt) as a
                // consequence of spawning; the bridge triggers the custom shell's
                // equivalent here so "StartAsync returned" means the same thing
                // for both manager kinds. DataReceived is already subscribed
                // (TerminalSession wires it in its constructor, before
                // InitializeAsync) and delivery rides the shell's ordered output
                // channel. A banner fault is reported like any other output-path
                // error rather than failing the start — the shell is running.
                try
                {
                    _customShell.SendInitialOutput();
                }
                catch (Exception ex)
                {
                    ProcessError?.Invoke(this, new ProcessErrorEventArgs(
                        ex, $"Custom shell initial output failed: {ex.Message}", _processId));
                }
            }
        }
        catch (Exception ex) when (ex is not ProcessStartException)
        {
            // Wrap everything (including CustomShellStartException, which does NOT
            // derive from ProcessStartException) so the IProcessManager contract
            // — "start failures throw ProcessStartException" — holds for callers.
            var shellId = options.CustomShellId ?? _customShell.Metadata.Name;
            throw new ProcessStartException($"Failed to start custom shell '{shellId}': {ex.Message}", ex, shellId);
        }
        finally
        {
            lock (_stateLock)
            {
                _starting = false;
            }
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
            // Convert span to memory and send to custom shell asynchronously; the
            // synchronous interface contract means we cannot await. For the built-in
            // BaseLineBufferedShell the call completes synchronously, so ordering is
            // preserved; third-party fully-async shells get no cross-call ordering
            // guarantee here, and their faults are observed below instead of being
            // silently swallowed by the abandoned task.
            Task writeTask = _customShell.WriteInputAsync(data.ToArray(), CancellationToken.None);
            if (!writeTask.IsCompletedSuccessfully)
            {
                writeTask.ContinueWith(
                    task =>
                    {
                        Exception error = task.Exception!.GetBaseException();
                        ProcessError?.Invoke(this, new ProcessErrorEventArgs(
                            error, $"Custom shell input write failed: {error.Message}", _processId));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            }
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
            TaskCompletionSource<int> exitSource;
            lock (_stateLock)
            {
                _isRunning = false;
                exitSource = _exitCodeSource;
            }

            // Set the exit code for any waiting tasks
            exitSource.TrySetResult(e.ExitCode);

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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        TaskCompletionSource<int> exitSource;
        lock (_stateLock)
        {
            _isDisposed = true;
            _isRunning = false;
            exitSource = _exitCodeSource;
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
        exitSource.TrySetResult(-1);
    }
}