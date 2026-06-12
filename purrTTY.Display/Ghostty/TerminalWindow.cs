using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Display.Theming;
using purrTTY.Logging;
using PurrTTY.Terminal.Rendering;
using PurrTTY.Terminal.Sessions;
using TerminalTheme = PurrTTY.Terminal.TerminalTheme;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// One ImGui terminal window. Owns a <see cref="SessionManager"/> whose sessions
/// are presented as tabs (the tab bar is hidden while there is a single tab),
/// applies per-window theme/font/opacity settings, and hides all window chrome
/// (background, border, menu strip, tabs) whenever the mouse is not over the
/// window — including while focused — for game immersion. The thin menu-bar
/// strip doubles as the drag handle since the window has no title bar; an
/// invisible button over the grid keeps text selection from dragging the window.
///
/// Partials: input encoding in <c>TerminalWindow.Input.cs</c>, font/metric
/// resolution in <c>TerminalWindow.Fonts.cs</c>, the perf HUD in
/// <c>TerminalWindow.PerfHud.cs</c>, the lock-mode hot zone in
/// <c>TerminalWindow.HotZone.cs</c>.
/// </summary>
public sealed partial class TerminalWindow : IDisposable
{
    /// <summary>Pixels around the window rect that still count as "hovering" (covers resize grips).</summary>
    private const float HoverMargin = 8f;

    /// <summary>Thickness of the focus/hover border overlay.</summary>
    private const float BorderThickness = 2f;

    /// <summary>ImGui popup id for the grid's right-click copy/paste context menu.</summary>
    private const string GridContextMenuId = "##grid_context";

    private readonly string _imguiName;

    private TerminalTheme _engineTheme;
    private RgbaColor _selectionColor;

    private bool _hasFocus;
    private bool _wantFocus;
    private bool _wasHoveredLastFrame;
    private bool _hadSessions;

    // Why the initial session could not be started, shown in the otherwise
    // empty window. Written from the session-creation pool thread.
    private volatile string? _sessionStartError;
    private bool _disposed;
    private Guid? _lastActiveSessionId;
    private (float2 Pos, float2 Size)? _pendingPlacement;

    // Interactive-resize tracking for the grid snap (see TrackResizeSnap).
    private float2 _trackedWindowSize;
    private bool _windowSizeTracked;
    private bool _userResizing;
    private float2? _pendingSnapSize;

    private float _cellWidth = 8f;
    private float _cellHeight = 16f;

    public int Id { get; }
    public SessionManager Sessions { get; }
    public TerminalWindowSettings Settings { get; }

    /// <summary>False once the window has been closed; the controller disposes it.</summary>
    public bool IsOpen { get; private set; } = true;

    public bool HasFocus => _hasFocus;

    /// <summary>
    /// True while the grid's right-click context menu is open. The popup steals
    /// ImGui window focus, so the host must treat it as focus-equivalent when
    /// gating game hotkeys.
    /// </summary>
    public bool IsContextMenuOpen { get; private set; }

    public string Title => Sessions.ActiveSession?.Title ?? "purrTTY";

    public float2 LastKnownPosition { get; private set; }
    public float2 LastKnownSize { get; private set; }
    public bool HasObservedGeometry { get; private set; }

    /// <summary>Host hook to suppress keyboard input for a frame (e.g. when the toggle hotkey fires).</summary>
    public Func<bool>? KeyboardSuppression { get; set; }

    /// <summary>Raised when this window gains or loses ImGui focus.</summary>
    public event Action<TerminalWindow, bool>? FocusChanged;

    /// <summary>
    /// Raised whenever user input (key, mouse report, paste) was sent to the
    /// active session. Deliberately payload-free: the only consumer is the
    /// controller's blink-phase reset, and copying the bytes per keystroke was
    /// avoidable garbage.
    /// </summary>
    public event Action? InputSent;

    public TerminalWindow(
        int id,
        SessionManager sessions,
        TerminalWindowSettings settings,
        float2? initialPosition = null,
        float2? initialSize = null)
    {
        Id = id;
        Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _imguiName = $"purrTTY##purrtty_window_{id}";
        _hotZoneImguiName = $"##purrtty_hotzone_{id}";

        if (initialPosition is { } pos && initialSize is { } size)
        {
            _pendingPlacement = (pos, size);
        }

        _engineTheme = Settings.Colors.ToEngineTheme();
        _selectionColor = Settings.Colors.SelectionBackground.WithAlpha(0xAA);

        foreach (var session in Sessions.Sessions)
        {
            WireSession(session);
        }

        // Pre-publication hook: the theme push and surface event wiring happen
        // before the session is visible to the tick thread, so the native calls
        // in WireSession can never race a concurrent BuildFrame (sessions are
        // created on pool threads).
        Sessions.SessionConfigurator = WireSession;
    }

