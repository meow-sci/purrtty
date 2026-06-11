using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     libc P/Invoke surface for the Unix PTY backend (Linux + macOS).
///     The PTY pair is created with <c>posix_openpt</c> and the child is launched
///     with <c>posix_spawnp</c> — deliberately fork-free: <c>fork()</c> from a large,
///     heavily-threaded game process risks allocator-lock deadlocks in the child and
///     ENOMEM under strict overcommit, both of which posix_spawn's vfork-style
///     implementation avoids.
/// </summary>
internal static class UnixPtyNative
{
    /// <summary>
    ///     Logical import name resolved by <see cref="Resolve"/>. "libc" alone does not
    ///     reliably dlopen on glibc systems (the real library is libc.so.6; libc.so is a
    ///     linker script from the dev package), so a DllImportResolver maps it explicitly.
    /// </summary>
    private const string Libc = "libc";

    private static bool s_resolverRegistered;

    // Same auto-registration pattern as Ghostty.Vt's NativeLibraryResolver: the
    // resolver must be installed before any libc P/Invoke in this assembly runs.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255",
        Justification = "Intentional: registers the libc DllImportResolver before first P/Invoke")]
    [ModuleInitializer]
    internal static void RegisterResolver()
    {
        if (s_resolverRegistered || OperatingSystem.IsWindows())
        {
            return;
        }

        s_resolverRegistered = true;
        NativeLibrary.SetDllImportResolver(typeof(UnixPtyNative).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Libc)
        {
            return IntPtr.Zero; // fall through to default probing (e.g. kernel32 on Windows)
        }

        string[] candidates = OperatingSystem.IsMacOS()
            ? ["/usr/lib/libSystem.B.dylib", "libSystem.B.dylib"]
            : ["libc.so.6", "libc.so", "libc"];

        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    // ---- constants (values differ between Linux/glibc and macOS where noted) ----

    internal const int O_RDWR = 0x2;
    internal static int O_NOCTTY => OperatingSystem.IsMacOS() ? 0x20000 : 0x100;

    /// <summary>TIOCSWINSZ ioctl request: 0x5414 on Linux, _IOW('t', 103, winsize) on macOS.</summary>
    internal static nuint TIOCSWINSZ => OperatingSystem.IsMacOS() ? 0x80087467 : 0x5414;

    internal const short POSIX_SPAWN_SETSIGDEF = 0x04;  // same value on glibc and macOS
    internal const short POSIX_SPAWN_SETSIGMASK = 0x08; // same value on glibc and macOS
    internal static short POSIX_SPAWN_SETSID => (short)(OperatingSystem.IsMacOS() ? 0x0400 : 0x80);

    internal const int SIGHUP = 1;
    internal const int SIGKILL = 9;

    internal const short POLLIN = 0x01;
    internal const short POLLERR = 0x08;
    internal const short POLLHUP = 0x10;
    internal const short POLLNVAL = 0x20; // same value on Linux and macOS

    internal const int EINTR = 4;
    internal const int EIO = 5;

    /// <summary>
    ///     Buffer sizes for the opaque posix_spawn types and sigset_t. Sized for the
    ///     largest implementation (glibc: attr ≈ 336 bytes, file_actions = 80 bytes,
    ///     sigset_t = 128 bytes; macOS uses a single pointer for the spawn types and a
    ///     4-byte sigset_t). The init functions construct in place, so an oversized
    ///     zeroed buffer is safe everywhere.
    /// </summary>
    internal const int SpawnAttrBufferSize = 512;
    internal const int FileActionsBufferSize = 128;
    internal const int SigsetBufferSize = 128;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }

    // ---- pty allocation ----

    [DllImport(Libc, SetLastError = true)]
    internal static extern int posix_openpt(int flags);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int grantpt(int fd);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int unlockpt(int fd);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int ptsname_r(int fd, byte[] buffer, nuint buflen);

    [DllImport(Libc, SetLastError = true)]
    private static extern int ioctl(int fd, nuint request, ref WinSize winSize);

    // ioctl(2) is variadic, and on Apple arm64 the variadic ABI passes anonymous
    // arguments on the STACK while a normal P/Invoke puts the third argument in x2 —
    // the kernel then reads a garbage winsize pointer. Eight named filler arguments
    // exhaust x0–x7 so the real pointer lands at sp, exactly where va_arg looks.
    // (Linux and macOS x64 keep variadic args in registers, so the plain form works.)
    [DllImport(Libc, EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl_appleArm64(
        int fd, nuint request, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, ref WinSize winSize);

    /// <summary>ioctl(fd, TIOCSWINSZ/TIOCGWINSZ) with the correct ABI per platform.</summary>
    internal static int WinSizeIoctl(int fd, nuint request, ref WinSize winSize)
        => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? ioctl_appleArm64(fd, request, 0, 0, 0, 0, 0, 0, ref winSize)
            : ioctl(fd, request, ref winSize);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int open(byte[] path, int flags);

    // ---- spawn ----
    // posix_spawn* functions return the error code directly (not via errno).

    [DllImport(Libc)]
    internal static extern int posix_spawn_file_actions_init(IntPtr fileActions);

    [DllImport(Libc)]
    internal static extern int posix_spawn_file_actions_destroy(IntPtr fileActions);

    [DllImport(Libc)]
    internal static extern int posix_spawn_file_actions_addopen(
        IntPtr fileActions, int fd, byte[] path, int oflag, int mode);

    [DllImport(Libc)]
    internal static extern int posix_spawn_file_actions_adddup2(IntPtr fileActions, int fd, int newFd);

    /// <summary>glibc 2.29+ / macOS 10.15+; caller must handle EntryPointNotFoundException.</summary>
    [DllImport(Libc)]
    internal static extern int posix_spawn_file_actions_addchdir_np(IntPtr fileActions, byte[] path);

    [DllImport(Libc)]
    internal static extern int posix_spawnattr_init(IntPtr attr);

    [DllImport(Libc)]
    internal static extern int posix_spawnattr_destroy(IntPtr attr);

    [DllImport(Libc)]
    internal static extern int posix_spawnattr_setflags(IntPtr attr, short flags);

    [DllImport(Libc)]
    internal static extern int posix_spawnattr_setsigdefault(IntPtr attr, byte[] sigset);

    [DllImport(Libc)]
    internal static extern int posix_spawnattr_setsigmask(IntPtr attr, byte[] sigset);

    [DllImport(Libc)]
    internal static extern int posix_spawnp(
        out int pid, byte[] file, IntPtr fileActions, IntPtr attr, IntPtr argv, IntPtr envp);

    // ---- I/O + lifecycle ----

    [DllImport(Libc, SetLastError = true)]
    internal static extern nint read(int fd, byte[] buffer, nuint count);

    [DllImport(Libc, SetLastError = true)]
    internal static extern unsafe nint write(int fd, byte* buffer, nuint count);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int poll(ref PollFd fds, nuint nfds, int timeoutMs);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int close(int fd);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int kill(int pid, int signal);

    [DllImport(Libc, SetLastError = true)]
    internal static extern int waitpid(int pid, out int status, int options);

    /// <summary>
    ///     Decodes a waitpid status into a shell-convention exit code:
    ///     WEXITSTATUS for a normal exit, 128 + signal number for a signaled death.
    ///     (The WIFEXITED/WEXITSTATUS bit layout is identical on Linux and macOS.)
    /// </summary>
    internal static int DecodeExitStatus(int status)
        => (status & 0x7F) == 0 ? (status >> 8) & 0xFF : 128 + (status & 0x7F);
}
