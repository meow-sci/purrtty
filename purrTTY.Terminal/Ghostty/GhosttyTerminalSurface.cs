using Ghostty.Vt.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;
using GhosttyTypes = Ghostty.Vt.Types;
using VtFrameReader = Ghostty.Vt.RenderFrameReader;
using VtKeyEncoder = Ghostty.Vt.KeyEncoder;
using VtKeyEvent = Ghostty.Vt.KeyEvent;
using VtMouseEncoder = Ghostty.Vt.MouseEncoder;
using VtMouseEvent = Ghostty.Vt.MouseEvent;
using VtRawCell = Ghostty.Vt.RawCell;
using VtRawCellLayout = Ghostty.Vt.RawCellLayout;
using VtRenderState = Ghostty.Vt.RenderState;
using VtTerminal = Ghostty.Vt.Terminal;

namespace PurrTTY.Terminal.Ghostty;

/// <summary>
/// Per-<see cref="GhosttyTerminalSurface.BuildFrame"/> diagnostics for the
/// debug perf HUD. Values are best-effort and engine-specific.
/// </summary>
public struct SurfaceFrameStats
{
    /// <summary>Milliseconds spent feeding PTY bytes (OSC tee + engine VT write).</summary>
    public double WriteMs;

    /// <summary>Milliseconds spent in the engine render-state update.</summary>
    public double UpdateMs;

    /// <summary>Milliseconds spent rebuilding the frame cells + cursor/scrollbar.</summary>
    public double PopulateMs;

    /// <summary>PTY bytes consumed this tick.</summary>
    public int BytesConsumed;

    /// <summary>0 = clean/skipped, 1 = partial row refresh, 2 = full rebuild.</summary>
    public int DirtyState;

    /// <summary>True when the frame was withheld by DEC 2026 synchronized output.</summary>
    public bool SyncPaused;

