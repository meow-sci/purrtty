using System.Runtime.InteropServices;
using System.Text;
using caTTY.Core.Rpc.Socket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AttributeListBuilder = caTTY.Core.Terminal.Process.AttributeListBuilder;
using ConPtyInputWriter = caTTY.Core.Terminal.Process.ConPtyInputWriter;
using ConPtyNative = caTTY.Core.Terminal.Process.ConPtyNative;
using ConPtyOutputPump = caTTY.Core.Terminal.Process.ConPtyOutputPump;
using ConPtyPipeManager = caTTY.Core.Terminal.Process.ConPtyPipeManager;
using ProcessCleanup = caTTY.Core.Terminal.Process.ProcessCleanup;
using ProcessEvents = caTTY.Core.Terminal.Process.ProcessEvents;
using ProcessLifecycleManager = caTTY.Core.Terminal.Process.ProcessLifecycleManager;
using ProcessStateManager = caTTY.Core.Terminal.Process.ProcessStateManager;
using ShellCommandResolver = caTTY.Core.Terminal.Process.ShellCommandResolver;
using StartupInfoBuilder = caTTY.Core.Terminal.Process.StartupInfoBuilder;
using SysProcess = System.Diagnostics.Process;

namespace caTTY.Core.Terminal;

/// <summary>
///     Manages shell processes using Windows Pseudoconsole (ConPTY) for true PTY functionality.
///     Follows Microsoft's recommended approach from:
///     https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session
/// </summary>
public class ProcessManager : IProcessManager
{
    private readonly object _processLock = new();
    private Process.ConPtyNative.COORD _currentSize;
    private bool _disposed;
    private IntPtr _inputReadHandle = IntPtr.Zero;
    private IntPtr _inputWriteHandle = IntPtr.Zero;
    private IntPtr _outputReadHandle = IntPtr.Zero;
    private Task? _outputReadTask;
    private IntPtr _outputWriteHandle = IntPtr.Zero;
    private SysProcess? _process;