    public void RequestFocus() => _wantFocus = true;

    /// <summary>
    /// Ticks every session's surface without rendering. Called by the
    /// controller (on the tick thread) while the terminal is hidden so PTY
    /// output keeps being consumed instead of piling up in the surface inboxes.
    /// </summary>
    public void DrainSessions()
    {
        if (_disposed || !IsOpen)
        {
            return;
        }

        // Indexed loop: Sessions is an IReadOnlyList-typed array snapshot, and
        // a foreach through the interface allocates an enumerator (this and the
        // loops in Render run every frame / on the hidden-drain cadence).
        var sessions = Sessions.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            sessions[i].Surface.BuildFrame();
        }
    }

    /// <summary>Closes the window; its sessions are disposed by the controller.</summary>
    public void Close() => IsOpen = false;

    /// <summary>
    /// Records why a session could not be started so the window can display it
    /// instead of staying permanently blank. Safe to call from any thread.
    /// </summary>
    public void NotifySessionStartFailed(string message) => _sessionStartError = message;

    /// <summary>
    /// Applies a theme to this window. Colors always apply; font and opacity
    /// values only apply when the theme defines them (user-saved themes do,
    /// bundled color schemes keep the window's current values).
    /// </summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        Settings.ThemeName = theme.Name;
        Settings.Colors = theme.Colors.Clone();
        Settings.ApplyThemeOverrides(theme);

        PushThemeToSessions();
        ApplyCursorStyleToSessions();
    }

    /// <summary>Snapshots the window's current settings as a named theme definition.</summary>
    public ThemeDefinition SnapshotAsTheme(string name) => new()
    {
        Name = name,
        Source = ThemeSource.UserFile,
        Colors = Settings.Colors.Clone(),
        FontFamily = Settings.FontFamily,
        FontSize = Settings.FontSize,
        BackgroundOpacity = Settings.BackgroundOpacity,
        ForegroundOpacity = Settings.ForegroundOpacity,
        CellBackgroundOpacity = Settings.CellBackgroundOpacity,
        CursorStyle = Settings.CursorStyle,
        CursorBlink = Settings.CursorBlink,
        BorderOnFocus = Settings.BorderOnFocus,
        BorderOnHover = Settings.BorderOnHover,
        BorderOpacity = Settings.BorderOpacity,
        LockMode = Settings.LockMode,
        HotZoneEnabled = Settings.HotZoneEnabled,
        HotZonePlacement = Settings.HotZonePlacement,
        HotZoneWidth = Settings.HotZoneWidth,
        HotZoneHeight = Settings.HotZoneHeight,
        HotZoneColor = Settings.HotZoneColor,
        HotZoneOpacity = Settings.HotZoneOpacity,
        HotZoneHoverOpacity = Settings.HotZoneHoverOpacity,
    };

    /// <summary>
    /// Applies a cursor style/blink change to this window and pushes it to every
    /// session as the engine default (apps keep their DECSCUSR overrides).
    /// </summary>
    public void SetCursorStyle(CursorShape style, bool blink)
    {
        Settings.CursorStyle = style;
        Settings.CursorBlink = blink;
        ApplyCursorStyleToSessions();
    }

    private void ApplyCursorStyleToSessions()
    {
        foreach (var session in Sessions.Sessions)
        {
            session.Surface.SetCursorStyle(Settings.CursorStyle, Settings.CursorBlink);
        }
    }

    private void PushThemeToSessions()
    {
        _engineTheme = Settings.Colors.ToEngineTheme();
        _selectionColor = Settings.Colors.SelectionBackground.WithAlpha(0xAA);
        foreach (var session in Sessions.Sessions)
        {
            session.Surface.SetTheme(_engineTheme);
        }
    }

    private void WireSession(TerminalSession session)
    {
        session.Surface.SetTheme(_engineTheme);
        session.Surface.SetCursorStyle(Settings.CursorStyle, Settings.CursorBlink);
        // OSC 52: an app asked to set the system clipboard. (A clipboard *query*
        // — Text == null — would need an OSC 52 reply written back to the PTY;
        // that round-trip is deferred.)
        session.Surface.ClipboardRequested += OnClipboardRequested;
    }

    private static void OnClipboardRequested(PurrTTY.Terminal.ClipboardRequest request)
    {
        if (!string.IsNullOrEmpty(request.Text))
        {
            ImGui.SetClipboardText(request.Text);
        }
    }

    /// <summary>Submits the window for this frame.</summary>
    /// <param name="hideChromeWhenNotHovered">Global "hide UI when not hovered" setting.</param>
    /// <param name="cursorBlinkOn">Shared blink phase from the controller.</param>
    public void Render(bool hideChromeWhenNotHovered, bool cursorBlinkOn)
    {
        if (!IsOpen || _disposed)
        {
            return;
        }

        float fontSize = Settings.FontSize;
        var fonts = ResolveFontsCached(fontSize);

        // Lock mode: while unfocused the window takes no mouse input at all, so
        // clicks (and hover) fall through to whatever is beneath — game world or
        // game UI. A pending focus request (hot zone click, menu action, toggle
        // hotkey) restores input on the same frame the focus is applied, so
        // there is never a frame where a freshly focused window ignores input.
        // The open context menu counts as focus (it steals ImGui window focus —
        // same reasoning as the controller's key gate): without it, right-
        // clicking a locked terminal turned it click-through under its own
        // popup and stranded it unfocused when the popup closed.
        bool clickThrough = Settings.LockMode && !_hasFocus && !_wantFocus && !IsContextMenuOpen;

        // While click-through, hovering must not reveal chrome the mouse cannot
        // interact with — the hot zone (and optional hover border) are the only
        // affordances of a locked window.
        bool showChrome = !hideChromeWhenNotHovered || (_wasHoveredLastFrame && !clickThrough);

        var bg = Settings.Colors.Background;
        var windowBg = showChrome
            ? new float4(bg.R / 255f, bg.G / 255f, bg.B / 255f, Settings.BackgroundOpacity)
            : new float4(0f, 0f, 0f, 0f);

        int colorPushes = 1;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, windowBg);
        if (!showChrome)
        {
            // The menu-bar strip background and the resize grip are part of the
            // window decorations drawn during Begin, so they have to be hidden
            // via style colors here. The grip stays functional while invisible.
            var transparent = new float4(0f, 0f, 0f, 0f);
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, transparent);
            ImGui.PushStyleColor(ImGuiCol.ResizeGrip, transparent);
            ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, transparent);
            ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, transparent);
            colorPushes += 4;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, showChrome ? 1f : 0f);

        if (_pendingPlacement is { } placement)
        {
            // Saved/cascaded geometry can land fully off-screen (different
            // monitor layout since last run); with chrome hidden there would be
            // nothing visible to grab, so clamp into the viewport work area.
            var (clampedPos, clampedSize) = ClampToWorkArea(placement.Pos, placement.Size);
            ImGui.SetNextWindowPos(clampedPos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(clampedSize, ImGuiCond.Always);
            _pendingPlacement = null;
        }
        else if (_pendingSnapSize is { } snapSize)
        {
            ImGui.SetNextWindowSize(snapSize, ImGuiCond.Always);
            _pendingSnapSize = null;
        }
        else
        {
            ImGui.SetNextWindowSize(new float2(880f, 520f), ImGuiCond.FirstUseEver);
        }

        if (_wantFocus)
        {
            ImGui.SetNextWindowFocus();
            _wantFocus = false;
        }

        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.MenuBar;
        if (clickThrough)
        {
            // NoMouseInputs removes the window from ImGui's hover resolution
            // entirely: no widget interaction, no drag-move, no resize — and no
            // WantCaptureMouse, so the click reaches the game.
            flags |= ImGuiWindowFlags.NoMouseInputs;
        }

        ImGui.Begin(_imguiName, flags);
        ImGui.PopStyleVar(2);

        UpdateFocusState(ImGui.IsWindowFocused());

        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X > 0f && windowSize.Y > 0f)
        {
            LastKnownPosition = windowPos;
            LastKnownSize = windowSize;
            HasObservedGeometry = true;
        }

        RenderTitleStrip(showChrome);
        RenderTabBar(showChrome);

        var canvasPos = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        // The window background is fully transparent while chrome is hidden, so
        // paint the terminal area itself with the theme background.
        if (!showChrome && Settings.BackgroundOpacity > 0f && avail.X > 0f && avail.Y > 0f)
        {
            var fill = Settings.Colors.Background.WithAlpha((byte)Math.Clamp(Settings.BackgroundOpacity * 255f, 0f, 255f));
            ImGui.GetWindowDrawList().AddRectFilled(canvasPos, canvasPos + avail, FrameGridRenderer.ToU32(fill));
        }

        int cols = Math.Max(1, (int)(avail.X / _cellWidth));
        int rows = Math.Max(1, (int)(avail.Y / _cellHeight));

        TrackResizeSnap(windowSize, avail, cols, rows);

        var session = Sessions.ActiveSession;
        if (session != null)
        {
            _hadSessions = true;
            _sessionStartError = null;

            if (cols != session.Surface.Cols || rows != session.Surface.Rows)
            {
                session.Surface.Resize(cols, rows, (int)_cellWidth, (int)_cellHeight);
                session.UpdateTerminalDimensions(cols, rows);
                session.ProcessManager.Resize(cols, rows);
                Sessions.UpdateLastKnownTerminalDimensions(cols, rows);
            }

            // The engine's mouse encoder maps pixels to cells by dividing by
            // *integer* cell metrics, so the surface size must be the integer
            // product (cols * truncated width). Passing the truncated
            // fractional product instead makes reported cells drift right as x
            // grows (see HandleAppMouse, which synthesizes matching pixels).
            int mouseCellW = Math.Max(1, (int)_cellWidth);
            int mouseCellH = Math.Max(1, (int)_cellHeight);
            session.Surface.SetMouseGeometry(cols * mouseCellW, rows * mouseCellH, mouseCellW, mouseCellH);

            long buildStart = Stopwatch.GetTimestamp();
            var frame = session.Surface.BuildFrame();
            long submitStart = Stopwatch.GetTimestamp();

            // An unfocused window draws a steady hollow cursor (classic terminal
            // behavior; the renderer forces the hollow shape) — it also signals
            // that input currently goes elsewhere, which matters in lock mode.
            bool cursorOn = !_hasFocus || !frame.Cursor.Blinking || cursorBlinkOn;

            var renderStats = FrameGridRenderer.Render(
                frame, ImGui.GetWindowDrawList(), canvasPos,
                _cellWidth, _cellHeight, fonts, fontSize, _selectionColor, cursorOn,
                Settings.ForegroundOpacity, Settings.CellBackgroundOpacity, _hasFocus);

            if (ShowPerfHud)
            {
                DrawPerfHud(
                    session, canvasPos, avail, cols, rows,
                    Stopwatch.GetElapsedTime(buildStart, submitStart).TotalMilliseconds,
                    Stopwatch.GetElapsedTime(submitStart).TotalMilliseconds,
                    renderStats);
            }
        }
        else if (_hadSessions && Sessions.SessionCount == 0)
        {
            // The last tab was closed; retire the window.
            IsOpen = false;
        }
        else if (_sessionStartError is { } startError && avail.X > 0f && avail.Y > 0f)
        {
            // The session never started (bad shell path, broken WSL, ...):
            // show why instead of leaving a permanently blank window.
            var drawList = ImGui.GetWindowDrawList();
            float lineH = ImGui.GetTextLineHeight();
            drawList.AddText(new float2(canvasPos.X + 8f, canvasPos.Y + 8f), 0xFF5555FFu, "Failed to start shell session:");
            drawList.AddText(new float2(canvasPos.X + 8f, canvasPos.Y + 8f + lineH), 0xFFCCCCCCu, startError);
            drawList.AddText(new float2(canvasPos.X + 8f, canvasPos.Y + 8f + lineH * 2.5f), 0xFF999999u,
                "Pick another shell from the purrTTY menu (New Tab / New Window).");
        }

        // Tick every non-active tab too: the PTY pumps never sleep and a
        // surface inbox only drains inside BuildFrame, so an unticked chatty
        // background tab grows its inbox until the safety cap drops output.
        // Dirty tracking makes ticking a quiet session nearly free. (Indexed:
        // see DrainSessions.)
        var allSessions = Sessions.Sessions;
        for (int i = 0; i < allSessions.Count; i++)
        {
            if (!ReferenceEquals(allSessions[i], session))
            {
                allSessions[i].Surface.BuildFrame();
            }
        }

        // Invisible button over the grid: claims the mouse so click-drag selects
        // text instead of moving the window (the menu strip remains the drag handle).
        bool gridHovered = false;
        if (avail.X >= 1f && avail.Y >= 1f)
        {
            ImGui.SetCursorScreenPos(canvasPos);
            ImGui.InvisibleButton("##grid", avail);
            gridHovered = ImGui.IsItemHovered();
        }

        if (session != null && _hasFocus)
        {
            HandleInput(session, canvasPos, cols, rows, gridHovered);
        }

        // Rendered unconditionally (not gated on focus) so the right-click
        // copy/paste popup stays alive once opened, even as the mouse leaves the grid.
        if (session != null)
        {
            DrawContextMenu();
        }

        // Queried inside this window's ID scope (popup ids are window-local).
        IsContextMenuOpen = session != null && ImGui.IsPopupOpen(GridContextMenuId);

        // Hover state feeding next frame's chrome visibility: mouse anywhere over
        // the window rect (plus a margin for resize grips), or an in-progress drag.
        var mouse = ImGui.GetMousePos();
        bool mouseInBounds =
            mouse.X >= windowPos.X - HoverMargin && mouse.X <= windowPos.X + windowSize.X + HoverMargin &&
            mouse.Y >= windowPos.Y - HoverMargin && mouse.Y <= windowPos.Y + windowSize.Y + HoverMargin;
        _wasHoveredLastFrame = mouseInBounds || _selecting;

        // Focus/hover border. Drawn on the foreground list as a pure overlay:
        // it never participates in layout, so chrome hiding (which requires a
        // stable grid size) is unaffected. Hover uses the same rect test as
        // chrome visibility, so it also works while click-through — a locked
        // window lights up under the mouse without capturing it.
        if (Settings.BorderOpacity > 0f
            && ((Settings.BorderOnFocus && _hasFocus) || (Settings.BorderOnHover && mouseInBounds)))
        {
            var border = Settings.Colors.Foreground.WithAlpha((byte)Math.Clamp(Settings.BorderOpacity * 255f, 0f, 255f));
            ImGui.GetForegroundDrawList().AddRect(
                windowPos, windowPos + windowSize, FrameGridRenderer.ToU32(border), thickness: BorderThickness);
        }

        ImGui.End();
        ImGui.PopStyleColor(colorPushes);

        // The hot zone is its own tiny window submitted after the terminal: with
        // the terminal click-through it is the one mouse-interactive spot that
        // refocuses the window (and absorbs that click so the game never sees it).
        if (clickThrough && Settings.HotZoneEnabled && HasObservedGeometry)
        {
            RenderHotZone(windowPos, windowSize);
        }
    }

    private void RenderTitleStrip(bool showChrome)
    {
        if (!showChrome)
        {
            // Keep the strip (and its drag area / layout height) but draw nothing.
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0f);
        }

        if (ImGui.BeginMenuBar())
        {
            ImGui.TextDisabled($" {Title}");

            float closeWidth = ImGui.GetFrameHeight() * 1.5f;
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - closeWidth));
            if (ImGui.MenuItem("x##close_window") && showChrome)
            {
                Close();
            }

            ImGui.EndMenuBar();
        }

        if (!showChrome)
        {
            ImGui.PopStyleVar();
        }
    }

    private void RenderTabBar(bool showChrome)
    {
        var sessions = Sessions.Sessions;
        var active = Sessions.ActiveSession;
        bool activeChanged = active?.Id != _lastActiveSessionId;
        _lastActiveSessionId = active?.Id;

        if (sessions.Count <= 1)
        {
            return;
        }

        // While chrome is hidden the tab bar stays laid out (so the grid height
        // is stable) but is rendered fully transparent. It becomes visible the
        // moment the mouse hovers the window.
        if (!showChrome)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0f);
        }

        if (ImGui.BeginTabBar("##session_tabs",
                ImGuiTabBarFlags.Reorderable | ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.FittingPolicyScroll))
        {
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                bool isActive = active != null && session.Id == active.Id;
                var tabFlags = isActive && activeChanged ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                bool open = true;
                if (ImGui.BeginTabItem(TabLabel(session), ref open, tabFlags))
                {
                    // The !activeChanged guard is load-bearing: on the frame
                    // after an external active-session change (new tab created,
                    // active closed), ImGui still reports the *previous* tab as
                    // selected — switching on that stale report would instantly
                    // revert the change the SetSelected flag above is applying.
                    if (!isActive && !activeChanged)
                    {
                        Sessions.SwitchToSession(session.Id);
                    }

                    ImGui.EndTabItem();
                }

                if (!open)
                {
                    CloseSession(session.Id);
                }
            }

            ImGui.EndTabBar();
        }

        if (!showChrome)
        {
            ImGui.PopStyleVar();
        }
    }

    // Composed tab labels are cached per session: title + guid formatting per
    // tab per frame is measurable garbage on the render path.
    private readonly Dictionary<Guid, (string Title, int? ExitCode, string Label)> _tabLabels = new();

    private string TabLabel(TerminalSession session)
    {
        string title = session.Title;
        int? exitCode = session.ProcessManager.ExitCode;
        if (_tabLabels.TryGetValue(session.Id, out var cached)
            && cached.Title == title && cached.ExitCode == exitCode)
        {
            return cached.Label;
        }

        string label = exitCode is { } code
            ? $"{title} (exit {code})##tab_{session.Id}"
            : $"{title}##tab_{session.Id}";
        _tabLabels[session.Id] = (title, exitCode, label);
        return label;
    }

    private void CloseSession(Guid sessionId)
    {
        _tabLabels.Remove(sessionId);
        try
        {
            // Deliberately synchronous on the tick thread: closing a session
            // detaches and disposes its native surface, which must happen on the
            // thread that ticks BuildFrame (a pool-thread dispose can free native
            // handles out from under an in-flight frame build). Session close
            // completes synchronously; the slow PTY/process teardown is
            // backgrounded inside the session itself.
            Sessions.CloseSessionAsync(sessionId).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"TerminalWindow: failed to close session {sessionId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Snaps the window to the character grid when an interactive resize ends.
    /// While a resize drag is in progress the terminal is resized live (cols/rows
    /// floor-divide the content region, leaving a fractional-cell remainder); on
    /// mouse release the window shrinks by that remainder so the grid fills the
    /// content region exactly. The chrome contribution (menu strip, tab bar,
    /// padding, border) is measured as <c>windowSize - avail</c> on the same
    /// frame rather than computed from style metrics, so the target is exact for
    /// any chrome configuration. A resize is only recognized when the size
    /// changes while the left button is held, and the snap fires on release —
    /// our own SetNextWindowSize applies with the mouse up, so it can never be
    /// mistaken for a user resize (no feedback loop, no time-based guards).
    /// </summary>
    private void TrackResizeSnap(float2 windowSize, float2 avail, int cols, int rows)
    {
        if (!_windowSizeTracked)
        {
            _trackedWindowSize = windowSize;
            _windowSizeTracked = true;
            return;
        }

        bool sizeChanged =
            Math.Abs(windowSize.X - _trackedWindowSize.X) > 0.5f ||
            Math.Abs(windowSize.Y - _trackedWindowSize.Y) > 0.5f;
        _trackedWindowSize = windowSize;

        bool mouseDown = ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (sizeChanged && mouseDown)
        {
            _userResizing = true;
            return;
        }

        if (_userResizing && !mouseDown)
        {
            _userResizing = false;

            float targetX = windowSize.X - (avail.X - cols * _cellWidth);
            float targetY = windowSize.Y - (avail.Y - rows * _cellHeight);
            if (Math.Abs(targetX - windowSize.X) > 0.5f || Math.Abs(targetY - windowSize.Y) > 0.5f)
            {
                _pendingSnapSize = new float2(targetX, targetY);
            }
        }
    }

    // Saved or cascaded geometry is clamped into the main viewport's work area:
    // restoring fully off-screen with chrome hidden leaves nothing visible to grab.
    private static (float2 Pos, float2 Size) ClampToWorkArea(float2 pos, float2 size)
    {
        var viewport = ImGui.GetMainViewport();
        var workPos = viewport.WorkPos;
        var workSize = viewport.WorkSize;
        if (workSize.X <= 0f || workSize.Y <= 0f)
        {
            return (pos, size);
        }

        float w = Math.Clamp(size.X, Math.Min(100f, workSize.X), workSize.X);
        float h = Math.Clamp(size.Y, Math.Min(60f, workSize.Y), workSize.Y);
        float x = Math.Clamp(pos.X, workPos.X, Math.Max(workPos.X, workPos.X + workSize.X - w));
        float y = Math.Clamp(pos.Y, workPos.Y, Math.Max(workPos.Y, workPos.Y + workSize.Y - h));
        return (new float2(x, y), new float2(w, h));
    }

    private void UpdateFocusState(bool nowFocused)
    {
        if (nowFocused == _hasFocus)
        {
            return;
        }

        _hasFocus = nowFocused;
        FocusChanged?.Invoke(this, nowFocused);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsOpen = false;
        Sessions.Dispose();
    }
}
