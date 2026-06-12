using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using purrTTY.Logging;
using PurrTTY.Terminal.Ghostty;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;
using PurrTTY.Terminal.Sessions;
using TerminalTheme = PurrTTY.Terminal.TerminalTheme;
using TKeyMods = PurrTTY.Terminal.Input.KeyModifiers;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// One ImGui terminal window. Owns a <see cref="SessionManager"/> whose sessions
/// are presented as tabs (the tab bar is hidden while there is a single tab),
/// applies per-window theme/font/opacity settings, and hides all window chrome
/// (background, border, menu strip, tabs) whenever the mouse is not over the
/// window — including while focused — for game immersion. The thin menu-bar
/// strip doubles as the drag handle since the window has no title bar; an
/// invisible button over the grid keeps text selection from dragging the window.
/// </summary>
public sealed class TerminalWindow : IDisposable
{
    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] NamedKeys =
    {
        (ImGuiKey.Enter, TerminalKey.Enter),
        (ImGuiKey.KeypadEnter, TerminalKey.Enter),
        (ImGuiKey.Backspace, TerminalKey.Backspace),
        (ImGuiKey.Tab, TerminalKey.Tab),
        (ImGuiKey.Escape, TerminalKey.Escape),
        (ImGuiKey.UpArrow, TerminalKey.ArrowUp),
        (ImGuiKey.DownArrow, TerminalKey.ArrowDown),
        (ImGuiKey.RightArrow, TerminalKey.ArrowRight),
        (ImGuiKey.LeftArrow, TerminalKey.ArrowLeft),
        (ImGuiKey.Home, TerminalKey.Home),
        (ImGuiKey.End, TerminalKey.End),
        (ImGuiKey.Delete, TerminalKey.Delete),
        (ImGuiKey.Insert, TerminalKey.Insert),
        (ImGuiKey.PageUp, TerminalKey.PageUp),
        (ImGuiKey.PageDown, TerminalKey.PageDown),
        (ImGuiKey.F1, TerminalKey.F1), (ImGuiKey.F2, TerminalKey.F2), (ImGuiKey.F3, TerminalKey.F3),
        (ImGuiKey.F4, TerminalKey.F4), (ImGuiKey.F5, TerminalKey.F5), (ImGuiKey.F6, TerminalKey.F6),
        (ImGuiKey.F7, TerminalKey.F7), (ImGuiKey.F8, TerminalKey.F8), (ImGuiKey.F9, TerminalKey.F9),
        (ImGuiKey.F10, TerminalKey.F10), (ImGuiKey.F11, TerminalKey.F11), (ImGuiKey.F12, TerminalKey.F12),
    };

    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] LetterKeys =
    {
        (ImGuiKey.A, TerminalKey.A), (ImGuiKey.B, TerminalKey.B), (ImGuiKey.C, TerminalKey.C),
        (ImGuiKey.D, TerminalKey.D), (ImGuiKey.E, TerminalKey.E), (ImGuiKey.F, TerminalKey.F),
        (ImGuiKey.G, TerminalKey.G), (ImGuiKey.H, TerminalKey.H), (ImGuiKey.I, TerminalKey.I),
        (ImGuiKey.J, TerminalKey.J), (ImGuiKey.K, TerminalKey.K), (ImGuiKey.L, TerminalKey.L),
        (ImGuiKey.M, TerminalKey.M), (ImGuiKey.N, TerminalKey.N), (ImGuiKey.O, TerminalKey.O),
        (ImGuiKey.P, TerminalKey.P), (ImGuiKey.Q, TerminalKey.Q), (ImGuiKey.R, TerminalKey.R),
        (ImGuiKey.S, TerminalKey.S), (ImGuiKey.T, TerminalKey.T), (ImGuiKey.U, TerminalKey.U),
        (ImGuiKey.V, TerminalKey.V), (ImGuiKey.W, TerminalKey.W), (ImGuiKey.X, TerminalKey.X),
        (ImGuiKey.Y, TerminalKey.Y), (ImGuiKey.Z, TerminalKey.Z),
    };

    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] DigitKeys =
    {
        (ImGuiKey._0, TerminalKey.Digit0), (ImGuiKey._1, TerminalKey.Digit1),
        (ImGuiKey._2, TerminalKey.Digit2), (ImGuiKey._3, TerminalKey.Digit3),
        (ImGuiKey._4, TerminalKey.Digit4), (ImGuiKey._5, TerminalKey.Digit5),
        (ImGuiKey._6, TerminalKey.Digit6), (ImGuiKey._7, TerminalKey.Digit7),
        (ImGuiKey._8, TerminalKey.Digit8), (ImGuiKey._9, TerminalKey.Digit9),
    };

    // Ctrl+Space/[/\/] produce the NUL/ESC/FS/GS controls (and ESC-prefixed
    // forms under Alt); the engine's key encoder derives the byte from key+mods.
    private static readonly (ImGuiKey ImguiKey, TerminalKey Key)[] ControlPunctuationKeys =
    {
        (ImGuiKey.Space, TerminalKey.Space),
        (ImGuiKey.LeftBracket, TerminalKey.BracketLeft),
        (ImGuiKey.RightBracket, TerminalKey.BracketRight),
        (ImGuiKey.Backslash, TerminalKey.Backslash),
    };

    /// <summary>Pixels around the window rect that still count as "hovering" (covers resize grips).</summary>
    private const float HoverMargin = 8f;

    /// <summary>Lock-mode focus hot zone size limits (pixels).</summary>
    public const float MinHotZoneSize = 8f;
    public const float MaxHotZoneSize = 512f;

    /// <summary>Thickness of the focus/hover border overlay.</summary>
    private const float BorderThickness = 2f;

    /// <summary>ImGui popup id for the grid's right-click copy/paste context menu.</summary>
    private const string GridContextMenuId = "##grid_context";

    private readonly string _imguiName;
    private readonly string _hotZoneImguiName;

    private TerminalTheme _engineTheme;
    private RgbaColor _selectionColor;

    private bool _hasFocus;
    private bool _wantFocus;
    private bool _wasHoveredLastFrame;
    private bool _selecting;
    private bool _hadSessions;

    // Last grid cell reported to a mouse-tracking app via a motion event. Motion
    // is only re-reported when the pointer crosses into a different cell (xterm /
    // ghostty granularity), which is what keeps live drag reporting from flooding
    // the PTY on every pixel of movement.
    private int _appMouseCol = -1;
    private int _appMouseRow = -1;

    // Per-button "press was forwarded to the app" state (Left/Middle/Right).
    // Presses are hover-gated but releases are not (so a drag ending off-grid
    // still reports button-up); without this an unrelated release — e.g. a
    // click that started on the game UI — would leak a spurious report.
    private readonly bool[] _appMousePressSent = new bool[3];

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

    /// <summary>Debug overlay with frame-build / submit timings and draw-call counts (all windows).</summary>
    public static bool ShowPerfHud { get; set; }

    // Perf-HUD throughput tracking (bytes consumed per second, ~500ms window).
    private long _hudAccumBytes;
    private long _hudWindowStartTs;
    private double _hudBytesPerSec;

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

    private void RenderHotZone(float2 windowPos, float2 windowSize)
    {
        float w = Math.Clamp(Settings.HotZoneWidth, MinHotZoneSize, Math.Max(MinHotZoneSize, windowSize.X));
        float h = Math.Clamp(Settings.HotZoneHeight, MinHotZoneSize, Math.Max(MinHotZoneSize, windowSize.Y));
        var size = new float2(w, h);
        var pos = HotZonePosition(windowPos, windowSize, w, h);

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        // WindowMinSize would otherwise inflate a small zone to 32x32, leaving
        // dead window area that eats game clicks the user expects to pass through.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new float2(1f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.Begin(_hotZoneImguiName,
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar(3);

        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##hotzone", size);
        bool hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemActivated())
        {
            // Focus on press (not click-release) so the terminal feels instant;
            // the next frame drops click-through and input works normally.
            RequestFocus();
        }

        if (hovered)
        {
            ImGui.SetTooltip("Focus terminal"u8);
        }

        float opacity = hovered ? Settings.HotZoneHoverOpacity : Settings.HotZoneOpacity;
        if (opacity > 0f)
        {
            // Foreground list: the fill must stay visible above the terminal
            // window's background regardless of ImGui window z-order.
            var fill = Settings.HotZoneColor.WithAlpha((byte)Math.Clamp(opacity * 255f, 0f, 255f));
            ImGui.GetForegroundDrawList().AddRectFilled(pos, pos + size, FrameGridRenderer.ToU32(fill), 3f);
        }

        ImGui.End();
    }

    private float2 HotZonePosition(float2 windowPos, float2 windowSize, float w, float h)
    {
        float left = windowPos.X;
        float centerX = windowPos.X + (windowSize.X - w) * 0.5f;
        float right = windowPos.X + windowSize.X - w;
        float top = windowPos.Y;
        float middleY = windowPos.Y + (windowSize.Y - h) * 0.5f;
        float bottom = windowPos.Y + windowSize.Y - h;

        return Settings.HotZonePlacement switch
        {
            HotZonePlacement.TopLeft => new float2(left, top),
            HotZonePlacement.TopCenter => new float2(centerX, top),
            HotZonePlacement.MiddleLeft => new float2(left, middleY),
            HotZonePlacement.MiddleRight => new float2(right, middleY),
            HotZonePlacement.BottomLeft => new float2(left, bottom),
            HotZonePlacement.BottomCenter => new float2(centerX, bottom),
            HotZonePlacement.BottomRight => new float2(right, bottom),
            _ => new float2(right, top),
        };
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

    private void HandleInput(TerminalSession session, float2 canvasPos, int cols, int rows, bool gridHovered)
    {
        var io = ImGui.GetIO();
        HandleKeyboard(session, io);
        HandleMouse(session, io, canvasPos, cols, rows, gridHovered);
    }

    private void HandleKeyboard(TerminalSession session, ImGuiIOPtr io)
    {
        if (KeyboardSuppression?.Invoke() == true)
        {
            return;
        }

        // Standard terminal clipboard chords; never forwarded to the shell.
        if (io.KeyCtrl && io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.V))
        {
            PasteFromClipboard();
            return;
        }

        if (io.KeyCtrl && io.KeyShift && ImGui.IsKeyPressed(ImGuiKey.C))
        {
            CopySelectionToClipboard();
            return;
        }

        var mods = ReadModifiers(io);

        foreach (var (imguiKey, key) in NamedKeys)
        {
            if (ImGui.IsKeyPressed(imguiKey))
            {
                EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods));
            }
        }

        // AltGr (Windows international layouts) is delivered as Ctrl+Alt with
        // the produced character in the ImGui character queue. When both are
        // held AND text arrived, the text is what the user typed (@, {, €, ...)
        // — prefer it over a spurious Ctrl+Alt chord. This is the standard
        // terminal heuristic (ghostty/wezterm/Windows Terminal).
        bool altGrText = io.KeyCtrl && io.KeyAlt && io.InputQueueCharacters.Count > 0;

        // Ctrl/Alt-modified keys never enter the ImGui character queue, so they
        // are encoded from key presses: Ctrl+letter controls, Alt+letter Meta
        // chords (readline Alt+B/F/D, Emacs, mc), Ctrl/Alt+digit, and the Ctrl
        // punctuation controls (NUL/ESC/FS/GS). The engine's key encoder
        // derives the bytes from key + modifiers.
        if ((io.KeyCtrl || io.KeyAlt) && !altGrText)
        {
            foreach (var (imguiKey, key) in LetterKeys)
            {
                if (ImGui.IsKeyPressed(imguiKey))
                {
                    EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods));
                }
            }

            foreach (var (imguiKey, key) in DigitKeys)
            {
                if (ImGui.IsKeyPressed(imguiKey))
                {
                    EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods));
                }
            }

            foreach (var (imguiKey, key) in ControlPunctuationKeys)
            {
                if (ImGui.IsKeyPressed(imguiKey))
                {
                    EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods));
                }
            }
        }

        // Printable text. Skip when Ctrl/Alt are held (those are command combos
        // handled above and do not represent typed text) — unless it's AltGr
        // input, where the queued characters ARE the typed text.
        if (((!io.KeyCtrl && !io.KeyAlt) || altGrText) && io.InputQueueCharacters.Count > 0)
        {
            int count = io.InputQueueCharacters.Count;
            Span<char> chars = stackalloc char[2];
            Span<byte> utf8 = stackalloc byte[4];
            for (int i = 0; i < count; i++)
            {
                char ch = (char)io.InputQueueCharacters[i];

                // Astral-plane input arrives as two queue entries (a UTF-16
                // surrogate pair); encode the pair as one code point — encoding
                // a lone half yields U+FFFD.
                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 < count && char.IsLowSurrogate((char)io.InputQueueCharacters[i + 1]))
                    {
                        chars[0] = ch;
                        chars[1] = (char)io.InputQueueCharacters[++i];
                        Send(session, utf8[..System.Text.Encoding.UTF8.GetBytes(chars, utf8)]);
                    }

                    continue; // unpaired high surrogate: drop
                }

                if (char.IsLowSurrogate(ch) || ch < 32 || ch == 127)
                {
                    continue;
                }

                chars[0] = ch;
                Send(session, utf8[..System.Text.Encoding.UTF8.GetBytes(chars[..1], utf8)]);
            }
        }
    }

    private void HandleMouse(TerminalSession session, ImGuiIOPtr io, float2 canvasPos, int cols, int rows, bool gridHovered)
    {
        // Shift overrides app mouse tracking (xterm/ghostty behavior): holding
        // Shift while an app tracks the mouse falls through to the normal
        // selection/context-menu path — otherwise there is no way to select and
        // copy text inside tmux/nvim.
        if (session.Surface.IsMouseTrackingEnabled && !io.KeyShift)
        {
            HandleAppMouse(session, io, canvasPos, cols, rows, gridHovered);
            return;
        }

        // Wheel scrolls the viewport scrollback when the app isn't tracking the mouse.
        if (gridHovered && io.MouseWheel != 0)
        {
            session.Surface.ScrollBy(-(int)Math.Round(io.MouseWheel * 3));
        }

        var cell = MouseCell(canvasPos, cols, rows);

        // Selection gestures: single-click+drag selects cells, double-click selects
        // a word, triple-click selects the logical line. A plain click that never
        // drags clears the selection (it falls through to ClearSelection on press
        // and the extend branch never fires), matching real terminals — without
        // this, holding the button for a frame painted a one-cell selection that
        // could never be deselected.
        if (gridHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            int clicks = ImGui.GetMouseClickedCount(ImGuiMouseButton.Left);
            if (clicks >= 3)
            {
                session.Surface.SelectLine(cell);
                _selecting = false;
            }
            else if (clicks == 2)
            {
                session.Surface.SelectWord(cell);
                _selecting = false;
            }
            else
            {
                // Clear now and record the anchor, but defer materializing the
                // selection until the mouse actually drags past the threshold.
                session.Surface.ClearSelection();
                session.Surface.BeginSelectCells(cell);
                _selecting = true;
            }
        }
        else if (_selecting && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            AutoScrollForDrag(session, canvasPos, rows);
            session.Surface.ExtendSelectCells(cell);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _selecting = false;
        }

        // Right-click opens the copy/paste context menu over the grid.
        if (gridHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            ImGui.OpenPopup(GridContextMenuId);
        }
    }

    // Right-click context menu over the grid: Copy (enabled only with a live
    // selection) and Paste. Opened from HandleMouse and rendered every frame so
    // the popup survives the mouse moving off the grid onto the menu itself.
    private void DrawContextMenu()
    {
        if (!ImGui.BeginPopup(GridContextMenuId))
        {
            return;
        }

        // Cheap native no-value probe; extracting the full selection text every
        // popup frame just for an enable-bool allocated the whole selection.
        bool hasSelection = Sessions.ActiveSession?.Surface.HasSelection == true;
        if (ImGui.MenuItem("Copy", "Ctrl+Shift+C", false, hasSelection))
        {
            CopySelectionToClipboard();
        }

        if (ImGui.MenuItem("Paste", "Ctrl+Shift+V"))
        {
            PasteFromClipboard();
        }

        ImGui.EndPopup();
    }

    private void HandleAppMouse(TerminalSession session, ImGuiIOPtr io, float2 canvasPos, int cols, int rows, bool hovered)
    {
        // libghostty's mouse encoder expects surface-local pixels (0,0 = top-left of
        // the terminal grid), not ImGui's screen-global mouse position.
        var rawPos = ImGui.GetMousePos() - canvasPos;
        var mods = ReadModifiers(io);

        // Track which cell the pointer is over so motion below can fire only on a
        // cell change. Updated every frame (incl. button-edge frames) so the next
        // move is measured from the current cell, never a stale one. The cell is
        // computed with the *real* (fractional) cell metrics.
        int col = Math.Clamp((int)(rawPos.X / _cellWidth), 0, Math.Max(0, cols - 1));
        int row = Math.Clamp((int)(rawPos.Y / _cellHeight), 0, Math.Max(0, rows - 1));
        bool cellChanged = col != _appMouseCol || row != _appMouseRow;
        _appMouseCol = col;
        _appMouseRow = row;

        // The engine maps pixels to cells by dividing by *integer* cell metrics
        // (the ones pushed via SetMouseGeometry). Raw float pixels disagree with
        // that division whenever the real cell width is fractional — the error
        // grows with x, so clicks land columns off on wide grids. Synthesizing
        // the position from the frontend-computed cell (its center in integer
        // metrics) makes frontend and engine agree by construction.
        int cellW = Math.Max(1, (int)_cellWidth);
        int cellH = Math.Max(1, (int)_cellHeight);
        var pos = new float2(col * cellW + cellW * 0.5f, row * cellH + cellH * 0.5f);

        // Press is gated on hover (the click must land on the grid); release fires
        // ungated so a drag that ends off-grid still reports button-up — but only
        // when the matching press was forwarded, so a click that started on the
        // game UI cannot leak a spurious release report.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _appMousePressSent[(int)MouseButton.Left] = true;
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Left, mods, pos);
        }
        else if (_appMousePressSent[(int)MouseButton.Left] && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _appMousePressSent[(int)MouseButton.Left] = false;
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Left, mods, pos);
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            _appMousePressSent[(int)MouseButton.Right] = true;
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Right, mods, pos);
        }
        else if (_appMousePressSent[(int)MouseButton.Right] && ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            _appMousePressSent[(int)MouseButton.Right] = false;
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Right, mods, pos);
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
        {
            _appMousePressSent[(int)MouseButton.Middle] = true;
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Middle, mods, pos);
        }
        else if (_appMousePressSent[(int)MouseButton.Middle] && ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
        {
            _appMousePressSent[(int)MouseButton.Middle] = false;
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Middle, mods, pos);
        }
        else if (cellChanged)
        {
            // Motion / drag reporting. Apps in button-event (1002) or any-event
            // (1003) tracking expect mouse-move reports so drag-driven UIs (nvim
            // visual-select, tmux pane-resize) update live instead of only on
            // release. The engine's mouse encoder is mode-aware — it emits a report
            // only when the active mode wants this motion (and encodes the held
            // button into the drag code, or "no button" for hover), and drops it
            // otherwise (e.g. normal 1000 tracking) — so we just offer each cell
            // crossing and let the encoder decide. We only report when a button is
            // held (a drag) or the pointer is over the grid (any-event hover),
            // mirroring the press/release gating above.
            var held = HeldMouseButton();
            if (held != MouseButton.None || hovered)
            {
                EncodeMouseAndSend(session, MouseAction.Motion, held, mods, pos);
            }
        }

        // Wheel reports as scroll-button presses (libghostty buttons 4/5), one
        // per wheel notch so fast flicks scroll proportionally.
        if (hovered && io.MouseWheel != 0)
        {
            var button = io.MouseWheel > 0 ? MouseButton.ScrollUp : MouseButton.ScrollDown;
            int notches = Math.Clamp((int)Math.Round(Math.Abs(io.MouseWheel)), 1, 10);
            for (int i = 0; i < notches; i++)
            {
                EncodeMouseAndSend(session, MouseAction.Press, button, mods, pos);
            }
        }
    }

    // The button currently held during a drag, used as the motion report's button
    // code. Left/Middle/Right priority matches the press handling; None means no
    // button is down (an any-event hover motion).
    private static MouseButton HeldMouseButton()
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return MouseButton.Left;
        if (ImGui.IsMouseDown(ImGuiMouseButton.Middle)) return MouseButton.Middle;
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right)) return MouseButton.Right;
        return MouseButton.None;
    }

    // While dragging a selection, scroll the viewport when the cursor leaves the
    // grid vertically. Speed accelerates with how far past the edge the cursor is
    // (capped), so a small overshoot creeps and a big one races.
    private void AutoScrollForDrag(TerminalSession session, float2 canvasPos, int rows)
    {
        float mouseY = ImGui.GetMousePos().Y;
        float top = canvasPos.Y;
        float bottom = canvasPos.Y + rows * _cellHeight;

        if (mouseY < top)
        {
            session.Surface.ScrollBy(-AutoScrollStep(top - mouseY));
        }
        else if (mouseY > bottom)
        {
            session.Surface.ScrollBy(AutoScrollStep(mouseY - bottom));
        }
    }

    private int AutoScrollStep(float overflowPixels)
        => Math.Clamp(1 + (int)(overflowPixels / Math.Max(1f, _cellHeight)), 1, 5);

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

    /// <summary>
    /// Debug overlay (top-right of the grid): frame-build vs ImGui-submit cost,
    /// dirty state, PTY throughput, and draw-call counts from the renderer.
    /// </summary>
    private void DrawPerfHud(
        TerminalSession session,
        float2 canvasPos,
        float2 avail,
        int cols,
        int rows,
        double buildMs,
        double submitMs,
        GridRenderStats stats)
    {
        var frameStats = (session.Surface as GhosttyTerminalSurface)?.LastFrameStats ?? default;

        _hudAccumBytes += frameStats.BytesConsumed;
        if (_hudWindowStartTs == 0)
        {
            _hudWindowStartTs = Stopwatch.GetTimestamp();
        }

        var elapsed = Stopwatch.GetElapsedTime(_hudWindowStartTs);
        if (elapsed.TotalMilliseconds >= 500)
        {
            _hudBytesPerSec = _hudAccumBytes / elapsed.TotalSeconds;
            _hudAccumBytes = 0;
            _hudWindowStartTs = Stopwatch.GetTimestamp();
        }

        string state = frameStats.SyncPaused
            ? "sync-hold"
            : frameStats.DirtyState switch { 0 => "clean", 1 => "partial", _ => "full" };

        // ImString interpolation writes UTF-8 into the binding's per-frame
        // shared buffer — zero managed allocation. The HUD exists to observe
        // the render path; it must not perturb it with three strings per frame.
        ImString l1 = $"grid {cols}x{rows}  build {buildMs:F2}ms (vt {frameStats.WriteMs:F2} upd {frameStats.UpdateMs:F2} pop {frameStats.PopulateMs:F2})";
        ImString l2 = $"submit {submitMs:F2}ms  state {state}  in {_hudBytesPerSec / 1048576.0:F2} MB/s";
        ImString l3 = $"draws bg:{stats.BackgroundRects} blk:{stats.BlockRects} runs:{stats.GlyphRuns} cell:{stats.GlyphCells} deco:{stats.DecorationLines} = {stats.TotalCalls}";

        float lineH = ImGui.GetTextLineHeight();
        float w = Math.Max(ImGui.CalcTextSize(l1).X, Math.Max(ImGui.CalcTextSize(l2).X, ImGui.CalcTextSize(l3).X));
        const float pad = 6f;

        var drawList = ImGui.GetWindowDrawList();
        var p0 = new float2(canvasPos.X + avail.X - w - pad * 2 - 4f, canvasPos.Y + 4f);
        var p1 = new float2(p0.X + w + pad * 2, p0.Y + lineH * 3 + pad * 2);
        drawList.AddRectFilled(p0, p1, 0xB0000000u);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad), 0xFF7FFF7Fu, l1);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad + lineH), 0xFF7FDFFFu, l2);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad + lineH * 2), 0xFFFFBF7Fu, l3);
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

    private GridPoint MouseCell(float2 canvasPos, int cols, int rows)
    {
        var mouse = ImGui.GetMousePos();
        int col = Math.Clamp((int)((mouse.X - canvasPos.X) / _cellWidth), 0, Math.Max(0, cols - 1));
        int row = Math.Clamp((int)((mouse.Y - canvasPos.Y) / _cellHeight), 0, Math.Max(0, rows - 1));
        return new GridPoint(col, row);
    }

    private void EncodeAndSend(TerminalSession session, in TerminalKeyEvent keyEvent)
    {
        Span<byte> buf = stackalloc byte[64];
        int n = session.Surface.EncodeKey(keyEvent, buf);
        if (n > 0)
        {
            Send(session, buf[..n]);
        }
    }

    private void EncodeMouseAndSend(TerminalSession session, MouseAction action, MouseButton button, TKeyMods mods, float2 pos)
    {
        var ev = new TerminalMouseEvent
        {
            Action = action,
            Button = button,
            Modifiers = mods,
            X = pos.X,
            Y = pos.Y,
        };
        Span<byte> buf = stackalloc byte[64];
        int n = session.Surface.EncodeMouse(ev, buf);
        if (n > 0)
        {
            Send(session, buf[..n]);
        }
    }

    private void Send(TerminalSession session, ReadOnlySpan<byte> bytes)
    {
        session.SendInput(bytes);
        InputSent?.Invoke();
    }

    private static TKeyMods ReadModifiers(ImGuiIOPtr io)
    {
        var mods = TKeyMods.None;
        if (io.KeyShift) mods |= TKeyMods.Shift;
        if (io.KeyCtrl) mods |= TKeyMods.Ctrl;
        if (io.KeyAlt) mods |= TKeyMods.Alt;
        return mods;
    }

    public bool CopySelectionToClipboard()
    {
        var text = Sessions.ActiveSession?.Surface.GetSelectionText();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        ImGui.SetClipboardText(text);
        return true;
    }

    public void PasteFromClipboard()
    {
        var session = Sessions.ActiveSession;
        if (session == null)
        {
            return;
        }

        var text = ImGui.GetClipboardText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var encoded = session.Surface.EncodePaste(System.Text.Encoding.UTF8.GetBytes(text));
        Send(session, encoded);
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

    private void ComputeCellMetrics(ImFontPtr font, float fontSize)
    {
        ImGui.PushFont(font, fontSize);
        var size = ImGui.CalcTextSize("M");
        ImGui.PopFont();

        _cellWidth = size.X > 0.5f ? size.X : fontSize * 0.6f;
        _cellHeight = size.Y > 0.5f ? size.Y : fontSize * 1.2f;
    }

    // Cached font resolution + ASCII run-batch validation. Recomputed when the
    // family/size changes or when more fonts finish loading (variant fallbacks
    // can resolve differently once their font appears).
    private readonly GlyphBatchCache _glyphBatchCache = new();
    private FrameFonts _cachedFonts;
    private bool _hasCachedFonts;
    private string? _cachedFontFamily;
    private float _cachedFontSize;
    private int _cachedLoadedFontCount;

    private FrameFonts ResolveFontsCached(float fontSize)
    {
        int loadedCount = PurrTTYFontManager.LoadedFonts.Count;
        if (_hasCachedFonts
            && _cachedFontFamily == Settings.FontFamily
            && _cachedFontSize == fontSize
            && _cachedLoadedFontCount == loadedCount)
        {
            return _cachedFonts;
        }

        var fonts = ResolveFonts();
        ComputeCellMetrics(fonts.Regular, fontSize);
        _glyphBatchCache.Clear();
        _cachedFonts = new FrameFonts(
            fonts.Regular, fonts.Bold, fonts.Italic, fonts.BoldItalic,
            IsAsciiMonospace(fonts.Regular, fontSize, _cellWidth),
            IsAsciiMonospace(fonts.Bold, fontSize, _cellWidth),
            IsAsciiMonospace(fonts.Italic, fontSize, _cellWidth),
            IsAsciiMonospace(fonts.BoldItalic, fontSize, _cellWidth),
            _glyphBatchCache);
        _hasCachedFonts = true;
        _cachedFontFamily = Settings.FontFamily;
        _cachedFontSize = fontSize;
        _cachedLoadedFontCount = loadedCount;
        return _cachedFonts;
    }

    private static readonly string AsciiSample = CreateAsciiSample();

    // Includes ' ' (0x20): run batching bridges blank cells with spaces, so the
    // space advance must be validated along with the printable glyphs.
    private static string CreateAsciiSample()
    {
        Span<char> chars = stackalloc char[95];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)(' ' + i);
        }

        return new string(chars);
    }

    // A variant may batch ASCII runs only when its measured printable-ASCII
    // advance matches the grid cell width exactly; otherwise the renderer
    // falls back to per-cell placement to keep columns aligned.
    private static bool IsAsciiMonospace(ImFontPtr font, float fontSize, float cellWidth)
    {
        ImGui.PushFont(font, fontSize);
        var size = ImGui.CalcTextSize(AsciiSample);
        ImGui.PopFont();
        return Math.Abs(size.X - AsciiSample.Length * cellWidth) <= 0.5f;
    }

    private FrameFonts ResolveFonts()
    {
        var config = PurrTTYFontManager.CreateFontConfigForFamily(Settings.FontFamily);
        var regular = ResolveFontByName(config.RegularFontName) ?? ImGui.GetFont();
        var bold = ResolveFontByName(config.BoldFontName) ?? regular;
        var italic = ResolveFontByName(config.ItalicFontName) ?? regular;
        var boldItalic = ResolveFontByName(config.BoldItalicFontName) ?? regular;
        return new FrameFonts(regular, bold, italic, boldItalic);
    }

    private static ImFontPtr? ResolveFontByName(string? name)
        => !string.IsNullOrEmpty(name) && PurrTTYFontManager.LoadedFonts.TryGetValue(name, out var font)
            ? font
            : null;

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