    private IntPtr _pseudoConsole = IntPtr.Zero;
    private CancellationTokenSource? _readCancellationSource;

    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new ProcessManager with optional logging.
    /// </summary>
    /// <param name="logger">Logger for diagnostics (optional)</param>
    public ProcessManager(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    ///     Gets whether a shell process is currently running.
    /// </summary>
    public bool IsRunning => ProcessStateManager.IsRunning(_process, _processLock);

    /// <summary>
    ///     Gets the process ID of the running shell, or null if no process is running.
    /// </summary>
    public int? ProcessId => ProcessStateManager.GetProcessId(_process, _processLock);

    /// <summary>
    ///     Gets the exit code of the last process, or null if no process has exited.
    /// </summary>
    public int? ExitCode => ProcessStateManager.GetExitCode(_process, _processLock);

    /// <summary>
    ///     Event raised when data is received from the shell process stdout/stderr.
    /// </summary>
    public event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <summary>
    ///     Event raised when the shell process exits.
    /// </summary>
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <summary>
    ///     Event raised when an error occurs during process operations.
    /// </summary>
    public event EventHandler<ProcessErrorEventArgs>? ProcessError;

    /// <summary>
    ///     Starts a new shell process with the specified options using Windows ConPTY.
    /// </summary>
    /// <param name="options">Launch options for the shell process</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the process has started</returns>
    /// <exception cref="InvalidOperationException">Thrown if a process is already running</exception>
    /// <exception cref="ProcessStartException">Thrown if the process fails to start</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown on non-Windows platforms</exception>
    public async Task StartAsync(ProcessLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ConPTY is only supported on Windows 10 version 1809 and later");
        }

        lock (_processLock)
        {
            if (_process != null)
            {
                throw new InvalidOperationException(
                    "A process is already running. Stop the current process before starting a new one.");
            }
        }

        try
        {
            // Store terminal size
            _currentSize = new ConPtyNative.COORD((short)options.InitialWidth, (short)options.InitialHeight);

            // Create communication pipes and pseudoconsole
            var pipeHandles = ConPtyPipeManager.CreatePipesAndPseudoConsole(_currentSize);
            _inputWriteHandle = pipeHandles.InputWriteHandle;
            _outputReadHandle = pipeHandles.OutputReadHandle;
            _pseudoConsole = pipeHandles.PseudoConsole;

            // Prepare startup information
            var startupInfo = StartupInfoBuilder.Create();

            // Initialize process thread attribute list with pseudoconsole
            try
            {
                startupInfo.lpAttributeList = AttributeListBuilder.CreateAttributeListWithPseudoConsole(_pseudoConsole);
            }
            catch (ProcessStartException)
            {
                CleanupPseudoConsole();
                throw;
            }

            // Query if socket RPC server is available and add endpoint to environment
            var endpoint = SocketRpcServerFactory.GetActiveEndpoint();
            if (endpoint != null)
            {
                options.EnvironmentVariables[SocketRpcServerFactory.EndpointEnvVar] = endpoint;
                _logger.LogDebug("Socket RPC endpoint {Endpoint} added to child process environment", endpoint);
            }

            // Create environment block from options (includes socket RPC endpoint if available)
            IntPtr envBlock = IntPtr.Zero;
            try
            {
                envBlock = ProcessLifecycleManager.CreateEnvironmentBlock(options.EnvironmentVariables);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create environment block, using parent environment");
            }

            // Resolve shell command
            (string shellPath, string shellArgs) = ShellCommandResolver.ResolveShellCommand(options);
            string commandLine = string.IsNullOrEmpty(shellArgs) ? shellPath : $"{shellPath} {shellArgs}";

            // Create the process
            ConPtyNative.PROCESS_INFORMATION processInfo;
            try
            {
                processInfo = ProcessLifecycleManager.CreateProcess(
                    commandLine,
                    options.WorkingDirectory ?? Environment.CurrentDirectory,
                    ref startupInfo,
                    envBlock);
            }
            catch
            {
                if (envBlock != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(envBlock);
                }
                AttributeListBuilder.FreeAttributeList(startupInfo.lpAttributeList);
                CleanupPseudoConsole();
                throw;
            }
            finally
            {
                // Free environment block after process creation
                if (envBlock != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(envBlock);
                }
            }

            // Clean up startup info
            AttributeListBuilder.FreeAttributeList(startupInfo.lpAttributeList);

            // Wrap the process handle in a Process object for lifecycle management
            var process = ProcessLifecycleManager.WrapProcessHandle(processInfo, OnProcessExited);

            lock (_processLock)
            {
                _process = process;
                _readCancellationSource = new CancellationTokenSource();
            }

            // Start reading output
            CancellationToken readToken = _readCancellationSource.Token;
            _outputReadTask = ReadOutputAsync(readToken);

            // Validate that the process started successfully
            try
            {
                await ProcessLifecycleManager.ValidateProcessStartAsync(process, shellPath, cancellationToken);
            }
            catch
            {
                CleanupProcess();
                throw;
            }
        }
        catch (Exception ex) when (!(ex is ProcessStartException))
        {
            CleanupProcess();
            throw new ProcessStartException($"Failed to start shell process: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Stops the currently running shell process and cleans up ConPTY resources.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the process has stopped</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        SysProcess? processToStop;
        CancellationTokenSource? cancellationSource;
        Task? readTask;

        lock (_processLock)
        {
            processToStop = _process;
            cancellationSource = _readCancellationSource;
            readTask = _outputReadTask;
        }

        try
        {
            await ProcessLifecycleManager.StopProcessGracefullyAsync(processToStop, cancellationSource, readTask);
        }
        finally
        {
            CleanupProcess();
        }
    }

    /// <summary>
    ///     Writes data to the shell process stdin via ConPTY.
    /// </summary>
    /// <param name="data">The data to write</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    /// <exception cref="ProcessWriteException">Thrown if writing to the process fails</exception>
    public void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        SysProcess? currentProcess;
        lock (_processLock)
        {
            currentProcess = _process;
        }

        ProcessStateManager.ValidateProcessRunning(currentProcess, _inputWriteHandle);
        ConPtyInputWriter.Write(data, _inputWriteHandle, currentProcess!, args => OnProcessError(args));
    }

    /// <summary>
    ///     Writes string data to the shell process stdin.
    /// </summary>
    /// <param name="text">The text to write (will be converted to UTF-8)</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    /// <exception cref="ProcessWriteException">Thrown if writing to the process fails</exception>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ThrowIfDisposed();

        SysProcess? currentProcess;
        lock (_processLock)
        {
            currentProcess = _process;
        }

        ProcessStateManager.ValidateProcessRunning(currentProcess, _inputWriteHandle);
        ConPtyInputWriter.Write(text, _inputWriteHandle, currentProcess!, args => OnProcessError(args));
    }

