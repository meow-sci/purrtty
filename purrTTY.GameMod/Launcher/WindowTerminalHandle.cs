using Brutal.ImGuiApi;
using purrTTY.Core.Terminal;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Launcher;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.Launcher;

/// <summary>
/// A handle to a standalone 2D terminal window. The window's first session is "the program": it is
/// picked up by polling (the session starts on a pool thread after the window exists), and its exit
/// is reported through <c>ProcessExited</c>. The window closing (the "x" button, the last tab closing,
/// <see cref="GhosttyTerminalController.CloseWindow"/>) closes the handle.
/// </summary>
internal sealed class WindowTerminalHandle : TerminalHandle
{
    private readonly GhosttyTerminalController _controller;
    private readonly TerminalWindow _window;
    private readonly float2 _initialPosition;
    private bool _startFailureReported;
    private bool _sessionGoneReported;

    public WindowTerminalHandle(
        TerminalLauncher launcher,
        MainThreadPump pump,
        GhosttyTerminalController controller,
        TerminalWindow window,
        float2 initialPosition)
        : base(launcher, pump)
    {
        _controller = controller;
        _window = window;
        _initialPosition = initialPosition;

        // Both events are raised on the main thread, possibly from inside the window's own ImGui
        // scope (UpdateFocusState / Close run in Render): queue the real work to the next pump.
        _window.Closed += OnWindowClosed;
        _window.FocusChanged += OnWindowFocusChanged;
    }

    public override string Name => _window.Name;

    public override TerminalPlacementKind Placement => TerminalPlacementKind.Window;

    protected override bool IsGridReady => Session is { } session && _window.IsSessionSized(session.Id);

    protected override void TickCore()
    {
        var sessions = _window.Sessions.Sessions;
        if (Session is null)
        {
            if (sessions.Count > 0)
            {
                BindSession(sessions[0]);
            }
            else if (_window.SessionStartError is { } error && !_startFailureReported)
            {
                // The session never started (bad program path, ...): the window shows the reason; the
                // handle reports the program as exited with no exit code.
                _startFailureReported = true;
                ModLog.Log.Error($"purrTTY launcher: terminal '{Name}' failed to start: {error}");
                MarkExited(null);
            }

            return;
        }

        if (!_sessionGoneReported && !Contains(sessions, Session))
        {
            // The user closed the program's tab but other tabs keep the window alive.
            _sessionGoneReported = true;
            MarkExited(Session.ProcessManager.ExitCode);
        }
    }

    protected override void FocusCore() => _window.RequestFocus();

    protected override void CloseCore() => _controller.CloseWindow(_window);

    protected override bool RenameCore(string name) => _window.TryRename(name);

    protected override bool ResizeCore(int cols, int rows)
    {
        // A window sizes its grid to its pane, so convert the grid to an approximate pixel size.
        // Before the first render the window's own cell size is only a placeholder: measure the font.
        var (cellWidth, cellHeight) = _window.HasObservedGeometry
            ? _window.CellSize
            : InWorldTerminalRenderer.MeasureCell(_controller.Configuration, _controller.Catalog, _window.Settings.ThemeName);

        var (width, height) = TerminalLaunchMapper.WindowPixelSize(
            cols, rows, cellWidth, cellHeight, ImGui.GetFrameHeight(), TerminalWindow.ContentPadding);
        var position = _window.HasObservedGeometry ? _window.LastKnownPosition : _initialPosition;
        _window.RequestPlacement(position, new float2(width, height));
        return true;
    }

    protected override void SetPlacementCore(TerminalInWorldOptions placement)
        => throw new InvalidOperationException("SetPlacementAsync applies to in-world terminals only.");

    protected override void ApplyThemeCore(string themeName)
    {
        var theme = _controller.Catalog.Find(themeName)
            ?? throw new KeyNotFoundException($"No theme named '{themeName}'.");
        _window.ApplyTheme(theme);
    }

    protected override void DetachCore()
    {
        _window.Closed -= OnWindowClosed;
        _window.FocusChanged -= OnWindowFocusChanged;
    }

    private void OnWindowClosed(TerminalWindow window) => Pump.Post(CloseState);

    private void OnWindowFocusChanged(TerminalWindow window, bool focused)
        => Pump.Post(() =>
        {
            if (IsOpenOnMain)
            {
                ObserveFocus(_window.HasFocus);
            }
        });

    private static bool Contains(IReadOnlyList<PurrTTY.Terminal.Sessions.TerminalSession> sessions, PurrTTY.Terminal.Sessions.TerminalSession session)
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
