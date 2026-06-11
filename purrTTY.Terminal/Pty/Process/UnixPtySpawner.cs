using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Allocates a PTY pair and spawns the shell attached to it (Linux + macOS).
///     The child is created with posix_spawnp using POSIX_SPAWN_SETSID plus an
///     addopen of the slave onto stdin: a fresh session leader opening a tty
///     acquires it as the controlling terminal on both Linux and BSD/macOS, so the
///     child gets full job control without any fork/ioctl dance in managed code.
///     Signal dispositions are reset to default in the child (the .NET runtime
///     ignores SIGPIPE process-wide and SIG_IGN survives exec, which would break
///     pipelines inside the spawned shell).
/// </summary>
internal static class UnixPtySpawner
{
    internal readonly record struct SpawnResult(int MasterFd, int Pid, string SlavePath);

    /// <summary>
    ///     Creates the PTY (sized to <paramref name="width"/>×<paramref name="height"/>)
    ///     and spawns <paramref name="shellPath"/> on it.
    /// </summary>
    /// <exception cref="ProcessStartException">PTY allocation or spawn failed</exception>
    internal static SpawnResult Spawn(
        string shellPath,
        string[] arguments,
        string? workingDirectory,
        IDictionary<string, string>? environmentOverrides,
        int width,
        int height,
        ILogger? logger = null)
    {
        int masterFd = UnixPtyNative.posix_openpt(UnixPtyNative.O_RDWR | UnixPtyNative.O_NOCTTY);
        if (masterFd < 0)
        {
            throw new ProcessStartException(
                $"posix_openpt failed: {Marshal.GetLastPInvokeErrorMessage()}", shellPath);
        }

        try
        {
            if (UnixPtyNative.grantpt(masterFd) != 0)
            {
                throw new ProcessStartException(
                    $"grantpt failed: {Marshal.GetLastPInvokeErrorMessage()}", shellPath);
            }

            if (UnixPtyNative.unlockpt(masterFd) != 0)
            {
                throw new ProcessStartException(
                    $"unlockpt failed: {Marshal.GetLastPInvokeErrorMessage()}", shellPath);
            }

            var nameBuffer = new byte[256];
            if (UnixPtyNative.ptsname_r(masterFd, nameBuffer, (nuint)nameBuffer.Length) != 0)
            {
                throw new ProcessStartException(
                    $"ptsname_r failed: {Marshal.GetLastPInvokeErrorMessage()}", shellPath);
            }

            int nameLength = Array.IndexOf(nameBuffer, (byte)0);
            string slavePath = Encoding.UTF8.GetString(nameBuffer, 0, nameLength < 0 ? nameBuffer.Length : nameLength);

            // Size the pty before the child runs so the shell sees the real grid
            // dimensions from its very first ioctl(TIOCGWINSZ). The winsize must be
            // set on a *slave* fd: set on the master before the slave ever opens, it
            // does not stick (openpty() applies it the same way). O_NOCTTY keeps the
            // game process from adopting the tty; the fd is closed right after spawn
            // (posix_spawn returns only after the child's file actions ran, so the
            // child already holds its own slave fds and master EOF still tracks it).
            int slaveFd = -1;
            try
            {
                slaveFd = OpenSlave(slavePath, shellPath);
                var winSize = new UnixPtyNative.WinSize { ws_row = (ushort)height, ws_col = (ushort)width };
                if (UnixPtyNative.WinSizeIoctl(slaveFd, UnixPtyNative.TIOCSWINSZ, ref winSize) != 0)
                {
                    // Not fatal — the shell just starts with default dimensions until
                    // the first Resize — but it must not fail silently.
                    logger?.LogWarning(
                        "Initial TIOCSWINSZ on {Slave} failed: {Error}",
                        slavePath, Marshal.GetLastPInvokeErrorMessage());
                }

                int pid = SpawnChild(shellPath, arguments, workingDirectory, environmentOverrides, slavePath);
                return new SpawnResult(masterFd, pid, slavePath);
            }
            finally
            {
                if (slaveFd >= 0)
                {
                    UnixPtyNative.close(slaveFd);
                }
            }
        }
        catch
        {
            UnixPtyNative.close(masterFd);
            throw;
        }
    }