    /// <summary>
    ///     Resizes the pseudoconsole terminal dimensions.
    ///     Uses Windows ConPTY ResizePseudoConsole API for proper terminal resizing.
    /// </summary>
    /// <param name="width">New width in columns</param>
    /// <param name="height">New height in rows</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown on non-Windows platforms</exception>
    public void Resize(int width, int height)
    {
        ThrowIfDisposed();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ConPTY resizing is only supported on Windows");
        }

        SysProcess? currentProcess;
        lock (_processLock)
        {
            currentProcess = _process;
        }

        ProcessStateManager.ValidateProcessForResize(currentProcess, _pseudoConsole);

        var newSize = new ConPtyNative.COORD((short)width, (short)height);
        int result = ConPtyNative.ResizePseudoConsole(_pseudoConsole, newSize);

        if (result != 0)
        {
            throw new InvalidOperationException($"Failed to resize pseudoconsole: Win32 error {result}");
        }

        _currentSize = newSize;
    }

    /// <summary>
    ///     Disposes the process manager and cleans up all resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopAsync().Wait(5000); // Wait up to 5 seconds for graceful shutdown
            }
            catch
            {
                // Ignore errors during disposal
            }

            CleanupProcess();
            _disposed = true;
        }
    }

    /// <summary>
    ///     Reads data from the ConPTY output pipe asynchronously and raises DataReceived events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task ReadOutputAsync(CancellationToken cancellationToken)
    {
        await ConPtyOutputPump.ReadOutputAsync(
            _outputReadHandle,
            () => ProcessId,
            data => OnDataReceived(new DataReceivedEventArgs(data)),
            (exception, message) => OnProcessError(new ProcessErrorEventArgs(exception, message, ProcessId)),
            cancellationToken);
    }

    /// <summary>
    ///     Handles process exit events.
    /// </summary>
    private void OnProcessExited(object? sender, EventArgs e)
    {
        ProcessEvents.HandleProcessExited(sender, e, CleanupProcess, ProcessExited, this);
    }

    /// <summary>
    ///     Raises the DataReceived event.
    /// </summary>
    private void OnDataReceived(DataReceivedEventArgs args)
    {
        ProcessEvents.RaiseDataReceived(DataReceived, this, args);
    }

    /// <summary>
    ///     Raises the ProcessError event.
    /// </summary>
    private void OnProcessError(ProcessErrorEventArgs args)
    {
        ProcessEvents.RaiseProcessError(ProcessError, this, args);
    }

    /// <summary>
    ///     Cleans up process resources including ConPTY handles.
    /// </summary>
    private void CleanupProcess()
    {
        lock (_processLock)
        {
            _readCancellationSource?.Cancel();
            _readCancellationSource?.Dispose();
            _readCancellationSource = null;

            ProcessCleanup.CleanupProcess(_process, OnProcessExited);
            _process = null;

            _outputReadTask = null;

            CleanupPseudoConsole();
        }
    }

    /// <summary>
    ///     Cleans up ConPTY-specific resources.
    /// </summary>
    private void CleanupPseudoConsole()
    {
        ProcessCleanup.CleanupPseudoConsole(_pseudoConsole, _inputReadHandle, _inputWriteHandle, _outputReadHandle, _outputWriteHandle);

        _pseudoConsole = IntPtr.Zero;
        _inputWriteHandle = IntPtr.Zero;
        _outputReadHandle = IntPtr.Zero;
        _inputReadHandle = IntPtr.Zero;
        _outputWriteHandle = IntPtr.Zero;
    }

    /// <summary>
    ///     Cleans up pipe handles.
    /// </summary>
    private void CleanupHandles()
    {
        ProcessCleanup.CleanupHandles(_inputReadHandle, _inputWriteHandle, _outputReadHandle, _outputWriteHandle);

        _inputWriteHandle = IntPtr.Zero;
        _outputReadHandle = IntPtr.Zero;
        _inputReadHandle = IntPtr.Zero;
        _outputWriteHandle = IntPtr.Zero;
    }

    /// <summary>
    ///     Throws an ObjectDisposedException if the manager has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProcessManager));
        }
    }
}
