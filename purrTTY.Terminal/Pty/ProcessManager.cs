using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AttributeListBuilder = purrTTY.Core.Terminal.Process.AttributeListBuilder;
using ConPtyInputWriter = purrTTY.Core.Terminal.Process.ConPtyInputWriter;
using ConPtyNative = purrTTY.Core.Terminal.Process.ConPtyNative;
using ConPtyOutputPump = purrTTY.Core.Terminal.Process.ConPtyOutputPump;
using ConPtyPipeManager = purrTTY.Core.Terminal.Process.ConPtyPipeManager;
using ProcessCleanup = purrTTY.Core.Terminal.Process.ProcessCleanup;
using ProcessEvents = purrTTY.Core.Terminal.Process.ProcessEvents;
using ProcessLifecycleManager = purrTTY.Core.Terminal.Process.ProcessLifecycleManager;
using ProcessStateManager = purrTTY.Core.Terminal.Process.ProcessStateManager;
using PtyInputQueue = purrTTY.Core.Terminal.Process.PtyInputQueue;
using PtyTeardown = purrTTY.Core.Terminal.Process.PtyTeardown;
using ShellCommandResolver = purrTTY.Core.Terminal.Process.ShellCommandResolver;
using StartupInfoBuilder = purrTTY.Core.Terminal.Process.StartupInfoBuilder;
using SysProcess = System.Diagnostics.Process;

namespace purrTTY.Core.Terminal;

/// <summary>
///     Manages shell processes using Windows Pseudoconsole (ConPTY) for true PTY functionality.
///     Follows Microsoft's recommended approach from:
///     https://learn.microsoft.com/en-us/windows/console/creating-a-pseudoconsole-session
///
///     Threading model: all fields are guarded by <see cref="_processLock"/>, which is
///     never held across a blocking call. Input is written by a dedicated
///     <see cref="PtyInputQueue"/> thread (a blocked WriteFile must never run on the
///     render tick thread); the input write handle is closed only after that thread
///     has joined, so the writer can use its snapshot of the handle without holding a
///     lock across the blocking write. Each StartAsync gets a generation token so a
///     stale teardown (timed-out waiter, late Exited callback) can never destroy a
///     freshly restarted session.
/// </summary>
public class ProcessManager : IProcessManager
{
    private readonly object _processLock = new();
    private Process.ConPtyNative.COORD _currentSize;
    private bool _disposed;
    private bool _starting;
    private int _generation;
    private IntPtr _inputWriteHandle = IntPtr.Zero;
    private IntPtr _outputReadHandle = IntPtr.Zero;
    private Task? _outputReadTask;
    private SysProcess? _process;
    private EventHandler? _exitedHandler;
    private PtyInputQueue? _inputQueue;

    // Exit code retained across cleanup (CleanupProcess nulls _process before
    // consumers like the tab label read it). Mirrors UnixProcessManager's
    // _exitCode semantics: set on exit, reset on the next StartAsync.
    private int? _lastExitCode;

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
    public int? ExitCode
    {
        get
        {
            int? live = ProcessStateManager.GetExitCode(_process, _processLock);
            if (live is not null)
            {
                return live;
            }

            lock (_processLock)
            {
                return _lastExitCode;
            }
        }
    }

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
    ///     Start failure means the shell could not be resolved, created, or attached;
    ///     a shell that spawns and then dies promptly is NOT a start failure — it is
    ///     reported once, via <see cref="ProcessExited"/> with its real exit code,
    ///     exactly like <c>UnixProcessManager</c>. (The former post-spawn 100 ms
    ///     validation delay raced the Exited callback: a fast-dying shell
    ///     nondeterministically produced either ProcessStartException, ProcessExited,
    ///     or both — and every successful start paid the delay.)
    /// </summary>
    /// <param name="options">Launch options for the shell process</param>
    /// <param name="cancellationToken">Unused; process creation is synchronous</param>
    /// <returns>A task that completes when the process has started</returns>
    /// <exception cref="InvalidOperationException">Thrown if a process is already running</exception>
    /// <exception cref="ProcessStartException">Thrown if the process fails to start</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown on non-Windows platforms</exception>
    public Task StartAsync(ProcessLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ConPTY is only supported on Windows 10 version 1809 and later");
        }

        int generation;
        lock (_processLock)
        {
            if (_process != null || _starting)
            {
                throw new InvalidOperationException(
                    "A process is already running. Stop the current process before starting a new one.");
            }

            _starting = true;
            generation = ++_generation;
            _lastExitCode = null; // new process — the previous exit code no longer applies
        }

