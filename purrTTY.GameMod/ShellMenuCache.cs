using purrTTY.Core.Terminal;
using purrTTY.Logging;

namespace purrTTY.GameMod;

/// <summary>
///     Process-lifetime snapshot of everything the New Tab / New Window menus need:
///     which shells are installed, the WSL distributions (Windows), and the
///     installed Unix shells (Linux/macOS). Detection — PATH scans, `wsl --list`,
///     /etc/shells — runs exactly once, on a background thread kicked off at mod
///     init. The menu draw path only ever reads <see cref="Current"/> and must
///     never trigger detection itself: a slow probe (wsl.exe service spin-up, a
///     dead network share in PATH) would hang the render thread.
/// </summary>
internal static class ShellMenuCache
{
    internal sealed class Snapshot
    {
        public required IReadOnlyList<(string Label, ShellType Type)> Entries { get; init; }
        public required IReadOnlyList<WslDistributionDetector.WslDistribution> WslDistributions { get; init; }
        public required IReadOnlyList<UnixShellDetector.UnixShell> UnixShells { get; init; }
    }

    private static volatile Snapshot? s_snapshot;
    private static int s_detectionStarted;

    /// <summary>The detected snapshot, or null while background detection is still running.</summary>
    public static Snapshot? Current => s_snapshot;

    /// <summary>
    ///     Kicks off shell detection on a background thread (idempotent; called once
    ///     from mod init). Until it completes the menus render a minimal fallback.
    /// </summary>
    public static void BeginDetection()
    {
        if (Interlocked.Exchange(ref s_detectionStarted, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                s_snapshot = Detect();
            }
            catch (Exception ex)
            {
                ModLog.Log.Debug($"purrTTY ShellMenuCache: shell detection failed: {ex.Message}");

                // Detection failing must not strand the player with only the
                // Game Console: Auto needs no detection and is the one shell
                // type valid on every platform.
                s_snapshot = new Snapshot
                {
                    Entries = [("Default Shell", ShellType.Auto), ("Game Console", ShellType.CustomGame)],
                    WslDistributions = [],
                    UnixShells = [],
                };
            }
        });
    }

    private static Snapshot Detect()
    {
        IReadOnlyList<WslDistributionDetector.WslDistribution> wslDistributions =
            OperatingSystem.IsWindows() ? WslDistributionDetector.GetInstalledDistributions() : [];
        IReadOnlyList<UnixShellDetector.UnixShell> unixShells =
            OperatingSystem.IsWindows() ? [] : UnixShellDetector.GetInstalledShells();

        var entries = new List<(string, ShellType)>();

        if (!OperatingSystem.IsWindows())
        {
            entries.Add(("Default Shell", ShellType.Auto));
        }

        foreach (var (shellType, label) in new[]
                 {
                     (ShellType.PowerShell, "PowerShell"),
                     (ShellType.PowerShellCore, "PowerShell Core"),
                     (ShellType.Cmd, "Command Prompt"),
                 })
        {
            if (ShellAvailabilityChecker.IsShellAvailable(shellType))
            {
                entries.Add((label, shellType));
            }
        }

        // wsl.exe ships with stock Windows even when WSL was never set up, so
        // executable presence is not evidence of a working WSL — offer the menu
        // entry only when at least one distribution actually exists.
        if (wslDistributions.Count > 0)
        {
            entries.Add(("WSL2", ShellType.Wsl));
        }

        entries.Add(("Game Console", ShellType.CustomGame));

        return new Snapshot
        {
            Entries = entries,
            WslDistributions = wslDistributions,
            UnixShells = unixShells,
        };
    }
}
