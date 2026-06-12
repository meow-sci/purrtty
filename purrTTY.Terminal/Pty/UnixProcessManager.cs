using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PtyInputQueue = purrTTY.Core.Terminal.Process.PtyInputQueue;
using PtyTeardown = purrTTY.Core.Terminal.Process.PtyTeardown;
using ShellCommandResolver = purrTTY.Core.Terminal.Process.ShellCommandResolver;
using UnixPtyNative = purrTTY.Core.Terminal.Process.UnixPtyNative;
using UnixPtyOutputPump = purrTTY.Core.Terminal.Process.UnixPtyOutputPump;
using UnixPtySpawner = purrTTY.Core.Terminal.Process.UnixPtySpawner;

namespace purrTTY.Core.Terminal;

/// <summary>
///     Manages shell processes on Linux/macOS using a POSIX pseudo-terminal
///     (posix_openpt + posix_spawnp). The Unix counterpart of the ConPTY-based
///     <see cref="ProcessManager"/>; selected by <c>TerminalSessionFactory</c> on
///     non-Windows hosts.
///
///     Threading model: all fields are guarded by <see cref="_stateLock"/>, which is
///     never held across a blocking call. Input is written by a dedicated
///     <see cref="PtyInputQueue"/> thread — write(2) on a pty master blocks while the
///     child's input queue is full (busy/SIGSTOPped/XOFF'd child + a large paste), and
///     Write is called on the render tick thread. The master fd is closed only after
///     both the reader and the writer thread have provably stopped using it; if either
///     fails to stop in time the fd is deliberately leaked instead (closing an fd
///     under a blocked syscall races fd reuse elsewhere in the process). Each start
///     gets a generation token so a stale teardown (timed-out waiter from a previous
///     run) can never destroy a freshly restarted session.
/// </summary>
public class UnixProcessManager : IProcessManager
{
    private readonly object _stateLock = new();
    private readonly ILogger _logger;

    private int _masterFd = -1;
    private int _pid;
    private bool _exited;
    private int? _exitCode;
    private bool _disposed;
    private bool _starting;
    private int _generation;
    private Task? _readerTask;
    private Task? _waiterTask;
    private CancellationTokenSource? _readCancellationSource;
    private PtyInputQueue? _inputQueue;

