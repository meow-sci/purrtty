using Brutal.ImGuiApi;
using Brutal.Numerics;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Sessions;
using TKeyMods = PurrTTY.Terminal.Input.KeyModifiers;

namespace purrTTY.Display.Ghostty;

// Input handling: keyboard encoding (delegated to the shared TerminalInputEncoder),
// selection + app-mouse reporting, clipboard, and the grid context menu.
public sealed partial class TerminalWindow
{
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

    // Cached so the keyboard path allocates no per-frame closure: the encoder
    // raises this whenever input was sent, and the controller resets the blink phase.
    private Action? _raiseInputSent;

    private void HandleInput(TerminalSession session, float2 canvasPos, int cols, int rows, bool gridHovered)
    {
        var io = ImGui.GetIO();
        HandleKeyboard(session, io);
        HandleMouse(session, io, canvasPos, cols, rows, gridHovered);
    }

    // Keyboard encoding is shared with the in-world terminal via TerminalInputEncoder.
    private void HandleKeyboard(TerminalSession session, ImGuiIOPtr io)
    {
        _raiseInputSent ??= () => InputSent?.Invoke();
        TerminalInputEncoder.ProcessKeyboard(session, io, KeyboardSuppression, _raiseInputSent);
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