    private static int SpawnChild(
        string shellPath,
        string[] arguments,
        string? workingDirectory,
        IDictionary<string, string>? environmentOverrides,
        string slavePath)
    {
        var fileActionsBuffer = new byte[UnixPtyNative.FileActionsBufferSize];
        var attrBuffer = new byte[UnixPtyNative.SpawnAttrBufferSize];
        var fileActionsHandle = GCHandle.Alloc(fileActionsBuffer, GCHandleType.Pinned);
        var attrHandle = GCHandle.Alloc(attrBuffer, GCHandleType.Pinned);
        IntPtr fileActions = fileActionsHandle.AddrOfPinnedObject();
        IntPtr attr = attrHandle.AddrOfPinnedObject();
        bool fileActionsInit = false;
        bool attrInit = false;

        var nativeStrings = new List<IntPtr>();
        var argvHandle = default(GCHandle);
        var envpHandle = default(GCHandle);

        try
        {
            ThrowIfSpawnError(UnixPtyNative.posix_spawn_file_actions_init(fileActions),
                "posix_spawn_file_actions_init", shellPath);
            fileActionsInit = true;

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
            {
                try
                {
                    ThrowIfSpawnError(
                        UnixPtyNative.posix_spawn_file_actions_addchdir_np(fileActions, NullTerminated(workingDirectory)),
                        "posix_spawn_file_actions_addchdir_np", shellPath);
                }
                catch (EntryPointNotFoundException)
                {
                    // glibc < 2.29: the shell starts in the game's working directory instead.
                }
            }

            // Child: setsid (new session) + open slave as fd 0 → controlling tty, then
            // mirror onto stdout/stderr. The parent never opens the slave, so the only
            // slave fds belong to the child and EOF/EIO on the master tracks its exit.
            ThrowIfSpawnError(
                UnixPtyNative.posix_spawn_file_actions_addopen(fileActions, 0, NullTerminated(slavePath), UnixPtyNative.O_RDWR, 0),
                "posix_spawn_file_actions_addopen", shellPath);
            ThrowIfSpawnError(UnixPtyNative.posix_spawn_file_actions_adddup2(fileActions, 0, 1),
                "posix_spawn_file_actions_adddup2", shellPath);
            ThrowIfSpawnError(UnixPtyNative.posix_spawn_file_actions_adddup2(fileActions, 0, 2),
                "posix_spawn_file_actions_adddup2", shellPath);

            ThrowIfSpawnError(UnixPtyNative.posix_spawnattr_init(attr), "posix_spawnattr_init", shellPath);
            attrInit = true;

            var emptySigset = new byte[UnixPtyNative.SigsetBufferSize];
            var fullSigset = new byte[UnixPtyNative.SigsetBufferSize];
            Array.Fill(fullSigset, (byte)0xFF);
            ThrowIfSpawnError(UnixPtyNative.posix_spawnattr_setsigmask(attr, emptySigset),
                "posix_spawnattr_setsigmask", shellPath);
            ThrowIfSpawnError(UnixPtyNative.posix_spawnattr_setsigdefault(attr, fullSigset),
                "posix_spawnattr_setsigdefault", shellPath);
            ThrowIfSpawnError(
                UnixPtyNative.posix_spawnattr_setflags(attr, (short)(
                    UnixPtyNative.POSIX_SPAWN_SETSID |
                    UnixPtyNative.POSIX_SPAWN_SETSIGDEF |
                    UnixPtyNative.POSIX_SPAWN_SETSIGMASK)),
                "posix_spawnattr_setflags", shellPath);

            IntPtr argv = BuildNativeStringArray(
                [shellPath, .. arguments], nativeStrings, out argvHandle);
            IntPtr envp = BuildNativeStringArray(
                BuildEnvironment(environmentOverrides, shellPath), nativeStrings, out envpHandle);

            int rc = UnixPtyNative.posix_spawnp(out int pid, NullTerminated(shellPath), fileActions, attr, argv, envp);
            if (rc != 0)
            {
                throw new ProcessStartException(
                    $"posix_spawnp('{shellPath}') failed: {Marshal.GetPInvokeErrorMessage(rc)}", shellPath);
            }

            return pid;
        }
        finally
        {
            if (fileActionsInit)
            {
                _ = UnixPtyNative.posix_spawn_file_actions_destroy(fileActions);
            }

            if (attrInit)
            {
                _ = UnixPtyNative.posix_spawnattr_destroy(attr);
            }

            fileActionsHandle.Free();
            attrHandle.Free();

            if (argvHandle.IsAllocated)
            {
                argvHandle.Free();
            }

            if (envpHandle.IsAllocated)
            {
                envpHandle.Free();
            }

            foreach (IntPtr ptr in nativeStrings)
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
    }

    private static int OpenSlave(string slavePath, string shellPath)
    {
        int fd = UnixPtyNative.open(NullTerminated(slavePath), UnixPtyNative.O_RDWR | UnixPtyNative.O_NOCTTY);
        if (fd < 0)
        {
            throw new ProcessStartException(
                $"open('{slavePath}') failed: {Marshal.GetLastPInvokeErrorMessage()}", shellPath);
        }

        return fd;
    }

    private static void ThrowIfSpawnError(int rc, string operation, string shellPath)
    {
        if (rc != 0)
        {
            throw new ProcessStartException(
                $"{operation} failed: {Marshal.GetPInvokeErrorMessage(rc)}", shellPath);
        }
    }

    private static byte[] NullTerminated(string value) => Encoding.UTF8.GetBytes(value + "\0");

    /// <summary>
    ///     Builds a NULL-terminated char** from managed strings. The individual UTF-8
    ///     strings are tracked in <paramref name="nativeStrings"/> for caller cleanup;
    ///     the pointer array itself is a pinned managed array.
    /// </summary>
    private static IntPtr BuildNativeStringArray(
        IReadOnlyList<string> values, List<IntPtr> nativeStrings, out GCHandle pinHandle)
    {
        var pointers = new IntPtr[values.Count + 1];
        for (int i = 0; i < values.Count; i++)
        {
            pointers[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
            nativeStrings.Add(pointers[i]);
        }

        pointers[values.Count] = IntPtr.Zero;
        pinHandle = GCHandle.Alloc(pointers, GCHandleType.Pinned);
        return pinHandle.AddrOfPinnedObject();
    }

    private static readonly HashSet<string> KnownShellNames = new(StringComparer.Ordinal)
    {
        "sh", "bash", "zsh", "fish", "dash", "ksh", "csh", "tcsh", "ash", "nu", "elvish", "pwsh",
    };

    /// <summary>
    ///     Parent environment merged with the launch overrides, in "NAME=VALUE" form.
    ///     SHELL is pointed at the launched program when it is a recognized shell (and
    ///     not explicitly overridden), matching what desktop terminal emulators do.
    /// </summary>
    private static List<string> BuildEnvironment(IDictionary<string, string>? overrides, string shellPath)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && key.Length > 0)
            {
                env[key] = entry.Value?.ToString() ?? string.Empty;
            }
        }

        if (overrides != null)
        {
            foreach ((string key, string value) in overrides)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    env[key] = value ?? string.Empty;
                }
            }
        }

        if (overrides?.ContainsKey("SHELL") != true && KnownShellNames.Contains(Path.GetFileName(shellPath)))
        {
            env["SHELL"] = shellPath;
        }

        var result = new List<string>(env.Count);
        foreach ((string key, string value) in env)
        {
            result.Add($"{key}={value}");
        }

        return result;
    }
}
