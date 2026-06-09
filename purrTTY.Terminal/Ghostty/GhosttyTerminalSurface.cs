using Ghostty.Vt.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;
using GhosttyTypes = Ghostty.Vt.Types;
using VtKeyEncoder = Ghostty.Vt.KeyEncoder;
using VtKeyEvent = Ghostty.Vt.KeyEvent;
using VtMouseEncoder = Ghostty.Vt.MouseEncoder;
using VtMouseEvent = Ghostty.Vt.MouseEvent;
using VtRenderState = Ghostty.Vt.RenderState;
using VtTerminal = Ghostty.Vt.Terminal;

namespace PurrTTY.Terminal.Ghostty;

/// <summary>
/// The libghostty-vt-backed <see cref="ITerminalSurface"/>. Wraps a single
/// engine <c>Terminal</c> + <c>RenderState</c> and turns its state into a
/// renderer-neutral <see cref="TerminalFrame"/>. Owns key/mouse encoders, the
/// OSC sidecar, theme push, and selection.
///
/// Single-threaded native model: <see cref="Write"/> only enqueues bytes under a
/// lock; the engine is mutated exclusively on the tick thread inside
/// <see cref="BuildFrame"/>. All other members must be called on the tick thread.
/// </summary>
public sealed class GhosttyTerminalSurface : ITerminalSurface
{
    private readonly object _sync = new();
    private readonly ILogger _logger;

    private readonly VtTerminal _terminal;
    private readonly VtRenderState _renderState;
    private readonly VtKeyEncoder _keyEncoder;
    private readonly VtMouseEncoder _mouseEncoder;
    private readonly VtMouseEvent _mouseEvent;
    private readonly OscSidecar _osc = new();

    // PTY input is buffered here and applied on the tick. Two buffers are
    // swapped to keep the lock hold time to a pointer swap.
    private byte[] _inbox = new byte[4096];
    private byte[] _scratch = new byte[4096];
    private int _inboxLen;

    // Engine replies (DA/DSR/...) collected during VTWrite, flushed after.
    private readonly List<byte> _replies = new(256);

    private bool _bellPending;
    private bool _titlePending;
    private bool _pendingChange = true; // first frame counts as a change

    private int _cellPxW;
    private int _cellPxH;
    private int _surfacePxW;
    private int _surfacePxH;
    private long _generation;

    // Drag-selection anchor, pinned to content so it survives viewport scrolls.
    private global::Ghostty.Vt.GridRef _selectionAnchor;
    private bool _selectionRectangle;
    private bool _hasSelectionAnchor;

    private readonly TerminalFrame _frame;
    private bool _disposed;

