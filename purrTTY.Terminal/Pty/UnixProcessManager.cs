using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
/// </summary>
public class UnixProcessManager : IProcessManager
{
    private readonly object _stateLock = new();
    private readonly object _writeLock = new();
    private readonly ILogger _logger;

    private int _masterFd = -1;
    private int _pid;
    private bool _exited;
    private int? _exitCode;
    private bool _disposed;
    private Task? _readerTask;
    private Task? _waiterTask;
    private CancellationTokenSource? _readCancellationSource;

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

        lock (_stateLock)
        {
            if (_pid != 0)
            {
                throw new InvalidOperationException(
                    "A process is already running. Stop the current process before starting a new one.");
            }
        }

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
                options.InitialHeight);
        }
        catch (Exception ex) when (ex is not ProcessStartException)
        {
            throw new ProcessStartException($"Failed to start shell process: {ex.Message}", ex, shellPath);
        }

        _logger.LogDebug("Spawned '{Shell}' pid {Pid} on {Slave}", shellPath, spawn.Pid, spawn.SlavePath);

        CancellationToken readToken;
        lock (_stateLock)
        {
            _masterFd = spawn.MasterFd;
            _pid = spawn.Pid;
            _exited = false;
            _exitCode = null;
            _readCancellationSource = new CancellationTokenSource();
            readToken = _readCancellationSource.Token;
        }

        _readerTask = UnixPtyOutputPump.ReadOutputAsync(
            spawn.MasterFd,
            data => DataReceived?.Invoke(this, new DataReceivedEventArgs(data)),
            (exception, message) => ProcessError?.Invoke(this, new ProcessErrorEventArgs(exception, message, spawn.Pid)),
            readToken);

        _waiterTask = Task.Factory.StartNew(
            () => WaitForExit(spawn.Pid),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        return Task.CompletedTask;
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
        lock (_stateLock)
        {
            pid = _pid;
            running = _pid != 0 && !_exited;
            waiterTask = _waiterTask;
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
            CleanupProcess();
        }
    }

    /// <inheritdoc />
    public void Write(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();

        if (data.IsEmpty)
        {
            return;
        }

        int pid;
        lock (_stateLock)
        {
            if (_pid == 0 || _exited)
            {
                throw new InvalidOperationException("No process is running");
            }

            pid = _pid;
        }

        // _masterFd is re-read under _writeLock: the close in CleanupProcess holds
        // the same lock, so the fd can neither be stale nor recycled mid-write.
        lock (_writeLock)
        {
            if (_masterFd < 0)
            {
                throw new InvalidOperationException("No process is running");
            }

            WriteAll(_masterFd, data, pid);
        }
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

        lock (_stateLock)
        {
            if (_pid == 0 || _exited)
            {
                throw new InvalidOperationException("No process is running");
            }
        }

        lock (_writeLock)
        {
            if (_masterFd < 0)
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

            CleanupProcess();
            _disposed = true;
        }
    }

    /// <summary>
    ///     Dedicated waiter thread: reaps the child, lets the reader drain the PTY's
    ///     buffered tail, then cleans up and raises ProcessExited (mirroring the
    ///     cleanup-then-raise order of the ConPTY manager).
    /// </summary>
    private void WaitForExit(int pid)
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

        CleanupProcess();

        ProcessExited?.Invoke(this, new ProcessExitedEventArgs(exitCode, pid));
    }

    private void SignalProcessGroup(int pid, int signal)
    {
        if (UnixPtyNative.kill(-pid, signal) != 0)
        {
            _ = UnixPtyNative.kill(pid, signal);
        }
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
    ///     Idempotent teardown: stops the reader, then closes the master fd only
    ///     after the reader thread has exited (closing under a blocked read races
    ///     against fd reuse elsewhere in the process).
    /// </summary>
    private void CleanupProcess()
    {
        Task? readerTask;
        CancellationTokenSource? cts;
        lock (_stateLock)
        {
            cts = _readCancellationSource;
            _readCancellationSource = null;
            readerTask = _readerTask;
        }

        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            finally
            {
                cts.Dispose();
            }
        }

        if (readerTask != null && !readerTask.IsCompleted)
        {
            try
            {
                readerTask.Wait(1000);
            }
            catch
            {
                // reader errors are reported through ProcessError
            }
        }

        lock (_stateLock)
        {
            _readerTask = null;

            // _writeLock serializes the close against in-flight Write/Resize so a
            // recycled fd number can never be touched by a stale writer. A writer
            // blocked on a full pty buffer unblocks with EPIPE once the child dies
            // (which Stop ensures before cleanup).
            lock (_writeLock)
            {
                if (_masterFd >= 0)
                {
                    _ = UnixPtyNative.close(_masterFd);
                    _masterFd = -1;
                }
            }

            // Allow restart on the same manager (SessionManager.RestartSession
            // stops and re-starts the same instance). ExitCode is kept.
            _pid = 0;
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
