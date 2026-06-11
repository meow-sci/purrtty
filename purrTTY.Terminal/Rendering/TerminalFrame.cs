namespace PurrTTY.Terminal.Rendering;

/// <summary>Cursor position and appearance for a frame.</summary>
public struct CursorState
{
    public int X;
    public int Y;
    public bool Visible;
    public CursorShape Shape;
    public bool Blinking;
}

/// <summary>Viewport scroll state, sufficient to render a scrollbar.</summary>
public struct ScrollbarState
{
    /// <summary>Rows scrolled up from the bottom (0 == at the live edge).</summary>
    public int Offset;

    /// <summary>Number of visible rows.</summary>
    public int ViewportHeight;

    /// <summary>Number of rows held in scrollback above the viewport.</summary>
    public int ScrollbackHeight;

    public readonly bool AtBottom => Offset <= 0;
}

/// <summary>
/// Frame-level fallback colors (the resolved default fg/bg, cursor color, and
/// the 256-entry palette). Per-cell colors are already resolved; these are for
/// blanks, cursor rendering, and frontend conveniences.
/// </summary>
public sealed class FrameColors
{
    public RgbaColor DefaultForeground;
    public RgbaColor DefaultBackground;
    public RgbaColor Cursor;
    public RgbaColor[] Palette = new RgbaColor[256];
}

/// <summary>
/// An immutable-for-the-frame snapshot of everything a frontend needs to draw
/// the terminal: dimensions, rows of cells, cursor, scrollbar, and fallback
/// colors. The backing arrays are owned and reused by the producing surface;
/// read them only within the tick that produced the frame. <see cref="Generation"/>
/// increments whenever the visible content changes, enabling skip-rendering.
/// </summary>
public sealed class TerminalFrame
{
    public int Cols { get; internal set; }
    public int Rows { get; internal set; }

    /// <summary>Monotonic counter; bumped on every content change.</summary>
    public long Generation { get; internal set; }

    /// <summary>Viewport rows, top to bottom. Length may exceed <see cref="Rows"/>; use <see cref="Rows"/>.</summary>
    public FrameRow[] RowData { get; internal set; }

    public CursorState Cursor;
    public ScrollbarState Scrollbar;
    public FrameColors Colors { get; } = new FrameColors();

    internal TerminalFrame(int cols, int rows)
    {
        Cols = cols;
        Rows = rows;
        RowData = new FrameRow[rows];
        for (int i = 0; i < rows; i++)
        {
            RowData[i] = new FrameRow(cols);
        }
    }

    /// <summary>Resizes the backing row/cell buffers, preserving instances where possible.</summary>
    internal void Resize(int cols, int rows)
    {
        if (RowData.Length < rows)
        {
            var grown = new FrameRow[rows];
            Array.Copy(RowData, grown, RowData.Length);
            for (int i = RowData.Length; i < rows; i++)
            {
                grown[i] = new FrameRow(cols);
            }
            RowData = grown;
        }

        for (int i = 0; i < rows; i++)
        {
            RowData[i].EnsureCapacity(cols);
        }

        Cols = cols;
        Rows = rows;
    }
}