        try
        {
            // Store terminal size
            _currentSize = new ConPtyNative.COORD((short)options.InitialWidth, (short)options.InitialHeight);

            // Create communication pipes and pseudoconsole
            var pipeHandles = ConPtyPipeManager.CreatePipesAndPseudoConsole(_currentSize);
            lock (_processLock)
            {
                _inputWriteHandle = pipeHandles.InputWriteHandle;
                _outputReadHandle = pipeHandles.OutputReadHandle;
                _pseudoConsole = pipeHandles.PseudoConsole;
            }

            // Prepare startup information
            var startupInfo = StartupInfoBuilder.Create();

            // Initialize process thread attribute list with pseudoconsole
            startupInfo.lpAttributeList = AttributeListBuilder.CreateAttributeListWithPseudoConsole(pipeHandles.PseudoConsole);

            // Create environment block from launch options
            IntPtr envBlock = IntPtr.Zero;
            ConPtyNative.PROCESS_INFORMATION processInfo;
            try
            {
                try
                {
                    envBlock = ProcessLifecycleManager.CreateEnvironmentBlock(options.EnvironmentVariables);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create environment block, using parent environment");
                }

                // Resolve shell command (quoted per Windows argv rules — paths and
                // arguments containing spaces survive the child's re-tokenization)
                (_, string commandLine) = ShellCommandResolver.ResolveShellCommandLine(options);

                processInfo = ProcessLifecycleManager.CreateProcess(
                    commandLine,
                    options.WorkingDirectory ?? Environment.CurrentDirectory,
                    ref startupInfo,
                    envBlock);

                // Wrap the process handle in a Process object for lifecycle management.
                // The handler captures this start's generation so a late Exited callback
                // can never tear down a restarted session.
                var exitedHandler = new EventHandler((sender, e) => OnProcessExited(sender, e, generation));
                var process = ProcessLifecycleManager.WrapProcessHandle(processInfo, exitedHandler);

                // Capture the pid for error reports: the ProcessId property reads
                // the live Process, which is nulled by cleanup — a late pump/write
                // error would otherwise carry no pid (the Unix manager captures
                // spawn.Pid the same way).
                int childPid = processInfo.dwProcessId;

                lock (_processLock)
                {
                    _process = process;
                    _exitedHandler = exitedHandler;
                    _readCancellationSource = new CancellationTokenSource();
                    CancellationToken readToken = _readCancellationSource.Token;
                    _outputReadTask = ReadOutputAsync(readToken, childPid);
                    _inputQueue = new PtyInputQueue(
                        "purrTTY ConPTY input",
                        WriteChunkToPipe,
                        (exception, message) => OnProcessError(new ProcessErrorEventArgs(exception, message, childPid)));
                }

            }
            finally
            {
                if (envBlock != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(envBlock);
                }

                AttributeListBuilder.FreeAttributeList(startupInfo.lpAttributeList);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            CleanupProcess(generation);
            if (ex is ProcessStartException)
            {
                throw;
            }

            throw new ProcessStartException($"Failed to start shell process: {ex.Message}", ex);
        }
        finally
        {
            lock (_processLock)
            {
                _starting = false;
            }
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
        int generation;

        lock (_processLock)
        {
            processToStop = _process;
            cancellationSource = _readCancellationSource;
            readTask = _outputReadTask;
            generation = _generation;
        }

        try
        {
            await ProcessLifecycleManager.StopProcessGracefullyAsync(processToStop, cancellationSource, readTask);
        }
        finally
        {
            CleanupProcess(generation);
        }
    }

    /// <summary>
    ///     Queues data for the shell process stdin via ConPTY. The write itself happens
    ///     on a dedicated writer thread (a pty write can block indefinitely when the
    ///     child stops reading, and this is called on the render tick thread); write
    ///     failures surface through <see cref="ProcessError"/>.
    /// </summary>
    /// <param name="data">The data to write</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    public void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        if (data.IsEmpty)
        {
            return;
        }

        PtyInputQueue? queue;
        lock (_processLock)
        {
            if (_process == null || _inputWriteHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("No process is running");
            }

            queue = _inputQueue;
        }

        queue?.Write(data);
    }

    /// <summary>
    ///     Queues string data for the shell process stdin.
    /// </summary>
    /// <param name="text">The text to write (will be converted to UTF-8)</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Write(Encoding.UTF8.GetBytes(text));
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

        // The resize runs under the lock so teardown (which nulls _pseudoConsole
        // under the same lock before closing it) cannot close the handle mid-call.
        lock (_processLock)
        {
            ProcessStateManager.ValidateProcessForResize(_process, _pseudoConsole);

            var newSize = new ConPtyNative.COORD((short)width, (short)height);
            int result = ConPtyNative.ResizePseudoConsole(_pseudoConsole, newSize);

            if (result != 0)
            {
                throw new InvalidOperationException($"Failed to resize pseudoconsole: Win32 error {result}");
            }

            _currentSize = newSize;
        }
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

            int generation;
            lock (_processLock)
            {
                generation = _generation;
            }

