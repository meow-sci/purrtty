using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Display.Configuration;
using purrTTY.Display.Controllers;
using purrTTY.Display.Rendering;
using purrTTY.Display.Types;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;
using PurrTTY.Terminal.Sessions;
using TerminalTheme = PurrTTY.Terminal.TerminalTheme;
using TKeyMods = PurrTTY.Terminal.Input.KeyModifiers;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// A clean ImGui terminal controller driven entirely by the renderer-neutral
/// libghostty-vt backend. Each frame it ticks the active session's surface
/// (<c>BuildFrame</c>), draws the resulting <see cref="TerminalFrame"/>, and
/// forwards input through the surface (keys/mouse encoded by the engine,
/// selection via engine gestures). Replaces the legacy emulator-coupled
/// controller; advanced chrome (settings panel, opacity, hover-hide,
/// window-state persistence) is intentionally deferred.
/// </summary>
public sealed class GhosttyTerminalController : ITerminalController
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

    private readonly SessionManager _sessionManager;
    private readonly TerminalFontConfig _fontConfig;
    private readonly TerminalTheme _theme;
    private readonly RgbaColor _selectionColor = new(0x33, 0x55, 0x88, 0xAA);

    private bool _isVisible;
    private bool _hasFocus;
    private bool _wantFocus;
    private bool _disposed;
    private TextSelection _selection;

    private float _cellWidth = 8f;
    private float _cellHeight = 16f;

    private bool _selecting;

    private double _blinkTimer;
    private bool _cursorOn = true;

    private static volatile bool _anyTerminalActive;

    /// <summary>
    /// True when a terminal is visible and focused. Used by the host's
    /// <c>KSA.Program.OnKey</c> Harmony patch to suppress game key handling while
    /// the terminal is capturing input.
    /// </summary>
    public static bool IsAnyTerminalActive => _anyTerminalActive;

    /// <summary>Optional host hook to suppress keyboard input for a frame (e.g. when the toggle hotkey fires).</summary>
    public Func<bool>? KeyboardSuppression { get; set; }

    public event EventHandler<DataInputEventArgs>? DataInput;
    public event EventHandler<FocusChangedEventArgs>? FocusChanged;

    public GhosttyTerminalController(SessionManager sessionManager, TerminalFontConfig fontConfig)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _fontConfig = fontConfig ?? throw new ArgumentNullException(nameof(fontConfig));
        _theme = DefaultTheme.Create();

        foreach (var session in _sessionManager.Sessions)
        {
            WireSession(session);
        }

        _sessionManager.SessionCreated += (_, e) => WireSession(e.Session);
    }

    private void WireSession(TerminalSession session)
    {
        session.Surface.SetTheme(_theme);
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

    public bool IsVisible
    {
        get => _isVisible;
        set => _isVisible = value;
    }

    public bool HasFocus => _hasFocus;
    public bool IsInputCaptureActive => _isVisible && _hasFocus;

    public bool ShouldCaptureInput() => _isVisible && _hasFocus;

    public void ForceFocus() => _wantFocus = true;

    public void Update(float deltaTime)
    {
        _blinkTimer += deltaTime;
        if (_blinkTimer >= 0.53)
        {
            _blinkTimer = 0;
            _cursorOn = !_cursorOn;
        }
    }

    public void Render()
    {
        if (!_isVisible || _disposed)
        {
            _anyTerminalActive = false;
            return;
        }

        var fonts = ResolveFonts();
        float fontSize = _fontConfig.FontSize;

        var bg = _theme.DefaultBackground;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new float4(bg.R / 255f, bg.G / 255f, bg.B / 255f, 0.95f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0f, 0f));

        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.MenuBar;

        if (_wantFocus)
        {
            ImGui.SetNextWindowFocus();
            _wantFocus = false;
        }

        ImGui.Begin("purrTTY", ref _isVisible, flags);
        ImGui.PopStyleVar();

        UpdateFocusState(ImGui.IsWindowFocused());
        _anyTerminalActive = _hasFocus;
        RenderMenuBar();

        var canvasPos = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        ComputeCellMetrics(fonts.Regular, fontSize);

        int cols = Math.Max(1, (int)(avail.X / _cellWidth));
        int rows = Math.Max(1, (int)(avail.Y / _cellHeight));

        var session = _sessionManager.ActiveSession;
        if (session != null)
        {
            if (cols != session.Surface.Cols || rows != session.Surface.Rows)
            {
                session.Surface.Resize(cols, rows, (int)_cellWidth, (int)_cellHeight);
                session.UpdateTerminalDimensions(cols, rows);
                session.ProcessManager.Resize(cols, rows);
                _sessionManager.UpdateLastKnownTerminalDimensions(cols, rows);
            }

            session.Surface.SetMouseGeometry(
                (int)(cols * _cellWidth), (int)(rows * _cellHeight), (int)_cellWidth, (int)_cellHeight);

            // Reserve the whole canvas with an invisible button BEFORE drawing the
            // grid. This is essential: the window has no title bar, so without an
            // item under the cursor ImGui treats a click-drag on the body as a
            // window-move gesture — text selection never starts and the window
            // slides around instead. The button also yields reliable hover/active
            // state for the mouse handlers. The grid is painted on top via the draw
            // list, so the button stays invisible.
            bool termHovered = false;
            bool termActive = false;
            if (avail.X >= 1f && avail.Y >= 1f)
            {
                ImGui.InvisibleButton("terminal_canvas", avail);
                termHovered = ImGui.IsItemHovered();
                termActive = ImGui.IsItemActive();
            }

            var frame = session.Surface.BuildFrame();
            bool cursorOn = !frame.Cursor.Blinking || _cursorOn;

            FrameGridRenderer.Render(
                frame, ImGui.GetWindowDrawList(), canvasPos,
                _cellWidth, _cellHeight, fonts, fontSize, _selectionColor, cursorOn);

            var io = ImGui.GetIO();
            if (_hasFocus)
            {
                HandleKeyboard(session, io);
            }

            // Mouse is gated on hover/active (not focus) so the first click both
            // selects and focuses in one gesture, as it did pre-libghostty.
            HandleMouse(session, io, canvasPos, cols, rows, termHovered, termActive);
        }

        ImGui.End();
        ImGui.PopStyleColor();
    }

    private void RenderMenuBar()
    {
        if (!ImGui.BeginMenuBar())
        {
            return;
        }

        var sessions = _sessionManager.Sessions;
        if (sessions.Count <= 1)
        {
            ImGui.TextDisabled(_sessionManager.ActiveSession?.Title ?? "purrTTY");
        }
        else
        {
            var active = _sessionManager.ActiveSession;
            foreach (var session in sessions)
            {
                bool isActive = ReferenceEquals(session, active);
                if (ImGui.MenuItem(session.Title, "", isActive) && !isActive)
                {
                    _sessionManager.SwitchToSession(session.Id);
                }
            }
        }

        ImGui.EndMenuBar();
    }

    private void HandleKeyboard(TerminalSession session, ImGuiIOPtr io)
    {
        if (KeyboardSuppression?.Invoke() == true)
        {
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

    private void HandleMouse(TerminalSession session, ImGuiIOPtr io, float2 canvasPos, int cols, int rows, bool hovered, bool active)
    {
        if (session.Surface.IsMouseTrackingEnabled)
        {
            HandleAppMouse(session, io, canvasPos, hovered);
            return;
        }

        // Wheel scrolls the viewport scrollback when the app isn't tracking the mouse.
        if (hovered && io.MouseWheel != 0)
        {
            session.Surface.ScrollBy(-(int)Math.Round(io.MouseWheel * 3));
        }

        var cell = MouseCell(canvasPos, cols, rows);

        // Selection gestures: single-click+drag selects cells, double-click selects
        // a word, triple-click selects the logical line.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
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
        else if (_selecting && active)
        {
            // The canvas button stays "active" from press to release (even when the
            // cursor leaves the grid), so the drag tracks correctly off-screen.
            AutoScrollForDrag(session, canvasPos, rows);
            session.Surface.ExtendSelectCells(cell);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            _selecting = false;
        }

        // Right-click copies the current selection.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CopySelectionToClipboard();
        }
    }

    private void HandleAppMouse(TerminalSession session, ImGuiIOPtr io, float2 canvasPos, bool hovered)
    {
        // libghostty's mouse encoder expects surface-local pixels (0,0 = top-left of
        // the terminal grid), not ImGui's screen-global mouse position.
        var pos = ImGui.GetMousePos() - canvasPos;
        var mods = ReadModifiers(io);

        // Press is gated on hover (the click must land on the grid); release fires
        // unconditionally so a drag that ends off-grid still reports button-up.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Left, mods, pos);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Left, mods, pos);
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Right, mods, pos);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Right))
        {
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Right, mods, pos);
        }
        else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
        {
            EncodeMouseAndSend(session, MouseAction.Press, MouseButton.Middle, mods, pos);
        }
        else if (ImGui.IsMouseReleased(ImGuiMouseButton.Middle))
        {
            EncodeMouseAndSend(session, MouseAction.Release, MouseButton.Middle, mods, pos);
        }

        // Wheel reports as a scroll-button press (libghostty buttons 4/5).
        if (hovered && io.MouseWheel != 0)
        {
            var button = io.MouseWheel > 0 ? MouseButton.ScrollUp : MouseButton.ScrollDown;
            EncodeMouseAndSend(session, MouseAction.Press, button, mods, pos);
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
        DataInput?.Invoke(this, new DataInputEventArgs(bytes.ToArray()));
    }

    private static TKeyMods ReadModifiers(ImGuiIOPtr io)
    {
        var mods = TKeyMods.None;
        if (io.KeyShift) mods |= TKeyMods.Shift;
        if (io.KeyCtrl) mods |= TKeyMods.Ctrl;
        if (io.KeyAlt) mods |= TKeyMods.Alt;
        return mods;
    }

    private void UpdateFocusState(bool nowFocused)
    {
        if (nowFocused == _hasFocus)
        {
            return;
        }

        bool previous = _hasFocus;
        _hasFocus = nowFocused;
        FocusChanged?.Invoke(this, new FocusChangedEventArgs(nowFocused, previous));
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
        var regular = ResolveFontByName(_fontConfig.RegularFontName) ?? ImGui.GetFont();
        var bold = ResolveFontByName(_fontConfig.BoldFontName) ?? regular;
        var italic = ResolveFontByName(_fontConfig.ItalicFontName) ?? regular;
        var boldItalic = ResolveFontByName(_fontConfig.BoldItalicFontName) ?? regular;
        return new FrameFonts(regular, bold, italic, boldItalic);
    }

    private static ImFontPtr? ResolveFontByName(string? name)
        => !string.IsNullOrEmpty(name) && PurrTTYFontManager.LoadedFonts.TryGetValue(name, out var font)
            ? font
            : null;

    public (int width, int height) GetTerminalDimensions()
    {
        var session = _sessionManager.ActiveSession;
        return session != null ? (session.Surface.Cols, session.Surface.Rows) : (80, 24);
    }

    public void ResizeTerminal(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0)
        {
            throw new ArgumentException("Terminal dimensions must be positive.");
        }

        var session = _sessionManager.ActiveSession;
        if (session == null)
        {
            return;
        }

        session.Surface.Resize(cols, rows, (int)_cellWidth, (int)_cellHeight);
        session.ProcessManager.Resize(cols, rows);
        session.UpdateTerminalDimensions(cols, rows);
    }

    public TextSelection GetCurrentSelection() => _selection;

    public void SetSelection(TextSelection selection) => _selection = selection;

    public bool CopySelectionToClipboard()
    {
        var text = _sessionManager.ActiveSession?.Surface.GetSelectionText();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        ImGui.SetClipboardText(text);
        return true;
    }

    public void PasteFromClipboard()
    {
        var session = _sessionManager.ActiveSession;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _anyTerminalActive = false;
        _sessionManager.Dispose();
    }
}
