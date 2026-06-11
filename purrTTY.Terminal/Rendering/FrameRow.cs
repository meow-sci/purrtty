namespace PurrTTY.Terminal.Rendering;

/// <summary>
/// One row of the rendered viewport. The backing <see cref="Cells"/> array is
/// reused across frames (length == frame column count); read only the first
/// <c>Cols</c> entries. <see cref="SelectionStart"/>/<see cref="SelectionEnd"/>
/// describe the inclusive row-local selection range, or are both -1 when this
/// row is not selected.
/// </summary>
public sealed class FrameRow
{
    public FrameCell[] Cells;

    /// <summary>First selected column (inclusive), or -1 if no selection on this row.</summary>
    public int SelectionStart;

    /// <summary>Last selected column (inclusive), or -1 if no selection on this row.</summary>
    public int SelectionEnd;

    /// <summary>Whether this row is a soft-wrap continuation of the previous one.</summary>
    public bool WrapContinuation;

    /// <summary>
    /// Whether any cell in this row carries a text decoration (underline,
    /// strikethrough, overline). Lets renderers skip the decoration pass for
    /// the common undecorated row.
    /// </summary>
    public bool HasDecorations;

    public FrameRow(int cols)
    {
        Cells = new FrameCell[cols];
        SelectionStart = -1;
        SelectionEnd = -1;
    }

    public bool HasSelection => SelectionStart >= 0;

    internal void EnsureCapacity(int cols)
    {
        if (Cells.Length != cols)
        {
            Cells = new FrameCell[cols];
        }
    }
}
