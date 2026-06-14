using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Logging;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Sessions;
using TKeyMods = PurrTTY.Terminal.Input.KeyModifiers;

namespace purrTTY.Display.Ghostty;

// Input handling: keyboard encoding (named/Ctrl/Alt keys, AltGr text, surrogate
// pairing), selection + app-mouse reporting, clipboard, and the grid context menu.
public sealed partial class TerminalWindow
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

    // Opt-in keyboard input diagnostics. Set PURRTTY_KEY_DIAG=1 (or =true) in
    // the environment before launching KSA, reproduce the issue (e.g. mash
    // Ctrl+R in atuin), then read the "[keydiag]" lines in the log. Two streams:
    //   press … — every key the frame reports pressed, with the modifier LEVELS
    //             ImGui reports and edge-vs-repeat. A "press key=R … ctrl=False
    //             chars=0" line is the level/edge desync (Cause 1): the chord
    //             gate then drops it silently.
    //   send  … — the exact bytes EncodeKey produced. Legacy Ctrl+R = "12";
    //             a kitty "CSI…u" sequence (starts "1B5B") means the app
    //             negotiated the protocol (Cause 2). mods=None on a byte that
    //             should be a control char is the desync biting the encoder.
    // Read once: the value cannot change mid-process and per-frame env reads are
    // wasteful.
    private static readonly bool KeyDiag =
        Environment.GetEnvironmentVariable("PURRTTY_KEY_DIAG") is "1" or "true";

    private bool _selecting;

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

        if (KeyDiag)
        {
            LogChordDiagnostics(io);
        }

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

    private GridPoint MouseCell(float2 canvasPos, int cols, int rows)
    {
        var mouse = ImGui.GetMousePos();
        int col = Math.Clamp((int)((mouse.X - canvasPos.X) / _cellWidth), 0, Math.Max(0, cols - 1));
        int row = Math.Clamp((int)((mouse.Y - canvasPos.Y) / _cellHeight), 0, Math.Max(0, rows - 1));
        return new GridPoint(col, row);
    }

    // Logs every letter/digit/Ctrl-punctuation key the frame reports as pressed
    // (including ImGui auto-repeats), tagged with the modifier LEVELS ImGui
    // reports right now and whether this is a true down-edge or a repeat. Skips
    // ordinary printable typing (no Ctrl/Alt and a character was produced this
    // frame) so the log stays focused on chords and on the dropped-chord
    // signature: a key edge that yielded no character with no modifier held.
    private static void LogChordDiagnostics(ImGuiIOPtr io)
    {
        LogKeyGroup(io, LetterKeys);
        LogKeyGroup(io, DigitKeys);
        LogKeyGroup(io, ControlPunctuationKeys);
    }

    private static void LogKeyGroup(ImGuiIOPtr io, (ImGuiKey ImguiKey, TerminalKey Key)[] group)
    {
        foreach (var (imguiKey, key) in group)
        {
            if (!ImGui.IsKeyPressed(imguiKey, repeat: true))
            {
                continue;
            }

            // Plain text input is handled by the character queue, not the chord
            // path — don't log it (it would bury the chord lines in noise).
            bool printable = !io.KeyCtrl && !io.KeyAlt && io.InputQueueCharacters.Count > 0;
            if (printable)
            {
                continue;
            }

            bool edge = ImGui.IsKeyPressed(imguiKey, repeat: false);
            ModLog.Log.Info(
                $"[keydiag] press key={key} {(edge ? "edge" : "repeat")} " +
                $"ctrl={io.KeyCtrl} alt={io.KeyAlt} shift={io.KeyShift} " +
                $"chars={io.InputQueueCharacters.Count}");
        }
    }

    private void EncodeAndSend(TerminalSession session, in TerminalKeyEvent keyEvent)
    {
        Span<byte> buf = stackalloc byte[64];
        int n = session.Surface.EncodeKey(keyEvent, buf);
        if (KeyDiag)
        {
            string text = string.IsNullOrEmpty(keyEvent.Text) ? "-" : keyEvent.Text;
            string bytes = n > 0 ? Convert.ToHexString(buf[..n]) : "";
            ModLog.Log.Info(
                $"[keydiag] send key={keyEvent.Key} action={keyEvent.Action} mods={keyEvent.Modifiers} " +
                $"text={text} bytes=[{bytes}] ({n})");
        }

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
}