    /// <summary>Whether the frame generation advanced this tick.</summary>
    public bool ContentChanged;
}

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

    // Safety net: the inbox never grows past this. The PTY pumps never sleep
    // (by design) and the inbox only drains on the tick, so a session that is
    // not being ticked (hidden terminal, inactive tab) would otherwise grow
    // without bound under chatty output. The frontend now ticks every session,
    // so hitting this cap means something upstream is broken — drop is the
    // last resort, not the regulator.
    private const int MaxInboxBytes = 8 * 1024 * 1024;

    // Cap on PTY bytes fed to the engine per BuildFrame. Bounds the per-tick
    // stall when catching up on a large backlog (e.g. the first tick after the
    // terminal is re-shown); the remainder stays queued for following ticks.
    private const int MaxBytesPerTick = 1024 * 1024;

    // Set when Write had to discard bytes at the inbox tail. The next bytes
    // that are accepted are preceded by CAN + ST, which aborts any escape
    // sequence and terminates any OSC/DCS string left open at the drop
    // boundary (both are no-ops in the parser's ground state), so the engine's
    // parser cannot desync across the gap.
    private bool _inboxDroppedTail;
    private static readonly byte[] DropResetSeq = { 0x18, 0x1B, (byte)'\\' };

    // Engine replies (DA/DSR/...) collected during VTWrite, flushed after.
    private readonly List<byte> _replies = new(256);

    private bool _bellPending;
    private bool _titlePending;
    private bool _pendingChange = true; // first frame counts as a change

    // Synchronized output (DEC mode 2026): timestamp of when the pause began,
    // 0 when not paused. Frames are withheld while an app batches a redraw,
    // bounded by SyncOutputTimeout in case it never clears the mode.
    private long _syncOutputSince;
    private static readonly TimeSpan SyncOutputTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Diagnostics from the most recent <see cref="BuildFrame"/> (for the perf HUD).</summary>
    public SurfaceFrameStats LastFrameStats;

    // Interned grapheme strings so steady-state frame rebuilds allocate nothing.
    private readonly GraphemeCache _graphemes = new();

    // One-time cross-check of the managed packed-cell decode against the
    // native accessors (catches a pin bump that changes the bit layout).
    private static int s_cellLayoutChecked;

    private int _cellPxW;
    private int _cellPxH;
    private int _surfacePxW;
    private int _surfacePxH;
    private long _generation;

    // Drag-selection anchor: a *tracked* grid ref (engine-owned bookkeeping)
    // so it both survives viewport scrolls and follows page churn. The tracked
    // object is created once and reused via Set() for subsequent drags.
    private global::Ghostty.Vt.TrackedGridRef? _selectionAnchor;
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

        if (Interlocked.Exchange(ref s_cellLayoutChecked, 1) == 0)
        {
            try
            {
                if (!VtRawCellLayout.Validate(out var layoutError))
                {
                    _logger.LogError(
                        "libghostty-vt packed-cell layout mismatch — cell rendering will be wrong until the managed decode is updated: {Error}",
                        layoutError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "libghostty-vt cell layout self-check failed to run");
            }
        }
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

        bool dropped = false;
        lock (_sync)
        {
            int prefix = _inboxDroppedTail ? DropResetSeq.Length : 0;
            long required = (long)_inboxLen + prefix + data.Length;
            if (required > MaxInboxBytes)
            {
                dropped = !_inboxDroppedTail;
                _inboxDroppedTail = true;
            }
            else
            {
                EnsureInboxCapacity((int)required);
                if (_inboxDroppedTail)
                {
                    DropResetSeq.CopyTo(_inbox.AsSpan(_inboxLen));
                    _inboxLen += DropResetSeq.Length;
                    _inboxDroppedTail = false;
                }

                data.CopyTo(_inbox.AsSpan(_inboxLen));
                _inboxLen += data.Length;
            }
        }

        if (dropped)
        {
            // Logged on the transition into the dropped state only, so a
            // firehose of writes cannot flood the log.
            _logger.LogWarning(
                "Terminal inbox exceeded {Cap} bytes while not being drained; discarding PTY output until the backlog is consumed",
                MaxInboxBytes);
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
        // Pin the anchor to content with a tracked grid ref, not to a viewport
        // row, so it stays put when the viewport scrolls mid-drag. An untracked
        // GridRef is only valid until the next mutating engine call — PTY bytes
        // are fed between begin and extend, and scrollback pruning or reflow can
        // free the page an untracked anchor points into (dangling native node).
        var point = GhosttyTypes.Point.Viewport(anchor.Col, anchor.Row);
        if (_selectionAnchor is { } tracked)
        {
            _hasSelectionAnchor = tracked.Set(point);
        }
        else
        {
            _selectionAnchor = _terminal.TrackGridRef(point);
            _hasSelectionAnchor = _selectionAnchor is not null;
        }

        _selectionRectangle = rectangle;
    }

    public void ExtendSelectCells(GridPoint head)
    {
        ThrowIfDisposed();
        if (!_hasSelectionAnchor || _selectionAnchor is not { } anchor)
        {
            return;
        }

        // The anchored content can be discarded mid-drag (e.g. scrollback
        // pruning under heavy output). End the drag gracefully instead of
        // handing the engine a stale reference.
        if (!anchor.TrySnapshot(out var anchorRef))
        {
            _hasSelectionAnchor = false;
            return;
        }

        var h = _terminal.GetGridRef(GhosttyTypes.Point.Viewport(head.Col, head.Row));
        _terminal.SetSelection(anchorRef, h, _selectionRectangle);
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

    public bool HasSelection
    {
        get
        {
            ThrowIfDisposed();
            return _terminal.HasSelection;
        }
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
        _mouseEvent.Button = ToNativeButton(mouseEvent.Button);
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
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();

        int inputLen;
        lock (_sync)
        {
            if (_inboxLen <= MaxBytesPerTick)
            {
                inputLen = _inboxLen;
                if (inputLen > 0)
                {
                    (_inbox, _scratch) = (_scratch, _inbox);
                    _inboxLen = 0;
                }
            }
            else
            {
                // Backlog catch-up: feed at most MaxBytesPerTick this tick so a
                // large buffered burst cannot stall the render thread for one
                // giant VTWrite; the rest is consumed on subsequent ticks.
                inputLen = MaxBytesPerTick;
                if (_scratch.Length < inputLen)
                {
                    _scratch = new byte[inputLen];
                }

                Array.Copy(_inbox, 0, _scratch, 0, inputLen);
                Array.Copy(_inbox, inputLen, _inbox, 0, _inboxLen - inputLen);
                _inboxLen -= inputLen;
            }
        }

        if (inputLen > 0)
        {
            var span = _scratch.AsSpan(0, inputLen);
            _osc.Feed(span);
            _terminal.VTWrite(span);
            // No _pendingChange here: the engine's dirty tracking decides
            // whether the bytes changed anything visible.
        }

        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_replies.Count > 0)
        {
            var reply = _replies.ToArray();
            _replies.Clear();
            PtyReply?.Invoke(reply);
        }

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

        // Synchronized output (DEC 2026): while an app is batching a redraw,
        // keep showing the last complete frame instead of a partial one (this
        // is what eliminates mid-frame tearing for apps that use it). Bounded
        // by a timeout so a stuck mode cannot freeze the terminal.
        if (_terminal.ModeGet(TerminalMode.SynchronizedOutput))
        {
            if (_syncOutputSince == 0)
            {
                _syncOutputSince = System.Diagnostics.Stopwatch.GetTimestamp();
            }

            if (System.Diagnostics.Stopwatch.GetElapsedTime(_syncOutputSince) < SyncOutputTimeout)
            {
                LastFrameStats = new SurfaceFrameStats
                {
                    WriteMs = ElapsedMs(t0, t1),
                    BytesConsumed = inputLen,
                    SyncPaused = true,
                };
                return _frame;
            }
            // Overdue: render live until the app finally clears the mode.
        }
        else
        {
            _syncOutputSince = 0;
        }

        _renderState.Update(_terminal);
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();

        bool changed = PopulateFrame(_pendingChange);
        _pendingChange = false;
        changed |= RefreshCursorAndScrollbar();
        long t3 = System.Diagnostics.Stopwatch.GetTimestamp();

        if (changed)
        {
            _frame.Generation = ++_generation;
            FrameChanged?.Invoke();
        }

        LastFrameStats = new SurfaceFrameStats
        {
            WriteMs = ElapsedMs(t0, t1),
            UpdateMs = ElapsedMs(t1, t2),
            PopulateMs = ElapsedMs(t2, t3),
            BytesConsumed = inputLen,
            DirtyState = _lastDirtyState,
            ContentChanged = changed,
        };

        return _frame;
    }

    private static double ElapsedMs(long from, long to)
        => System.Diagnostics.Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;

    private int _lastDirtyState; // 0 = skipped/clean, 1 = partial, 2 = full

    // Grapheme clusters longer than this (in UTF-8 bytes) take a rare
    // heap-allocating slow path; everything realistic fits.
    private const int GraphemeScratchBytes = 256;

    /// <summary>
    /// Rebuilds the frame's cell content from the render state, honoring the
    /// engine's dirty tracking: a clean state is skipped entirely, a partial
    /// state only refreshes dirty rows (clean rows keep their cells from the
    /// previous tick — the engine guarantees row indices are stable unless the
    /// update was Full), and a full state rebuilds everything including colors.
    /// Consumed dirty flags are cleared (the engine never clears them itself).
    /// Returns whether any cell content changed.
    /// </summary>
    private bool PopulateFrame(bool force)
    {
        int cols = _terminal.Cols;
        int rows = _terminal.Rows;
        if (_frame.Cols != cols || _frame.Rows != rows)
        {
            _frame.Resize(cols, rows);
            force = true;
        }

        var dirty = _renderState.Dirty;
        if (!force && dirty == RenderStateDirty.False)
        {
            _lastDirtyState = 0;
            return false;
        }

        bool full = force || dirty == RenderStateDirty.Full;
        _lastDirtyState = full ? 2 : 1;
        if (full)
        {
            // Palette / default-color changes always mark the terminal dirty,
            // which yields a Full state — so colors only need re-reading here.
            RefreshColors();
        }

        var defFg = _frame.Colors.DefaultForeground;
        var defBg = _frame.Colors.DefaultBackground;

        Span<byte> utf8Scratch = stackalloc byte[GraphemeScratchBytes];
        var reader = _renderState.CreateFrameReader();
        try
        {
            int rowIdx = 0;
            while (rowIdx < rows && reader.NextRow())
            {
                bool rowDirty = reader.RowDirty;
                if (rowDirty)
                {
                    reader.ClearRowDirty();
                }

                if (!full && !rowDirty)
                {
                    rowIdx++;
                    continue;
                }

                var dstRow = _frame.RowData[rowIdx];
                dstRow.WrapContinuation = reader.RowWrapContinuation;
                if (reader.RowSelection is { } sel)
                {
                    dstRow.SelectionStart = sel.StartX;
                    dstRow.SelectionEnd = sel.EndX;
                }
                else
                {
                    dstRow.SelectionStart = -1;
                    dstRow.SelectionEnd = -1;
                }

                bool hasDecorations = false;
                reader.BindRowCells();
                int colIdx = 0;
                while (colIdx < cols && reader.NextCell())
                {
                    FillCell(ref dstRow.Cells[colIdx], reader.Cell, ref reader, utf8Scratch, defFg, defBg, ref hasDecorations);
                    colIdx++;
                }

                for (int c = colIdx; c < cols; c++)
                {
                    ClearCell(ref dstRow.Cells[c], defFg, defBg);
                }

                dstRow.HasDecorations = hasDecorations;
                rowIdx++;
            }

            for (; rowIdx < rows; rowIdx++)
            {
                var dstRow = _frame.RowData[rowIdx];
                dstRow.SelectionStart = -1;
                dstRow.SelectionEnd = -1;
                dstRow.WrapContinuation = false;
                dstRow.HasDecorations = false;
                for (int c = 0; c < cols; c++)
                {
                    ClearCell(ref dstRow.Cells[c], defFg, defBg);
                }
            }
        }
        finally
        {
            reader.Dispose();
        }

        _renderState.ClearDirty();
        return true;
    }

    private void RefreshColors()
    {
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
    }

    /// <summary>
    /// Refreshes cursor and scrollbar state every tick (they are cheap and not
    /// covered by row dirty tracking) and reports whether either changed.
    /// </summary>
    private bool RefreshCursorAndScrollbar()
    {
        bool inViewport = _renderState.CursorViewportHasValue;
        var cursor = new CursorState
        {
            X = inViewport ? _renderState.CursorViewportX : 0,
            Y = inViewport ? _renderState.CursorViewportY : 0,
            Visible = _renderState.CursorVisible && inViewport,
            Shape = ToCursorShape(_renderState.CursorStyle),
            Blinking = _renderState.CursorBlinking,
        };

        bool changed =
            cursor.X != _frame.Cursor.X
            || cursor.Y != _frame.Cursor.Y
            || cursor.Visible != _frame.Cursor.Visible
            || cursor.Shape != _frame.Cursor.Shape
            || cursor.Blinking != _frame.Cursor.Blinking;
        _frame.Cursor = cursor;

        var native = _terminal.Scrollbar;
        var scrollbar = new ScrollbarState
        {
            Offset = native.Offset,
            ViewportHeight = native.ViewportHeight,
            ScrollbackHeight = native.ScrollbackHeight,
        };

        changed |= scrollbar.Offset != _frame.Scrollbar.Offset
            || scrollbar.ViewportHeight != _frame.Scrollbar.ViewportHeight
            || scrollbar.ScrollbackHeight != _frame.Scrollbar.ScrollbackHeight;
        _frame.Scrollbar = scrollbar;

        return changed;
    }

    private void FillCell(
        ref FrameCell dst,
        in VtRawCell cell,
        ref VtFrameReader reader,
        scoped Span<byte> utf8Scratch,
        RgbaColor defFg,
        RgbaColor defBg,
        ref bool hasDecorations)
    {
        string? grapheme = null;
        if (cell.IsGraphemeCluster)
        {
            int len = reader.ReadCellGraphemeUtf8(utf8Scratch);
            if (len > 0)
            {
                grapheme = _graphemes.FromUtf8(utf8Scratch[..len]);
            }
            else if (len < 0)
            {
                grapheme = ReadOversizedGrapheme(ref reader, -len);
            }
        }
        else
        {
            uint cp = cell.Codepoint;
            if (cp != 0)
            {
                grapheme = _graphemes.FromCodepoint(cp);
            }
        }

        var fg = defFg;
        var bg = cell.ContentTag switch
        {
            VtRawCell.TagBgColorPalette => _frame.Colors.Palette[cell.BgPaletteIndex],
            VtRawCell.TagBgColorRgb => ToRgba(cell.BgRgb),
            _ => defBg,
        };

        var flags = CellFlags.None;
        var underline = UnderlineStyle.None;
        RgbaColor? underlineColor = null;

        if (cell.HasStyling)
        {
            var style = reader.CellStyle;
            fg = ResolveStyleColor(style.FgColor, defFg);
            if (!cell.HasBgContent)
            {
                bg = ResolveStyleColor(style.BgColor, defBg);
            }

            if (style.Bold) flags |= CellFlags.Bold;
            if (style.Italic) flags |= CellFlags.Italic;
            if (style.Faint) flags |= CellFlags.Faint;
            if (style.Blink) flags |= CellFlags.Blink;
            if (style.Strikethrough) flags |= CellFlags.Strikethrough;
            if (style.Invisible) flags |= CellFlags.Invisible;
            if (style.Overline) flags |= CellFlags.Overline;

            underline = (UnderlineStyle)Math.Clamp(style.Underline, 0, 5);

            // Only RGB underline colors are surfaced; palette/none fall back to fg at draw time.
            underlineColor = style.UnderlineColor.Tag == StyleColorTag.Rgb
                ? ToRgba(style.UnderlineColor.Rgb)
                : null;

            // Reverse video: the engine reports logical fg/bg plus an inverse
            // flag; resolve it to final draw colors here (the frontend draws
            // Fg/Bg directly). The Inverse flag is still surfaced as metadata.
            if (style.Inverse)
            {
                flags |= CellFlags.Inverse;
                (fg, bg) = (bg, fg);
            }
        }

        dst.Grapheme = grapheme;
        dst.Fg = fg;
        dst.Bg = bg;
        dst.Flags = flags;
        dst.Underline = underline;
        dst.Width = ToCellWidth(cell.Wide);
        dst.UnderlineColor = underlineColor;

        if (underline != UnderlineStyle.None
            || (flags & (CellFlags.Strikethrough | CellFlags.Overline)) != 0)
        {
            hasDecorations = true;
        }
    }

    private string? ReadOversizedGrapheme(ref VtFrameReader reader, int requiredBytes)
    {
        var buffer = new byte[requiredBytes];
        int len = reader.ReadCellGraphemeUtf8(buffer);
        return len > 0 ? _graphemes.FromUtf8(buffer.AsSpan(0, len)) : null;
    }

    private RgbaColor ResolveStyleColor(in GhosttyTypes.StyleColor color, RgbaColor fallback) => color.Tag switch
    {
        StyleColorTag.Rgb => ToRgba(color.Rgb),
        StyleColorTag.Palette => _frame.Colors.Palette[color.PaletteIndex],
        _ => fallback,
    };

    /// <summary>
    /// Interns grapheme strings so the per-tick frame rebuild allocates nothing
    /// once warmed up. Single tick-thread use (like the rest of the surface).
    /// </summary>
    private sealed class GraphemeCache
    {
        // The tables live for the surface lifetime; a stream that churns unique
        // graphemes (binary noise, randomized emoji) would otherwise grow them
        // without bound. On overflow the tables reset and re-warm — steady-state
        // content re-interns within a frame or two.
        private const int MaxEntries = 64 * 1024;

        private readonly string?[] _ascii = new string?[128];
        private readonly Dictionary<uint, string> _codepoints = new();
        private readonly Dictionary<string, string> _clusters = new();
        private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _clusterLookup;

        public GraphemeCache()
        {
            _clusterLookup = _clusters.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        public string FromCodepoint(uint cp)
        {
            if (cp < 128)
            {
                return _ascii[cp] ??= ((char)cp).ToString();
            }

            if (!_codepoints.TryGetValue(cp, out var s))
            {
                ResetIfFull();
                s = cp <= 0xFFFF ? ((char)cp).ToString() : char.ConvertFromUtf32((int)cp);
                _codepoints[cp] = s;
            }

            return s;
        }

        public string FromUtf8(ReadOnlySpan<byte> utf8)
        {
            // UTF-16 unit count never exceeds the UTF-8 byte count.
            Span<char> chars = utf8.Length <= GraphemeScratchBytes
                ? stackalloc char[utf8.Length]
                : new char[utf8.Length];
            int n = System.Text.Encoding.UTF8.GetChars(utf8, chars);
            var span = chars[..n];

            if (_clusterLookup.TryGetValue(span, out var s))
            {
                return s;
            }

            ResetIfFull();
            s = new string(span);
            _clusters[s] = s;
            return s;
        }

        private void ResetIfFull()
        {
            if (_codepoints.Count + _clusters.Count >= MaxEntries)
            {
                _codepoints.Clear();
                _clusters.Clear();
            }
        }
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

    // Map the renderer-neutral button to libghostty's GhosttyMouseButton enum
    // (UNKNOWN=0, LEFT=1, RIGHT=2, MIDDLE=3, FOUR=4 = scroll-up, FIVE=5 = scroll-down).
    // A negative value clears the button on the native event. NOTE: the neutral
    // ordering differs (Left=0/Middle=1/Right=2), so a straight cast is wrong.
    private static int ToNativeButton(MouseButton button) => button switch
    {
        MouseButton.Left => 1,
        MouseButton.Right => 2,
        MouseButton.Middle => 3,
        MouseButton.ScrollUp => 4,
        MouseButton.ScrollDown => 5,
        _ => -1,
    };

    private void EnsureInboxCapacity(int required)
    {
        if (_inbox.Length >= required)
        {
            return;
        }

        // long arithmetic: int doubling overflows past 1 GB. Capacity is also
        // clamped to the inbox cap, which Write guarantees `required` respects.
        long newSize = _inbox.Length * 2L;
        while (newSize < required)
        {
            newSize *= 2;
        }

        Array.Resize(ref _inbox, (int)Math.Min(newSize, MaxInboxBytes));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _selectionAnchor?.Dispose();
        _mouseEvent.Dispose();
        _keyEncoder.Dispose();
        _mouseEncoder.Dispose();
        _renderState.Dispose();
        _terminal.Dispose();
    }
}