            CleanupProcess(generation);
            _disposed = true;
        }
    }

    /// <summary>
    ///     Reads data from the ConPTY output pipe asynchronously and raises DataReceived events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="childPid">The child pid, captured at start for error reports</param>
    private Task ReadOutputAsync(CancellationToken cancellationToken, int childPid)
    {
        return ConPtyOutputPump.ReadOutputAsync(
            _outputReadHandle,
            data => OnDataReceived(new DataReceivedEventArgs(data)),
            (exception, message) => OnProcessError(new ProcessErrorEventArgs(exception, message, childPid)),
            cancellationToken);
    }

    /// <summary>
    ///     Writes one queued input chunk to the ConPTY input pipe. Runs on the input
    ///     queue's writer thread only. The handle snapshot is safe to use outside the
    ///     lock because teardown closes the handle only after this thread has joined.
    /// </summary>
    private void WriteChunkToPipe(byte[] chunk)
    {
        IntPtr handle;
        int? processId;
        lock (_processLock)
        {
            handle = _inputWriteHandle;
            processId = _process?.Id;
        }

        if (handle == IntPtr.Zero)
        {
            return; // torn down — drop
        }

        ConPtyInputWriter.WriteAll(handle, chunk, processId);
    }

    /// <summary>
    ///     Handles process exit events for the start that installed this handler.
    /// </summary>
    private void OnProcessExited(object? sender, EventArgs e, int generation)
    {
        // Gate the whole handler on the generation, not just the cleanup:
        // detaching the Exited handler cannot recall an in-flight invocation,
        // so after a restart a late callback would read the disposed previous
        // process (snapshot degrades to 0/0) and raise a bogus ProcessExited
        // against the fresh session. Mirrors UnixProcessManager.WaitForExit.
        lock (_processLock)
        {
            if (generation != _generation)
            {
                return;
            }
        }

        ProcessEvents.HandleProcessExited(
            sender,
            e,
            () => CleanupProcess(generation),
            ProcessExited,
            this,
            (exitCode, _) =>
            {
                lock (_processLock)
                {
                    _lastExitCode = exitCode;
                }
            });
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
    ///     Cleans up process resources including ConPTY handles. No-ops if
    ///     <paramref name="generation"/> is stale (a restart superseded it).
    ///     Teardown order matters: close the pseudoconsole FIRST — conhost flushes its
    ///     remaining output and breaks both pipes, which ends the output pump without
    ///     yanking the read handle out from under a blocked ReadFile and unblocks a
    ///     writer stuck on a full input pipe — then drain the pump, join the writer,
    ///     and only then close the pipe handles.
    /// </summary>
    private void CleanupProcess(int generation)
    {
        Task? readTask;
        PtyInputQueue? inputQueue;
        IntPtr pseudoConsole;
        IntPtr inputWrite;
        IntPtr outputRead;

        CancellationTokenSource? cts;
        lock (_processLock)
        {
            if (generation != _generation)
            {
                return; // stale teardown (timed-out waiter / late exit callback) after a restart
            }

            cts = _readCancellationSource;
            _readCancellationSource = null;

            ProcessCleanup.CleanupProcess(_process, _exitedHandler);
            _process = null;
            _exitedHandler = null;

            readTask = _outputReadTask;
            _outputReadTask = null;

            inputQueue = _inputQueue;
            _inputQueue = null;

            pseudoConsole = _pseudoConsole;
            _pseudoConsole = IntPtr.Zero;
            inputWrite = _inputWriteHandle;
            _inputWriteHandle = IntPtr.Zero;
            outputRead = _outputReadHandle;
            _outputReadHandle = IntPtr.Zero;
        }

        // Cancel + dispose outside the lock, exactly like the Unix manager: a
        // CTS callback must never run while _processLock is held.
        PtyTeardown.CancelAndDispose(cts);

        if (pseudoConsole != IntPtr.Zero)
        {
            ConPtyNative.ClosePseudoConsole(pseudoConsole);
        }

        // Drain: let the pump deliver the tail output (final prompt, a short
        // command's entire result) before its handle goes away.
        bool readerExited = PtyTeardown.WaitForPump(readTask, 2000);
        bool writerExited = PtyTeardown.ShutdownWriter(inputQueue, 2000);

        // Close a handle only when its pump thread is provably out of it; otherwise
        // leak it — closing a handle another thread is blocked on races Win32 handle
        // recycling (the write could land in an unrelated kernel object).
        if (readerExited)
        {
            if (outputRead != IntPtr.Zero)
            {
                ConPtyNative.CloseHandle(outputRead);
            }
        }
        else
        {
            _logger.LogWarning("ConPTY output pump did not exit in time; leaking the output read handle");
        }

        if (writerExited)
        {
            if (inputWrite != IntPtr.Zero)
            {
                ConPtyNative.CloseHandle(inputWrite);
            }
        }
        else
        {
            _logger.LogWarning("ConPTY input writer did not exit in time; leaking the input write handle");
        }
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
