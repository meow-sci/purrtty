using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using purrTTY.Logging;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;
using PurrTTY.Terminal.Sessions;
using TerminalTheme = PurrTTY.Terminal.TerminalTheme;
using TKeyMods = PurrTTY.Terminal.Input.KeyModifiers;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// The live display settings of one terminal window. Mutated by the game menus
/// (font/opacity sliders, theme application) and snapshotted by "save current
/// settings as theme".
/// </summary>
public sealed class TerminalWindowSettings
{
    public string ThemeName { get; set; } = "Default";
    public ThemeColors Colors { get; set; } = new();
    public string FontFamily { get; set; } = "Hack";
    public float FontSize { get; set; } = 32f;
    public float BackgroundOpacity { get; set; } = 1f;
    public float ForegroundOpacity { get; set; } = 1f;
    public float CellBackgroundOpacity { get; set; } = 1f;
}

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

    /// <summary>Pixels around the window rect that still count as "hovering" (covers resize grips).</summary>
    private const float HoverMargin = 8f;

    private readonly string _imguiName;

    private TerminalTheme _engineTheme;
    private RgbaColor _selectionColor;

    private bool _hasFocus;
    private bool _wantFocus;
    private bool _wasHoveredLastFrame;
    private bool _selecting;
    private bool _hadSessions;
    private bool _disposed;
    private Guid? _lastActiveSessionId;
    private (float2 Pos, float2 Size)? _pendingPlacement;

    private float _cellWidth = 8f;
    private float _cellHeight = 16f;

    public int Id { get; }
    public SessionManager Sessions { get; }
    public TerminalWindowSettings Settings { get; }

    /// <summary>False once the window has been closed; the controller disposes it.</summary>
    public bool IsOpen { get; private set; } = true;

    public bool HasFocus => _hasFocus;
    public string Title => Sessions.ActiveSession?.Title ?? "purrTTY";

    public float2 LastKnownPosition { get; private set; }
    public float2 LastKnownSize { get; private set; }
    public bool HasObservedGeometry { get; private set; }

    /// <summary>Host hook to suppress keyboard input for a frame (e.g. when the toggle hotkey fires).</summary>
    public Func<bool>? KeyboardSuppression { get; set; }

    /// <summary>Raised when this window gains or loses ImGui focus.</summary>
    public event Action<TerminalWindow, bool>? FocusChanged;

    /// <summary>Raised with the raw bytes of any user input sent to the active session.</summary>
    public event Action<byte[]>? DataInput;

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

        Sessions.SessionCreated += (_, e) => WireSession(e.Session);
    }

    public void RequestFocus() => _wantFocus = true;

    /// <summary>Closes the window; its sessions are disposed by the controller.</summary>
    public void Close() => IsOpen = false;

    /// <summary>
    /// Applies a theme to this window. Colors always apply; font and opacity
    /// values only apply when the theme defines them (user-saved themes do,
    /// bundled color schemes keep the window's current values).
    /// </summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        Settings.ThemeName = theme.Name;
        Settings.Colors = theme.Colors.Clone();

        if (theme.FontFamily is { } family)
        {
            Settings.FontFamily = family;
        }

        if (theme.FontSize is { } size)
        {
            Settings.FontSize = Math.Clamp(size, Controllers.LayoutConstants.MIN_FONT_SIZE, Controllers.LayoutConstants.MAX_FONT_SIZE);
        }

        if (theme.BackgroundOpacity is { } bg)
        {
            Settings.BackgroundOpacity = Math.Clamp(bg, 0f, 1f);
        }

        if (theme.ForegroundOpacity is { } fg)
        {
            Settings.ForegroundOpacity = Math.Clamp(fg, 0f, 1f);
        }

        if (theme.CellBackgroundOpacity is { } cell)
        {
            Settings.CellBackgroundOpacity = Math.Clamp(cell, 0f, 1f);
        }

        PushThemeToSessions();
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
    };

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

        var fonts = ResolveFonts();
        float fontSize = Settings.FontSize;
        bool showChrome = !hideChromeWhenNotHovered || _wasHoveredLastFrame;

        var bg = Settings.Colors.Background;
        var windowBg = showChrome
            ? new float4(bg.R / 255f, bg.G / 255f, bg.B / 255f, Settings.BackgroundOpacity)
            : new float4(0f, 0f, 0f, 0f);

        int colorPushes = 1;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, windowBg);
        if (!showChrome)
        {
            // The menu-bar strip background is part of the window decorations
            // drawn during Begin, so it has to be hidden via style color here.
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new float4(0f, 0f, 0f, 0f));
            colorPushes++;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, showChrome ? 1f : 0f);

        if (_pendingPlacement is { } placement)
        {
            ImGui.SetNextWindowPos(placement.Pos, ImGuiCond.Always);
            ImGui.SetNextWindowSize(placement.Size, ImGuiCond.Always);
            _pendingPlacement = null;
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

        ComputeCellMetrics(fonts.Regular, fontSize);

        int cols = Math.Max(1, (int)(avail.X / _cellWidth));
        int rows = Math.Max(1, (int)(avail.Y / _cellHeight));

        var session = Sessions.ActiveSession;
        if (session != null)
        {
            _hadSessions = true;

            if (cols != session.Surface.Cols || rows != session.Surface.Rows)
            {
                session.Surface.Resize(cols, rows, (int)_cellWidth, (int)_cellHeight);
                session.UpdateTerminalDimensions(cols, rows);
                session.ProcessManager.Resize(cols, rows);
                Sessions.UpdateLastKnownTerminalDimensions(cols, rows);
            }

            session.Surface.SetMouseGeometry(
                (int)(cols * _cellWidth), (int)(rows * _cellHeight), (int)_cellWidth, (int)_cellHeight);

            var frame = session.Surface.BuildFrame();
            bool cursorOn = !frame.Cursor.Blinking || cursorBlinkOn;

            FrameGridRenderer.Render(
                frame, ImGui.GetWindowDrawList(), canvasPos,
                _cellWidth, _cellHeight, fonts, fontSize, _selectionColor, cursorOn,
                Settings.ForegroundOpacity, Settings.CellBackgroundOpacity);
        }
        else if (_hadSessions && Sessions.SessionCount == 0)
        {
            // The last tab was closed; retire the window.
            IsOpen = false;
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

        // Hover state feeding next frame's chrome visibility: mouse anywhere over
        // the window rect (plus a margin for resize grips), or an in-progress drag.
        var mouse = ImGui.GetMousePos();
        bool mouseInBounds =
            mouse.X >= windowPos.X - HoverMargin && mouse.X <= windowPos.X + windowSize.X + HoverMargin &&
            mouse.Y >= windowPos.Y - HoverMargin && mouse.Y <= windowPos.Y + windowSize.Y + HoverMargin;
        _wasHoveredLastFrame = mouseInBounds || _selecting;

        ImGui.End();
        ImGui.PopStyleColor(colorPushes);
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
            foreach (var session in sessions)
            {
                bool isActive = active != null && session.Id == active.Id;
                string label = session.Title;
                if (session.ProcessManager.ExitCode is { } exitCode)
                {
                    label += $" (exit {exitCode})";
                }

                var tabFlags = isActive && activeChanged ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                bool open = true;
                if (ImGui.BeginTabItem($"{label}##tab_{session.Id}", ref open, tabFlags))
                {
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

    private void CloseSession(Guid sessionId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Sessions.CloseSessionAsync(sessionId);
            }
            catch (Exception ex)
            {
                ModLog.Log.Debug($"TerminalWindow: failed to close session {sessionId}: {ex.Message}");
            }
        });
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

        if (io.KeyCtrl)
        {
            foreach (var (imguiKey, key) in LetterKeys)
            {
                if (ImGui.IsKeyPressed(imguiKey))
                {
                    EncodeAndSend(session, new TerminalKeyEvent(key, KeyAction.Press, mods));
                }
            }
        }

        // Printable text. Skip when Ctrl/Alt are held (those are command combos
        // handled above and do not represent typed text).
        if (!io.KeyCtrl && !io.KeyAlt && io.InputQueueCharacters.Count > 0)
        {
            for (int i = 0; i < io.InputQueueCharacters.Count; i++)
            {
                char ch = (char)io.InputQueueCharacters[i];
                if (ch >= 32 && ch != 127)
                {
                    Send(session, System.Text.Encoding.UTF8.GetBytes(ch.ToString()));
                }
            }
        }
    }

    private void HandleMouse(TerminalSession session, ImGuiIOPtr io, float2 canvasPos, int cols, int rows, bool gridHovered)
    {
        // Wheel scrolling (viewport scrollback when the app isn't tracking the mouse).
        if (gridHovered && io.MouseWheel != 0 && !session.Surface.IsMouseTrackingEnabled)
        {
            session.Surface.ScrollBy(-(int)Math.Round(io.MouseWheel * 3));
        }

        var cell = MouseCell(canvasPos, cols, rows);

        if (session.Surface.IsMouseTrackingEnabled)
        {
            if (gridHovered || _selecting)
            {
                HandleAppMouse(session, io, cell);
            }

            return;
        }

        // Selection gestures: single-click+drag selects cells, double-click selects
        // a word, triple-click selects the logical line.
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
                session.Surface.ClearSelection();
                session.Surface.BeginSelectCells(cell);
                _selecting = true;
            }
        }
        else if (_selecting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            AutoScrollForDrag(session, canvasPos, rows);
            session.Surface.ExtendSelectCells(cell);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _selecting = false;
        }

        // Right-click copies the current selection.
        if (gridHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CopySelectionToClipboard();
        }
    }

    private void HandleAppMouse(TerminalSession session, ImGuiIOPtr io, GridPoint cell)
    {
        var pos = ImGui.GetMousePos();
        var mods = ReadModifiers(io);

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Left, mods, pos);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Left, mods, pos);
        }
        else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Right, mods, pos);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Right, mods, pos);
        }

        if (io.MouseWheel != 0)
        {
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Middle, mods, pos);
        }
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
        DataInput?.Invoke(bytes.ToArray());
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

    private FrameFonts ResolveFonts()
    {
        var config = PurrTTYFontManager.CreateFontConfigForFamily(Settings.FontFamily, Settings.FontSize);
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
