using caTTY.Core.Rpc;
using caTTY.Core.Rpc.Socket;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Terminal;

/// <summary>
///     Factory for creating and wiring terminal sessions.
/// </summary>
internal class TerminalSessionFactory
{
    /// <summary>
    ///     Creates a new terminal session with all necessary components and event subscriptions.
    /// </summary>
    /// <param name="sessionId">Unique identifier for the session</param>
    /// <param name="sessionTitle">Title for the session</param>
    /// <param name="initialWidth">Initial width in columns</param>
    /// <param name="initialHeight">Initial height in rows</param>
    /// <param name="onStateChanged">Event handler for session state changes</param>
    /// <param name="onTitleChanged">Event handler for session title changes</param>
    /// <param name="onProcessExited">Event handler for process exit</param>
    /// <param name="rpcHandler">Optional RPC handler for game integration (null disables RPC functionality)</param>
    /// <param name="oscRpcHandler">Optional OSC RPC handler for OSC-based RPC commands (null uses default no-op handler)</param>
    /// <param name="launchOptions">Optional launch options for the session (used to determine shell type)</param>
    /// <returns>A fully configured terminal session</returns>
    /// <exception cref="InvalidOperationException">Thrown if custom game shell cannot be created</exception>
    public static TerminalSession CreateSession(
        Guid sessionId,
        string sessionTitle,
        int initialWidth,
        int initialHeight,
        EventHandler<SessionStateChangedEventArgs> onStateChanged,
        EventHandler<SessionTitleChangedEventArgs> onTitleChanged,
        EventHandler<SessionProcessExitedEventArgs> onProcessExited,
        IRpcHandler? rpcHandler = null,
        IOscRpcHandler? oscRpcHandler = null,
        ProcessLaunchOptions? launchOptions = null)
    {
        var terminal = TerminalEmulator.Create(initialWidth, initialHeight, 2500, NullLogger.Instance, rpcHandler, oscRpcHandler);

        // Conditional process manager creation based on shell type
        IProcessManager processManager;
        Console.WriteLine($"TerminalSessionFactory.CreateSession: Launch options shell type: {launchOptions?.ShellType ?? ShellType.Auto}");

        if (launchOptions?.ShellType == ShellType.CustomGame)
        {
            Console.WriteLine($"TerminalSessionFactory.CreateSession: Creating CustomGame shell with ID: {launchOptions.CustomShellId}");

            if (string.IsNullOrEmpty(launchOptions.CustomShellId))
            {
                throw new InvalidOperationException("CustomGame shell type requires CustomShellId to be set");
            }

            try
            {
                var customShell = CustomShellRegistry.Instance.CreateShell(launchOptions.CustomShellId);
                if (customShell == null)
                {
                    throw new InvalidOperationException($"CustomShellRegistry.CreateShell returned null for shell ID: {launchOptions.CustomShellId}");
                }
                Console.WriteLine($"TerminalSessionFactory.CreateSession: Successfully created custom shell '{launchOptions.CustomShellId}'");
                processManager = new CustomShellPtyBridge(customShell);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TerminalSessionFactory.CreateSession: FAILED to create custom shell: {ex.Message}");
                throw new InvalidOperationException($"Failed to create custom game shell '{launchOptions.CustomShellId}': {ex.Message}", ex);
            }
        }
        else
        {
            Console.WriteLine($"TerminalSessionFactory.CreateSession: Creating standard ProcessManager for shell type: {launchOptions?.ShellType ?? ShellType.Auto}");
            processManager = new ProcessManager();
        }

        var session = new TerminalSession(sessionId, sessionTitle, terminal, processManager);

        // Wire up session events
        session.StateChanged += onStateChanged;
        session.TitleChanged += onTitleChanged;
        session.ProcessExited += onProcessExited;

        return session;
    }
}
