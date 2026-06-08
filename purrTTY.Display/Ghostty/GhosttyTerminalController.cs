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
    private GridPoint _selectAnchor;

    private double _blinkTimer;
    private bool _cursorOn = true;

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
            session.Surface.SetTheme(_theme);
        }

        _sessionManager.SessionCreated += (_, e) => e.Session.Surface.SetTheme(_theme);
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
            return;
        }

        var font = ResolveFont();
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
        RenderMenuBar();

        var canvasPos = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail();

        ComputeCellMetrics(font, fontSize);

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

            var frame = session.Surface.BuildFrame();
            bool cursorOn = !frame.Cursor.Blinking || _cursorOn;

            FrameGridRenderer.Render(
                frame, ImGui.GetWindowDrawList(), canvasPos,
                _cellWidth, _cellHeight, font, fontSize, _selectionColor, cursorOn);

            if (_hasFocus)
            {
                HandleInput(session, canvasPos, cols, rows);
            }
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

    private void HandleInput(TerminalSession session, float2 canvasPos, int cols, int rows)
    {
        var io = ImGui.GetIO();
        HandleKeyboard(session, io);
        HandleMouse(session, io, canvasPos, cols, rows);
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

    private void HandleMouse(TerminalSession session, ImGuiIOPtr io, float2 canvasPos, int cols, int rows)
    {
        bool hovered = ImGui.IsWindowHovered();

        // Wheel scrolling (viewport scrollback when the app isn't tracking the mouse).
        if (hovered && io.MouseWheel != 0 && !session.Surface.IsMouseTrackingEnabled)
        {
            session.Surface.ScrollBy(-(int)Math.Round(io.MouseWheel * 3));
        }

        var cell = MouseCell(canvasPos, cols, rows);

        if (session.Surface.IsMouseTrackingEnabled)
        {
            HandleAppMouse(session, io, cell);
            return;
        }

        // Selection gestures.
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                session.Surface.SelectWord(cell);
                _selecting = false;
            }
            else
            {
                _selectAnchor = cell;
                _selecting = true;
                session.Surface.ClearSelection();
            }
        }
        else if (_selecting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            session.Surface.SelectCells(_selectAnchor, cell);
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
            var button = io.MouseWheel > 0 ? MouseButton.Middle : MouseButton.Middle;
            EncodeMouseAndSend(session, MouseAction.Press, button, mods, pos);
        }
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

    private ImFontPtr ResolveFont()
    {
        if (!string.IsNullOrEmpty(_fontConfig.RegularFontName)
            && PurrTTYFontManager.LoadedFonts.TryGetValue(_fontConfig.RegularFontName, out var font))
        {
            return font;
        }

        return ImGui.GetFont();
    }

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
        _sessionManager.Dispose();
    }
}