    /// <summary>
    ///     Creates a new UnixProcessManager with optional logging.
    /// </summary>
    /// <param name="logger">Logger for diagnostics (optional)</param>
    public UnixProcessManager(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _pid != 0 && !_exited;
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
                return _pid != 0 ? _pid : null;
            }
        }
    }

    /// <inheritdoc />
    public int? ExitCode
    {
        get
        {
            lock (_stateLock)
            {
                return _exitCode;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<DataReceivedEventArgs>? DataReceived;

    /// <inheritdoc />
    public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

    /// <inheritdoc />
    public event EventHandler<ProcessErrorEventArgs>? ProcessError;

    /// <summary>
    ///     Starts a new shell process attached to a fresh PTY.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if a process is already running</exception>
    /// <exception cref="ProcessStartException">Thrown if the process fails to start</exception>
    /// <exception cref="PlatformNotSupportedException">Thrown on Windows (use <see cref="ProcessManager"/>)</exception>
    public Task StartAsync(ProcessLaunchOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "UnixProcessManager only supports Linux/macOS; use ProcessManager (ConPTY) on Windows");
        }

        int generation;
        lock (_stateLock)
        {
            if (_pid != 0 || _starting)
            {
                throw new InvalidOperationException(
                    "A process is already running. Stop the current process before starting a new one.");
            }

            _starting = true;
            generation = ++_generation;
        }

        try
        {
            (string shellPath, string[] argv) = ShellCommandResolver.ResolveShellCommandArgv(options);

            string? workingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory;

            UnixPtySpawner.SpawnResult spawn;
            try
            {
                spawn = UnixPtySpawner.Spawn(
                    shellPath,
                    argv,
                    workingDirectory,
                    options.EnvironmentVariables,
                    options.InitialWidth,
                    options.InitialHeight,
                    _logger);
            }
            catch (Exception ex) when (ex is not ProcessStartException)
            {
                throw new ProcessStartException($"Failed to start shell process: {ex.Message}", ex, shellPath);
            }

            _logger.LogDebug("Spawned '{Shell}' pid {Pid} on {Slave}", shellPath, spawn.Pid, spawn.SlavePath);

            lock (_stateLock)
            {
                _masterFd = spawn.MasterFd;
                _pid = spawn.Pid;
                _exited = false;
                _exitCode = null;
                _readCancellationSource = new CancellationTokenSource();
                _inputQueue = new PtyInputQueue(
                    "purrTTY pty input",
                    chunk => WriteChunkToPty(chunk, spawn.Pid),
                    (exception, message) => ProcessError?.Invoke(this, new ProcessErrorEventArgs(exception, message, spawn.Pid)));

                _readerTask = UnixPtyOutputPump.ReadOutputAsync(
                    spawn.MasterFd,
                    data => DataReceived?.Invoke(this, new DataReceivedEventArgs(data)),
                    (exception, message) => ProcessError?.Invoke(this, new ProcessErrorEventArgs(exception, message, spawn.Pid)),
                    _readCancellationSource.Token);

                _waiterTask = Task.Factory.StartNew(
                    () => WaitForExit(spawn.Pid, generation),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }

            return Task.CompletedTask;
        }
        finally
        {
            lock (_stateLock)
            {
                _starting = false;
            }
        }
    }

    /// <summary>
    ///     Stops the running shell: SIGHUP to the child's session group, escalating
    ///     to SIGKILL if it does not exit promptly.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        int pid;
        bool running;
        Task? waiterTask;
        int generation;
        lock (_stateLock)
        {
            pid = _pid;
            running = _pid != 0 && !_exited;
            waiterTask = _waiterTask;
            generation = _generation;
        }

        try
        {
            if (running && pid != 0)
            {
                // The child was made a session leader (SETSID), so its pgid == pid and
                // a negative pid signals the whole group.
                SignalProcessGroup(pid, UnixPtyNative.SIGHUP);

                if (waiterTask != null && await Task.WhenAny(waiterTask, Task.Delay(2000, cancellationToken)) != waiterTask)
                {
                    SignalProcessGroup(pid, UnixPtyNative.SIGKILL);
                    if (waiterTask != null)
                    {
                        await Task.WhenAny(waiterTask, Task.Delay(2000, cancellationToken));
                    }
                }
            }
        }
        finally
        {
            CleanupProcess(generation);
        }
    }

    /// <summary>
    ///     Queues data for the shell's PTY. The write itself happens on a dedicated
    ///     writer thread (write(2) on the master can block indefinitely while the
    ///     child is not reading, and this is called on the render tick thread); write
    ///     failures surface through <see cref="ProcessError"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if no process is running</exception>
    public void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        if (data.IsEmpty)
        {
            return;
        }

        PtyInputQueue? queue;
        lock (_stateLock)
        {
            if (_pid == 0 || _exited)
            {
                throw new InvalidOperationException("No process is running");
            }

            queue = _inputQueue;
        }

        queue?.Write(data);
    }

    /// <inheritdoc />
    public void Write(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    ///     Resizes the PTY. The kernel delivers SIGWINCH to the foreground process
    ///     group automatically on TIOCSWINSZ.
    /// </summary>
    public void Resize(int width, int height)
    {
        ThrowIfDisposed();

        // The ioctl runs under the lock (it never blocks) so teardown — which
        // invalidates _masterFd under the same lock before closing it — cannot
        // close the fd mid-call.
        lock (_stateLock)
        {
            if (_pid == 0 || _exited || _masterFd < 0)
            {
                throw new InvalidOperationException("No process is running");
            }

            var winSize = new UnixPtyNative.WinSize { ws_row = (ushort)height, ws_col = (ushort)width };
            if (UnixPtyNative.WinSizeIoctl(_masterFd, UnixPtyNative.TIOCSWINSZ, ref winSize) != 0)
            {
                throw new InvalidOperationException(
                    $"Failed to resize PTY: {Marshal.GetLastPInvokeErrorMessage()}");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                StopAsync().Wait(5000);
            }
            catch
            {
                // Ignore errors during disposal
            }

            int generation;
            lock (_stateLock)
            {
                generation = _generation;
            }

            CleanupProcess(generation);
            _disposed = true;
        }
    }

    /// <summary>
    ///     Dedicated waiter thread: reaps the child, lets the reader drain the PTY's
    ///     buffered tail, then cleans up and raises ProcessExited (mirroring the
    ///     cleanup-then-raise order of the ConPTY manager). A stale waiter (this
    ///     manager restarted while it was still waiting) must touch nothing: the
    ///     generation check makes both the state update and the cleanup no-ops.
    /// </summary>
    private void WaitForExit(int pid, int generation)
    {
        int status = 0;
        int result;
        do
        {
            result = UnixPtyNative.waitpid(pid, out status, 0);
        } while (result < 0 && Marshal.GetLastPInvokeError() == UnixPtyNative.EINTR);

        int exitCode = result == pid ? UnixPtyNative.DecodeExitStatus(status) : 0;

        Task? readerTask;
        lock (_stateLock)
        {
            if (generation != _generation)
            {
                _logger.LogDebug("Stale waiter for pid {Pid} superseded by restart; ignoring exit", pid);
                return;
            }

            _exited = true;
            _exitCode = exitCode;
            readerTask = _readerTask;
        }

        // Normal case: the dead child held the only slave fds, so the reader hits
        // EOF/EIO by itself once the buffered output is delivered. The timeout only
        // matters when an orphaned grandchild still holds the slave open.
        try
        {
            readerTask?.Wait(2000);
        }
        catch
        {
            // reader errors are reported through ProcessError
        }

        CleanupProcess(generation);

        ProcessExited?.Invoke(this, new ProcessExitedEventArgs(exitCode, pid));
    }

    private void SignalProcessGroup(int pid, int signal)
    {
        if (UnixPtyNative.kill(-pid, signal) != 0)
        {
            _ = UnixPtyNative.kill(pid, signal);
        }
    }

    /// <summary>
    ///     Writes one queued input chunk to the master fd. Runs on the input queue's
    ///     writer thread only. The fd snapshot is safe to use outside the lock because
    ///     teardown closes the fd only after this thread has joined (or leaks it).
    /// </summary>
    private void WriteChunkToPty(byte[] chunk, int pid)
    {
        int fd;
        lock (_stateLock)
        {
            fd = _masterFd;
        }

        if (fd < 0)
        {
            return; // torn down — drop
        }

        WriteAll(fd, chunk, pid);
    }

    private static void WriteAll(int fd, ReadOnlySpan<byte> data, int pid)
    {
        unsafe
        {
            fixed (byte* basePtr = data)
            {
                int offset = 0;
                while (offset < data.Length)
                {
                    nint written = UnixPtyNative.write(fd, basePtr + offset, (nuint)(data.Length - offset));
                    if (written < 0)
                    {
                        int error = Marshal.GetLastPInvokeError();
                        if (error == UnixPtyNative.EINTR)
                        {
                            continue;
                        }

                        throw new ProcessWriteException($"Failed to write to PTY: errno {error}", pid);
                    }

                    offset += (int)written;
                }
            }
        }
    }

    /// <summary>
    ///     Idempotent, generation-guarded teardown: stops the reader and the input
    ///     writer, then closes the master fd only once both threads have provably
    ///     stopped using it. If either thread fails to stop in time, the single fd is
    ///     deliberately leaked — closing it under a blocked read/write races fd reuse
    ///     elsewhere in the process, which is far worse than one lost descriptor.
    /// </summary>
    private void CleanupProcess(int generation)
    {
        Task? readerTask;
        CancellationTokenSource? cts;
        PtyInputQueue? inputQueue;
        int masterFd;

        lock (_stateLock)
        {
            if (generation != _generation)
            {
                return; // stale teardown after a restart
            }

            cts = _readCancellationSource;
            _readCancellationSource = null;
            readerTask = _readerTask;
            _readerTask = null;
            inputQueue = _inputQueue;
            _inputQueue = null;

            // Invalidate the fd for new callers; the close happens below, after the
            // pump threads are out of it.
            masterFd = _masterFd;
            _masterFd = -1;

            // Allow restart on the same manager (SessionManager.RestartSession
            // stops and re-starts the same instance). ExitCode is kept.
            _pid = 0;
        }

        // A CTS callback must never run while _stateLock is held.
        PtyTeardown.CancelAndDispose(cts);

        // The reader blocks in poll(100ms), so cancellation is observed promptly;
        // the writer unblocks with EPIPE once the child is dead (Stop ensures that
        // before cleanup).
        bool readerExited = PtyTeardown.WaitForPump(readerTask, 1000);
        bool writerExited = PtyTeardown.ShutdownWriter(inputQueue, 1000);

        if (masterFd >= 0)
        {
            if (readerExited && writerExited)
            {
                _ = UnixPtyNative.close(masterFd);
            }
            else
            {
                _logger.LogWarning(
                    "PTY pump thread still running at cleanup (reader exited: {Reader}, writer exited: {Writer}); leaking master fd {Fd}",
                    readerExited, writerExited, masterFd);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(UnixProcessManager));
        }
    }
}
