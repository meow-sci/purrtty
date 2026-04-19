using caTTY.Core.Rpc;
using caTTY.Core.Rpc.Socket;

namespace caTTY.Core.Terminal;

/// <summary>
///     Handles the creation and initialization of terminal sessions.
/// </summary>
internal class SessionCreator
{
    /// <summary>
    ///     Creates and initializes a new terminal session.
    /// </summary>
    /// <param name="sessionId">Unique identifier for the session</param>
    /// <param name="sessionTitle">Title for the session</param>
    /// <param name="launchOptions">Launch options for the session</param>
    /// <param name="onStateChanged">Event handler for session state changes</param>
    /// <param name="onTitleChanged">Event handler for session title changes</param>
    /// <param name="onProcessExited">Event handler for process exit</param>
    /// <param name="rpcHandler">Optional RPC handler for CSI RPC commands</param>
    /// <param name="oscRpcHandler">Optional OSC RPC handler for OSC-based RPC commands</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The created and initialized session</returns>
    public static async Task<TerminalSession> CreateSessionAsync(
        Guid sessionId,
        string sessionTitle,
        ProcessLaunchOptions launchOptions,
        EventHandler<SessionStateChangedEventArgs> onStateChanged,
        EventHandler<SessionTitleChangedEventArgs> onTitleChanged,
        EventHandler<SessionProcessExitedEventArgs> onProcessExited,
        IRpcHandler? rpcHandler,
        IOscRpcHandler? oscRpcHandler,
        CancellationToken cancellationToken)
    {
        var session = TerminalSessionFactory.CreateSession(
            sessionId,
            sessionTitle,
            launchOptions.InitialWidth,
            launchOptions.InitialHeight,
            onStateChanged,
            onTitleChanged,
            onProcessExited,
            rpcHandler,
            oscRpcHandler,
            launchOptions);

        try
        {
            await session.InitializeAsync(launchOptions, cancellationToken);
            session.Activate();

            // Update session settings with process information after successful initialization
            var processId = session.ProcessManager.ProcessId;
            var isRunning = session.ProcessManager.IsRunning;
            session.UpdateProcessState(processId, isRunning);

            // Update session settings with terminal dimensions
            session.UpdateTerminalDimensions(session.Terminal.Width, session.Terminal.Height);

            // For custom shells, send initial output AFTER session is fully initialized and wired up
            // This ensures the terminal is ready to process output and cursor starts at 0,0
            if (session.ProcessManager is CustomShellPtyBridge customShellBridge)
            {
                customShellBridge.SendInitialOutput();
            }

            return session;
        }
        catch
        {
            // Unsubscribe from events to prevent memory leaks
            try
            {
                session.StateChanged -= onStateChanged;
                session.TitleChanged -= onTitleChanged;
                session.ProcessExited -= onProcessExited;
            }
            catch
            {
                // Ignore unsubscribe errors during cleanup
            }

            // Dispose session resources safely
            try
            {
                session.Dispose();
            }
            catch
            {
                // Ignore dispose errors during cleanup
            }

            throw;
        }
    }
}