    public GhosttyTerminalSurface(int cols, int rows, ILogger? logger = null, nuint? maxScrollback = null)
    {
        if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));

        _logger = logger ?? NullLogger.Instance;

        _terminal = new VtTerminal(cols, rows, opts =>
        {
            if (maxScrollback is { } mb)
            {
                opts.MaxScrollback = mb;
            }

            opts.OnWritePty = span =>
            {
                for (int i = 0; i < span.Length; i++)
                {
                    _replies.Add(span[i]);
                }
            };
            opts.OnBell = () => _bellPending = true;
            opts.OnTitleChanged = () => _titlePending = true;
        });

        _renderState = new VtRenderState();
        _keyEncoder = new VtKeyEncoder();
        _mouseEncoder = new VtMouseEncoder();
        _mouseEvent = new VtMouseEvent();

        _osc.IconNameChanged += name => IconNameChanged?.Invoke(name);
        _osc.ClipboardRequested += req => ClipboardRequested?.Invoke(req);

        _frame = new TerminalFrame(cols, rows);
    }

    public int Cols => _terminal.Cols;
    public int Rows => _terminal.Rows;
    public TerminalFrame CurrentFrame => _frame;
    public bool IsBracketedPasteEnabled => _terminal.ModeGet(TerminalMode.BracketedPaste);
    public bool IsMouseTrackingEnabled => _terminal.MouseTracking;

    public event Action<byte[]>? PtyReply;
    public event Action? Bell;
    public event Action<string>? TitleChanged;
    public event Action<string>? IconNameChanged;
    public event Action<ClipboardRequest>? ClipboardRequested;
    public event Action? FrameChanged;

    // ---- IN: data + viewport ----

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || _disposed)
        {
            return;
        }

        lock (_sync)
        {
            EnsureInboxCapacity(_inboxLen + data.Length);
            data.CopyTo(_inbox.AsSpan(_inboxLen));
            _inboxLen += data.Length;
        }
    }

    public void Resize(int cols, int rows, int cellPixelWidth = 0, int cellPixelHeight = 0)
    {
        ThrowIfDisposed();
        if (cols <= 0 || rows <= 0)
        {
            return;
        }

        if (cellPixelWidth > 0) _cellPxW = cellPixelWidth;
        if (cellPixelHeight > 0) _cellPxH = cellPixelHeight;

        _terminal.Resize(cols, rows, _cellPxW, _cellPxH);
        _frame.Resize(cols, rows);
        _pendingChange = true;
    }

    public void ScrollBy(int deltaRows)
    {
        ThrowIfDisposed();
        _terminal.ScrollViewportBy(deltaRows);
        _pendingChange = true;
    }

    public void ScrollToTop()
    {
        ThrowIfDisposed();
        _terminal.ScrollViewportToTop();
        _pendingChange = true;
    }

    public void ScrollToBottom()
    {
        ThrowIfDisposed();
        _terminal.ScrollViewportToBottom();
        _pendingChange = true;
    }

    // ---- IN: selection ----

    public void SelectCells(GridPoint anchor, GridPoint head, bool rectangle = false)
    {
        ThrowIfDisposed();
        var a = _terminal.GetGridRef(GhosttyTypes.Point.Viewport(anchor.Col, anchor.Row));
        var h = _terminal.GetGridRef(GhosttyTypes.Point.Viewport(head.Col, head.Row));
        _terminal.SetSelection(a, h, rectangle);
        _pendingChange = true;
    }

    public void BeginSelectCells(GridPoint anchor, bool rectangle = false)
    {
        ThrowIfDisposed();
        // Pin the anchor to content (GridRef), not to a viewport row, so it stays
        // put when the viewport scrolls mid-drag.
        _selectionAnchor = _terminal.GetGridRef(GhosttyTypes.Point.Viewport(anchor.Col, anchor.Row));
        _selectionRectangle = rectangle;
        _hasSelectionAnchor = true;
    }

    public void ExtendSelectCells(GridPoint head)
    {
        ThrowIfDisposed();
        if (!_hasSelectionAnchor)
        {
            return;
        }

        var h = _terminal.GetGridRef(GhosttyTypes.Point.Viewport(head.Col, head.Row));
        _terminal.SetSelection(_selectionAnchor, h, _selectionRectangle);
        _pendingChange = true;
    }

    public void SelectWord(GridPoint point)
    {
        ThrowIfDisposed();
        _terminal.SelectWord(_terminal.GetGridRef(GhosttyTypes.Point.Viewport(point.Col, point.Row)));
        _pendingChange = true;
    }

    public void SelectLine(GridPoint point)
    {
        ThrowIfDisposed();
        _terminal.SelectLine(_terminal.GetGridRef(GhosttyTypes.Point.Viewport(point.Col, point.Row)));
        _pendingChange = true;
    }

    public void SelectAll()
    {
        ThrowIfDisposed();
        _terminal.SelectAll();
        _pendingChange = true;
    }

    public void ClearSelection()
    {
        ThrowIfDisposed();
        _terminal.ClearSelection();
        _hasSelectionAnchor = false;
        _pendingChange = true;
    }

    public string? GetSelectionText()
    {
        ThrowIfDisposed();
        return _terminal.GetSelectionText();
    }

    // ---- IN: theme + cursor ----

    public void SetTheme(TerminalTheme theme)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(theme);

        var palette = new GhosttyTypes.ColorRgb[256];
        for (int i = 0; i < 256; i++)
        {
            var c = theme.Palette[i];
            palette[i] = new GhosttyTypes.ColorRgb { R = c.R, G = c.G, B = c.B };
        }

        _terminal.SetColorPalette(palette);
        _terminal.SetForegroundColor(ToColorRgb(theme.DefaultForeground));
        _terminal.SetBackgroundColor(ToColorRgb(theme.DefaultBackground));
        _terminal.SetCursorColor(ToColorRgb(theme.Cursor));
        _pendingChange = true;
    }

    public void SetCursorStyle(CursorShape shape, bool blink)
    {
        ThrowIfDisposed();
        _terminal.SetDefaultCursorStyle(ToVisualStyle(shape));
        _terminal.SetDefaultCursorBlink(blink);
        _pendingChange = true;
    }

    // ---- IN: input encoding ----

    public int EncodeKey(in TerminalKeyEvent keyEvent, Span<byte> output)
    {
        ThrowIfDisposed();

        try
        {
            return EncodeKeyOnce(keyEvent, output);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Key encode failed for {Key}", keyEvent.Key);
            return 0;
        }
    }

    // Use a fresh native event per call: the engine distinguishes "no text"
    // (named keys → encode from Key) from "empty text", and a reused event
    // cannot reliably clear previously-set UTF-8. Key presses are infrequent.
    private int EncodeKeyOnce(in TerminalKeyEvent keyEvent, Span<byte> output)
    {
        _keyEncoder.ConfigureFromTerminal(_terminal);
        using var ev = new VtKeyEvent
        {
            Key = (int)keyEvent.Key,
            Action = (int)keyEvent.Action,
            Modifiers = (ushort)keyEvent.Modifiers,
        };
        if (!string.IsNullOrEmpty(keyEvent.Text))
        {
            ev.Text = keyEvent.Text;
        }

        var bytes = _keyEncoder.Encode(ev);
        int n = Math.Min(bytes.Length, output.Length);
        bytes[..n].CopyTo(output);
        return n;
    }

    public int EncodeMouse(in TerminalMouseEvent mouseEvent, Span<byte> output)
    {
        ThrowIfDisposed();
        if (!_terminal.MouseTracking)
        {
            return 0;
        }

        _mouseEncoder.ConfigureFromTerminal(_terminal);
        if (_surfacePxW > 0 && _surfacePxH > 0 && _cellPxW > 0 && _cellPxH > 0)
        {
            _mouseEncoder.SetSize(_surfacePxW, _surfacePxH, _cellPxW, _cellPxH);
        }

        _mouseEvent.Action = (int)mouseEvent.Action;
        _mouseEvent.Button = mouseEvent.Button == MouseButton.None ? -1 : (int)mouseEvent.Button;
        _mouseEvent.Modifiers = (int)mouseEvent.Modifiers;
        _mouseEvent.X = mouseEvent.X;
        _mouseEvent.Y = mouseEvent.Y;

        try
        {
            var bytes = _mouseEncoder.Encode(_mouseEvent);
            int n = Math.Min(bytes.Length, output.Length);
            bytes[..n].CopyTo(output);
            return n;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Mouse encode failed");
            return 0;
        }
    }

    public void SetMouseGeometry(int surfacePixelWidth, int surfacePixelHeight, int cellPixelWidth, int cellPixelHeight)
    {
        _surfacePxW = surfacePixelWidth;
        _surfacePxH = surfacePixelHeight;
        if (cellPixelWidth > 0) _cellPxW = cellPixelWidth;
        if (cellPixelHeight > 0) _cellPxH = cellPixelHeight;
    }

    public byte[] EncodePaste(ReadOnlySpan<byte> text)
    {
        ThrowIfDisposed();
        return global::Ghostty.Vt.Paste.Encode(text, IsBracketedPasteEnabled);
    }

    // ---- OUT: frame ----

    public TerminalFrame BuildFrame()
    {
        ThrowIfDisposed();

        int inputLen;
        lock (_sync)
        {
            inputLen = _inboxLen;
            if (inputLen > 0)
            {
                (_inbox, _scratch) = (_scratch, _inbox);
                _inboxLen = 0;
            }
        }

        if (inputLen > 0)
        {
            var span = _scratch.AsSpan(0, inputLen);
            _osc.Feed(span);
            _terminal.VTWrite(span);
            _pendingChange = true;
        }

        if (_replies.Count > 0)
        {
            var reply = _replies.ToArray();
            _replies.Clear();
            PtyReply?.Invoke(reply);
        }

        _renderState.Update(_terminal);

        bool changed = _pendingChange || _renderState.Dirty != RenderStateDirty.False;
        PopulateFrame();
        _pendingChange = false;

        if (_bellPending)
        {
            _bellPending = false;
            Bell?.Invoke();
        }

        if (_titlePending)
        {
            _titlePending = false;
            TitleChanged?.Invoke(_terminal.Title ?? string.Empty);
        }

        if (changed)
        {
            _frame.Generation = ++_generation;
            FrameChanged?.Invoke();
        }

        return _frame;
    }

    private void PopulateFrame()
    {
        int cols = _terminal.Cols;
        int rows = _terminal.Rows;
        if (_frame.Cols != cols || _frame.Rows != rows)
        {
            _frame.Resize(cols, rows);
        }

        var colors = _renderState.Colors;
        for (int i = 0; i < 256 && i < colors.Palette.Length; i++)
        {
            _frame.Colors.Palette[i] = ToRgba(colors.Palette[i]);
        }

        var defFg = ToRgba(colors.Foreground);
        var defBg = ToRgba(colors.Background);
        _frame.Colors.DefaultForeground = defFg;
        _frame.Colors.DefaultBackground = defBg;
        _frame.Colors.Cursor = colors.CursorHasValue ? ToRgba(colors.Cursor) : defFg;

        int rowIdx = 0;
        foreach (var srcRow in _renderState.Rows)
        {
            if (rowIdx >= rows)
            {
                break;
            }

            var dstRow = _frame.RowData[rowIdx];
            dstRow.WrapContinuation = srcRow.WrapContinuation;
            if (srcRow.Selection is { } sel)
            {
                dstRow.SelectionStart = sel.StartX;
                dstRow.SelectionEnd = sel.EndX;
            }
            else
            {
                dstRow.SelectionStart = -1;
                dstRow.SelectionEnd = -1;
            }

            int colIdx = 0;
            foreach (var cell in srcRow.Cells)
            {
                if (colIdx >= cols)
                {
                    break;
                }

                FillCell(ref dstRow.Cells[colIdx], cell, defFg, defBg);
                colIdx++;
            }

            for (int c = colIdx; c < cols; c++)
            {
                ClearCell(ref dstRow.Cells[c], defFg, defBg);
            }

            rowIdx++;
        }

        for (; rowIdx < rows; rowIdx++)
        {
            var dstRow = _frame.RowData[rowIdx];
            dstRow.SelectionStart = -1;
            dstRow.SelectionEnd = -1;
            dstRow.WrapContinuation = false;
            for (int c = 0; c < cols; c++)
            {
                ClearCell(ref dstRow.Cells[c], defFg, defBg);
            }
        }

        _frame.Cursor = new CursorState
        {
            X = _renderState.CursorViewportX,
            Y = _renderState.CursorViewportY,
            Visible = _renderState.CursorVisible && _renderState.CursorViewportHasValue,
            Shape = ToCursorShape(_renderState.CursorStyle),
            Blinking = _renderState.CursorBlinking,
        };

        var scrollbar = _terminal.Scrollbar;
        _frame.Scrollbar = new ScrollbarState
        {
            Offset = scrollbar.Offset,
            ViewportHeight = scrollbar.ViewportHeight,
            ScrollbackHeight = scrollbar.ScrollbackHeight,
        };
    }

    private static void FillCell(ref FrameCell dst, GhosttyTypes.Cell cell, RgbaColor defFg, RgbaColor defBg)
    {
        dst.Grapheme = cell.HasText ? cell.Grapheme : null;
        dst.Fg = cell.FgColor is { } fg ? ToRgba(fg) : defFg;
        dst.Bg = cell.BgColor is { } bg ? ToRgba(bg) : defBg;

        var style = cell.Style;

        // Reverse video: the engine reports logical fg/bg plus an inverse flag, so
        // resolve it to final draw colors here (the frontend draws Fg/Bg directly).
        // The Inverse flag is still surfaced below as metadata.
        if (style.Inverse)
        {
            (dst.Fg, dst.Bg) = (dst.Bg, dst.Fg);
        }

        var flags = CellFlags.None;
        if (style.Bold) flags |= CellFlags.Bold;
        if (style.Italic) flags |= CellFlags.Italic;
        if (style.Faint) flags |= CellFlags.Faint;
        if (style.Blink) flags |= CellFlags.Blink;
        if (style.Inverse) flags |= CellFlags.Inverse;
        if (style.Strikethrough) flags |= CellFlags.Strikethrough;
        if (style.Invisible) flags |= CellFlags.Invisible;
        if (style.Overline) flags |= CellFlags.Overline;
        dst.Flags = flags;

        dst.Underline = (UnderlineStyle)Math.Clamp(style.Underline, 0, 5);
        dst.Width = ToCellWidth(cell.Wide);

        // Only RGB underline colors are surfaced; palette/none fall back to fg at draw time.
        dst.UnderlineColor = style.UnderlineColor.Tag == StyleColorTag.Rgb
            ? ToRgba(style.UnderlineColor.Rgb)
            : null;
    }

    private static void ClearCell(ref FrameCell dst, RgbaColor defFg, RgbaColor defBg)
    {
        dst.Grapheme = null;
        dst.Fg = defFg;
        dst.Bg = defBg;
        dst.Flags = CellFlags.None;
        dst.Underline = UnderlineStyle.None;
        dst.Width = CellWidth.Narrow;
        dst.UnderlineColor = null;
    }

    private static RgbaColor ToRgba(GhosttyTypes.ColorRgb c) => new(c.R, c.G, c.B);

    private static GhosttyTypes.ColorRgb ToColorRgb(RgbaColor c) => new() { R = c.R, G = c.G, B = c.B };

    private static CellWidth ToCellWidth(CellWide wide) => wide switch
    {
        CellWide.Wide => CellWidth.Wide,
        CellWide.Narrow => CellWidth.Narrow,
        _ => CellWidth.Spacer, // SpacerTail / SpacerHead
    };

    private static CursorShape ToCursorShape(CursorVisualStyle style) => style switch
    {
        CursorVisualStyle.Bar => CursorShape.Bar,
        CursorVisualStyle.Block => CursorShape.Block,
        CursorVisualStyle.Underline => CursorShape.Underline,
        CursorVisualStyle.BlockHollow => CursorShape.BlockHollow,
        _ => CursorShape.Block,
    };

    private static CursorVisualStyle ToVisualStyle(CursorShape shape) => shape switch
    {
        CursorShape.Bar => CursorVisualStyle.Bar,
        CursorShape.Block => CursorVisualStyle.Block,
        CursorShape.Underline => CursorVisualStyle.Underline,
        CursorShape.BlockHollow => CursorVisualStyle.BlockHollow,
        _ => CursorVisualStyle.Block,
    };

    private void EnsureInboxCapacity(int required)
    {
        if (_inbox.Length >= required)
        {
            return;
        }

        int newSize = _inbox.Length * 2;
        while (newSize < required)
        {
            newSize *= 2;
        }

        Array.Resize(ref _inbox, newSize);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mouseEvent.Dispose();
        _keyEncoder.Dispose();
        _mouseEncoder.Dispose();
        _renderState.Dispose();
        _terminal.Dispose();
    }
}
