using Microsoft.Extensions.Logging;
using PurrTTY.Terminal.Ghostty;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Sessions;

/// <summary>
/// Builds and wires terminal sessions. This is the construction seam: it
/// creates a libghostty-vt-backed <see cref="GhosttyTerminalSurface"/> (instead
/// of the legacy emulator) plus the appropriate <see cref="IProcessManager"/>
/// (ConPTY on Windows, POSIX pty on Linux/macOS, or custom game shell), then
/// assembles a <see cref="TerminalSession"/>.
/// </summary>
public static class TerminalSessionFactory
{
    public static TerminalSession CreateSession(
        Guid sessionId,
        string sessionTitle,
        int initialWidth,
        int initialHeight,
        EventHandler<SessionStateChangedEventArgs> onStateChanged,
        EventHandler<SessionTitleChangedEventArgs> onTitleChanged,
        EventHandler<SessionProcessExitedEventArgs> onProcessExited,
        ProcessLaunchOptions? launchOptions = null,
        ILogger? logger = null)
    {
        var surface = new GhosttyTerminalSurface(initialWidth, initialHeight, logger);

        // The surface owns native handles; if any later construction step throws
        // (e.g. an unknown custom-shell ID), dispose it instead of leaking it.
        IProcessManager processManager;
        try
        {
            processManager = CreateProcessManager(launchOptions, logger);
        }
        catch
        {
            surface.Dispose();
            throw;
        }

        TerminalSession session;
        try
        {
            session = new TerminalSession(sessionId, sessionTitle, surface, processManager, logger);
        }
        catch
        {
            surface.Dispose();
            processManager.Dispose();
            throw;
        }

        session.StateChanged += onStateChanged;
        session.TitleChanged += onTitleChanged;
        session.ProcessExited += onProcessExited;
        return session;
    }

    private static IProcessManager CreateProcessManager(ProcessLaunchOptions? launchOptions, ILogger? logger)
    {
        if (launchOptions?.ShellType != ShellType.CustomGame)
        {
            // ConPTY on Windows; POSIX pty (posix_openpt + posix_spawnp) elsewhere.
            return OperatingSystem.IsWindows()
                ? new ProcessManager(logger)
                : new UnixProcessManager(logger);
        }

        if (string.IsNullOrEmpty(launchOptions.CustomShellId))
        {
            throw new InvalidOperationException("CustomGame shell type requires CustomShellId to be set.");
        }

        try
        {
            var customShell = CustomShellRegistry.Instance.CreateShell(launchOptions.CustomShellId)
                ?? throw new InvalidOperationException(
                    $"CustomShellRegistry.CreateShell returned null for shell ID: {launchOptions.CustomShellId}");
            return new CustomShellPtyBridge(customShell);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to create custom game shell '{launchOptions.CustomShellId}': {ex.Message}", ex);
        }
    }
}
