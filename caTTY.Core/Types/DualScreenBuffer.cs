using System;

namespace caTTY.Core.Types;

/// <summary>
///     A switchable screen buffer that maintains independent primary and alternate buffers.
///     Reads/writes delegate to the active buffer based on <paramref name="isAlternateActive"/>.
/// </summary>
public sealed class DualScreenBuffer : IScreenBuffer
{
    private readonly Func<bool> _isAlternateActive;
    private ScreenBuffer _primary;
    private ScreenBuffer _alternate;

    public DualScreenBuffer(int width, int height, Func<bool> isAlternateActive)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _isAlternateActive = isAlternateActive ?? throw new ArgumentNullException(nameof(isAlternateActive));

        _primary = new ScreenBuffer(width, height);
        _alternate = new ScreenBuffer(width, height);
    }

    public int Width => Active.Width;
    public int Height => Active.Height;

    /// <summary>
    ///     Gets the current content revision number.
    ///     Combines the active buffer's revision with a high-bit flag for the alternate screen
    ///     to ensure keys are distinct when switching buffers.
    /// </summary>
    public long Revision 
    {
        get
        {
            long rev = Active.Revision;
            if (_isAlternateActive())
            {
                // Set the second highest bit to indicate alternate screen
                // (Highest bit is sign bit, avoiding it just in case)
                rev |= 0x4000000000000000;
            }
            return rev;
        }
    }

    public ScreenBuffer Primary => _primary;
    public ScreenBuffer Alternate => _alternate;

    private ScreenBuffer Active => _isAlternateActive() ? _alternate : _primary;

    public Cell GetCell(int row, int col) => Active.GetCell(row, col);

    public void SetCell(int row, int col, Cell cell) => Active.SetCell(row, col, cell);

    public void Clear() => Active.Clear();

    public void ClearRow(int row) => Active.ClearRow(row);

    public void ClearRegion(int startRow, int startCol, int endRow, int endCol) =>
        Active.ClearRegion(startRow, startCol, endRow, endCol);

    public ReadOnlySpan<Cell> GetRow(int row) => Active.GetRow(row);

    public ReadOnlyMemory<Cell> GetRowMemory(int row) => Active.GetRowMemory(row);

    public void ScrollUp(int lines) => Active.ScrollUp(lines);

    public void ScrollDown(int lines) => Active.ScrollDown(lines);

    public void CopyTo(Span<Cell> destination, int startRow, int endRow) =>
        Active.CopyTo(destination, startRow, endRow);

    public void Resize(int newWidth, int newHeight)
    {
        _primary.Resize(newWidth, newHeight);
        _alternate.Resize(newWidth, newHeight);
    }

    public bool IsInBounds(int row, int col) => Active.IsInBounds(row, col);

    public bool IsRowDirty(int row) => Active.IsRowDirty(row);

    public bool HasAnyDirtyRows() => Active.HasAnyDirtyRows();

    public void ClearDirtyFlags() => Active.ClearDirtyFlags();

    public void MarkAllRowsDirty() => Active.MarkAllRowsDirty();

    public void ClearAlternate()
    {
        _alternate.Clear();
    }
}
