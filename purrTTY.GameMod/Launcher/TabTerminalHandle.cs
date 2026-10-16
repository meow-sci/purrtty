using purrTTY.Core.Terminal;
using purrTTY.Display.Ghostty;
using purrTTY.Logging;
using PurrTTY.Terminal.Sessions;

namespace purrTTY.GameMod.Launcher;

/// <summary>
/// A handle to one tab (session) inside an existing 2D terminal window. The session is created on a
/// pool thread by <see cref="GhosttyTerminalController.OpenTabIn"/>; its task result is the exact
/// session (no guessing from session-created events). The handle closes when the tab's session is
/// closed (by the user or <see cref="CloseAsync"/>) or when the host window closes. A tab has no
/// registry name, theme or size of its own: <see cref="Name"/> is a handle label, and resize/theme
/// requests are refused because they would affect the sibling tabs.
/// </summary>
internal sealed class TabTerminalHandle : TerminalHandle
{
    private readonly TerminalWindow _window;
    private string _name;
    private bool _closeWhenBound;

    public TabTerminalHandle(
        TerminalLauncher launcher,
        MainThreadPump pump,
        TerminalWindow window,
        string name,
        Task<TerminalSession?> pendingSession)
        : base(launcher, pump)
    {
        _window = window;
        _name = name;

        _window.Closed += OnWindowClosed;

        // The task completes on a pool thread: re-enter the main thread before touching state.
        pendingSession.ContinueWith(
            task =>
            {
                TerminalSession? session = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                Pump.Post(() => OnSessionResolved(session));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public override string Name => Volatile.Read(ref _name);

    public override TerminalPlacementKind Placement => TerminalPlacementKind.Tab;

    protected override bool IsGridReady => Session is { } session && _window.IsSessionSized(session.Id);

    private void OnSessionResolved(TerminalSession? session)
    {
        if (_closeWhenBound)
        {
            // CloseAsync ran while the session was still starting: the handle is already closed, so
            // close the tab that has just appeared instead of leaving it orphaned.
            if (session is not null)
            {
                _window.CloseSession(session.Id);
            }

            return;
        }

        if (!IsOpenOnMain)
        {
            return;
        }

        if (session is null)
        {
            // CreateSessionAsync failed (already logged by the controller; the window shows the reason).
            ModLog.Log.Error($"purrTTY launcher: tab '{Name}' failed to start");
            MarkExited(null);
            return;
        }

        BindSession(session);
    }

    protected override void TickCore()
    {
        if (Session is not { } session)
        {
            return;
        }

        // The tab is gone when its session left the window's list (the user closed the tab).
        if (!Contains(_window.Sessions.Sessions, session))
        {
            CloseState();
            return;
        }

        // Polled (there is no event for "the active tab changed"): the tab is focused when its
        // window holds focus and it is the window's active tab.
        ObserveFocus(_window.HasFocus && ReferenceEquals(_window.Sessions.ActiveSession, session));
    }

    protected override void FocusCore()
    {
        if (Session is { } session)
        {
            _window.Sessions.SwitchToSession(session.Id);
        }

        _window.RequestFocus();
    }

    protected override void CloseCore()
    {
        if (Session is { } session)
        {
            _window.CloseSession(session.Id);
        }
        else
        {
            // Still starting: close it the moment it exists.
            _closeWhenBound = true;
        }
    }

    protected override bool RenameCore(string name)
    {
        // Labels only: a tab is not a registry entry. Names must stay unique among this launcher's terminals.
        string trimmed = name.Trim();
        if (TerminalTargetRegistry.Resolve(trimmed) is not null || Launcher.IsNameInUse(trimmed, except: this))
        {
            return false;
        }

        Volatile.Write(ref _name, trimmed);
        return true;
    }

    protected override bool ResizeCore(int cols, int rows) => false; // a tab has its window's size

    protected override void SetPlacementCore(TerminalInWorldOptions placement)
        => throw new InvalidOperationException("SetPlacementAsync applies to in-world terminals only.");

    protected override void ApplyThemeCore(string themeName)
        => throw new InvalidOperationException("A tab uses its window's theme; launch a window to theme it separately.");

    protected override void DetachCore() => _window.Closed -= OnWindowClosed;

    private void OnWindowClosed(TerminalWindow window) => Pump.Post(CloseState);

    private static bool Contains(IReadOnlyList<TerminalSession> sessions, TerminalSession session)
    {
        for (int i = 0; i < sessions.Count; i++)
        {
            if (ReferenceEquals(sessions[i], session))
            {
                return true;
            }
        }

        return false;
    }
}
