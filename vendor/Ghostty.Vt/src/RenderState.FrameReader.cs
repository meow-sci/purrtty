using Ghostty.Vt.Enums;
using Ghostty.Vt.Native;
using Ghostty.Vt.Types;

namespace Ghostty.Vt;

// purrtty addition: the render-hot frame read path.
//
// The general-purpose RenderStateRowEnumerator/RenderStateCellEnumerator pair
// costs ~9 native calls and a string allocation per cell, which dominates the
// per-tick CPU budget for large grids (a small-font fullscreen grid is tens of
// thousands of cells). This reader gets a dirty-row-aware frame consume down
// to 2 native calls per cell (3 for styled cells):
//
//   - the packed cell (GhosttyCell, a u64 mirror of ghostty's
//     `page.Cell packed struct(u64)`) is decoded entirely in managed code via
//     RawCell instead of per-field ghostty_cell_get round-trips;
//   - the style struct is fetched only when the cell's style_id is non-zero;
//   - grapheme clusters are read as UTF-8 into a caller buffer
//     (ROW_CELLS_DATA_GRAPHEMES_UTF8) instead of codepoint arrays + StringBuilder;
//   - one cells handle is created per reader and re-bound per row instead of a
//     native alloc/free pair per row;
//   - per-row and state-level dirty flags can be cleared after consuming, which
//     is what makes RenderState.Dirty meaningful frame-over-frame (the engine
//     never clears them itself).
//
// RawCell mirrors the bit layout of the PINNED native library (see
// vendor/Ghostty.Vt/README.md). ValidateNativeLayout() cross-checks the decode
// against ghostty_cell_get at runtime so a future pin bump that changes the
// layout is caught loudly instead of rendering garbage silently.
public sealed partial class RenderState
{
    /// <summary>
    /// Clears the state-level dirty flag. Call after consuming a frame; the
    /// engine only ever raises the flag, never lowers it.
    /// </summary>
    public unsafe void ClearDirty()
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        int value = (int)RenderStateDirty.False;
        NativeMethods.ghostty_render_state_set(
            _handle.DangerousGetHandle(), 0 /* GHOSTTY_RENDER_STATE_OPTION_DIRTY */, &value);
    }

    /// <summary>
    /// Creates the optimized frame reader. Must be disposed; do not outlive the
    /// next <see cref="Update"/> call.
    /// </summary>
    public RenderFrameReader CreateFrameReader()
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        return new RenderFrameReader(_handle.DangerousGetHandle());
    }
}

/// <summary>
/// purrtty addition: a managed mirror of ghostty's <c>page.Cell</c>
/// (<c>packed struct(u64)</c>). Decoding the packed value locally replaces four
/// to five <c>ghostty_cell_get</c> native calls per cell.
/// Bit layout (LSB first): content_tag u2 | content u24 (codepoint u21 /
/// palette u8 / rgb u24) | style_id u16 | wide u2 | protected u1 | hyperlink u1 |
/// semantic_content u2 | padding u16.
/// </summary>
public readonly struct RawCell
{
    public const int TagCodepoint = 0;
    public const int TagCodepointGrapheme = 1;
    public const int TagBgColorPalette = 2;
    public const int TagBgColorRgb = 3;

    public readonly ulong Bits;

    public RawCell(ulong bits) => Bits = bits;

    public int ContentTag => (int)(Bits & 0x3);

    /// <summary>First (or only) codepoint; 0 for blank and bg-only cells.</summary>
    public uint Codepoint => ContentTag <= TagCodepointGrapheme ? (uint)((Bits >> 2) & 0x1FFFFF) : 0u;

    /// <summary>True when the cell is a multi-codepoint grapheme cluster.</summary>
    public bool IsGraphemeCluster => ContentTag == TagCodepointGrapheme;

    /// <summary>True when the cell carries a background color in its content (no text, no style lookup).</summary>
    public bool HasBgContent => ContentTag >= TagBgColorPalette;

    /// <summary>Palette index (valid when ContentTag == TagBgColorPalette).</summary>
    public byte BgPaletteIndex => (byte)(Bits >> 2);

    /// <summary>RGB color (valid when ContentTag == TagBgColorRgb).</summary>
    public ColorRgb BgRgb => new()
    {
        R = (byte)(Bits >> 2),
        G = (byte)(Bits >> 10),
        B = (byte)(Bits >> 18),
    };

    public ushort StyleId => (ushort)(Bits >> 26);

    /// <summary>True when a style struct should be fetched (style_id != default).</summary>
    public bool HasStyling => StyleId != 0;

    public CellWide Wide => (CellWide)((Bits >> 42) & 0x3);

    public bool HasText => ContentTag <= TagCodepointGrapheme && ((Bits >> 2) & 0x1FFFFF) != 0;
}

/// <summary>
/// purrtty addition: forward-only reader over the render state's rows/cells,
/// optimized for the per-tick frame rebuild. See the file header for the
/// native-call budget. Not thread-safe; single tick-thread use only.
/// </summary>
public unsafe ref struct RenderFrameReader
{
    private nint _rowIterator;
    private nint _cells;

    internal RenderFrameReader(nint state)
    {
        nint iter = 0;
        var result = NativeMethods.ghostty_render_state_row_iterator_new(nint.Zero, &iter);
        GhosttyException.ThrowIfFailure(result);
        _rowIterator = iter;

        result = NativeMethods.ghostty_render_state_get(
            state, (int)RenderStateData.RowIterator, &iter);
        if (result != 0)
        {
            NativeMethods.ghostty_render_state_row_iterator_free(_rowIterator);
            _rowIterator = 0;
            GhosttyException.ThrowIfFailure(result);
        }

        nint cells = 0;
        result = NativeMethods.ghostty_render_state_row_cells_new(nint.Zero, &cells);
        if (result != 0)
        {
            NativeMethods.ghostty_render_state_row_iterator_free(_rowIterator);
            _rowIterator = 0;
            GhosttyException.ThrowIfFailure(result);
        }

        _cells = cells;
    }

    /// <summary>Advances to the next row. Must be called before any row accessor.</summary>
    public bool NextRow() => NativeMethods.ghostty_render_state_row_iterator_next(_rowIterator);

    /// <summary>Whether the current row changed since its dirty flag was last cleared.</summary>
    public bool RowDirty
    {
        get
        {
            byte dirty = 0;
            NativeMethods.ghostty_render_state_row_get(_rowIterator, 1 /* ROW_DATA_DIRTY */, &dirty);
            return dirty != 0;
        }
    }

    /// <summary>Clears the current row's dirty flag after its content has been consumed.</summary>
    public void ClearRowDirty()
    {
        byte value = 0;
        NativeMethods.ghostty_render_state_row_set(_rowIterator, 0 /* ROW_OPTION_DIRTY */, &value);
    }

    /// <summary>Row-local selection range, or null when the row is not selected.</summary>
    public RowSelection? RowSelection
    {
        get
        {
            var sel = new GhosttyRenderStateRowSelectionNative
            {
                Size = (nuint)sizeof(GhosttyRenderStateRowSelectionNative),
            };
            if (NativeMethods.ghostty_render_state_row_get(_rowIterator, 4 /* ROW_DATA_SELECTION */, &sel) != 0)
            {
                return null;
            }

            return new RowSelection { StartX = sel.StartX, EndX = sel.EndX };
        }
    }

    /// <summary>Whether the current row is a soft-wrap continuation of the previous one.</summary>
    public bool RowWrapContinuation
    {
        get
        {
            ulong rawRow = 0;
            NativeMethods.ghostty_render_state_row_get(_rowIterator, 2 /* ROW_DATA_RAW */, &rawRow);
            if (rawRow == 0)
            {
                return false;
            }

            byte wrapContinuation = 0;
            NativeMethods.ghostty_row_get(rawRow, 2 /* ROW_DATA_WRAP_CONTINUATION */, &wrapContinuation);
            return wrapContinuation != 0;
        }
    }

    /// <summary>Binds the reusable cells handle to the current row. Call once per consumed row.</summary>
    public void BindRowCells()
    {
        nint cells = _cells;
        var result = NativeMethods.ghostty_render_state_row_get(
            _rowIterator, 3 /* ROW_DATA_CELLS */, &cells);
        GhosttyException.ThrowIfFailure(result);
    }

    /// <summary>Advances to the next cell of the bound row.</summary>
    public bool NextCell() => NativeMethods.ghostty_render_state_row_cells_next(_cells);

    /// <summary>The packed cell at the current position, decoded managed-side.</summary>
    public RawCell Cell
    {
        get
        {
            ulong raw = 0;
            NativeMethods.ghostty_render_state_row_cells_get(_cells, 1 /* ROW_CELLS_DATA_RAW */, &raw);
            return new RawCell(raw);
        }
    }

    /// <summary>The style struct for the current cell. Only meaningful when <see cref="RawCell.HasStyling"/>.</summary>
    public Style CellStyle
    {
        get
        {
            Style style = default;
            style.Size = (nuint)sizeof(Style);
            NativeMethods.ghostty_render_state_row_cells_get(_cells, 2 /* ROW_CELLS_DATA_STYLE */, &style);
            return style;
        }
    }

    /// <summary>
    /// Writes the current cell's full grapheme cluster as UTF-8 into
    /// <paramref name="destination"/>. Returns the byte count, 0 for cells
    /// without text, or a negative value whose magnitude is the required
    /// buffer size when <paramref name="destination"/> is too small.
    /// </summary>
    public int ReadCellGraphemeUtf8(scoped Span<byte> destination)
    {
        fixed (byte* ptr = destination)
        {
            var buffer = new GhosttyBufferNative
            {
                Ptr = (nint)ptr,
                Cap = (nuint)destination.Length,
                Len = 0,
            };
            var result = NativeMethods.ghostty_render_state_row_cells_get(
                _cells, 9 /* ROW_CELLS_DATA_GRAPHEMES_UTF8 */, &buffer);
            return result == 0 ? (int)buffer.Len : -(int)buffer.Len;
        }
    }

    public void Dispose()
    {
        if (_cells != 0)
        {
            NativeMethods.ghostty_render_state_row_cells_free(_cells);
            _cells = 0;
        }

        if (_rowIterator != 0)
        {
            NativeMethods.ghostty_render_state_row_iterator_free(_rowIterator);
            _rowIterator = 0;
        }
    }
}

/// <summary>
/// purrtty addition: runtime cross-check of <see cref="RawCell"/>'s managed bit
/// decode against the native <c>ghostty_cell_get</c> accessors, using a
/// throwaway terminal. Run once at startup; a failure means the pinned native
/// library's packed cell layout changed and the fast decode must be updated.
/// </summary>
public static class RawCellLayout
{
    /// <summary>Validates the layout; returns false (with a description) on mismatch.</summary>
    public static unsafe bool Validate(out string? error)
    {
        error = null;

        using var terminal = new Terminal(8, 1);
        using var state = new RenderState();

        // Cell 0/1: styled wide text ('你' is wide, bold red on green bg) → exercises
        //           HAS_STYLING + a non-zero STYLE_ID.
        // Cell 2:   unstyled ASCII 'A'.
        // Cols 3-4: erased under an RGB background (content-tag bg_color_rgb cells).
        // Cols 5-7: re-erased under a 256-color palette background (content-tag
        //           bg_color_palette cells) — exercises the BgPaletteIndex path.
        // The second EL overwrites cols 5-7 of the first, leaving both bg kinds.
        terminal.VTWrite("\x1b[1;31;42m你\x1b[0mA\x1b[48;2;10;20;30m\x1b[K\x1b[6G\x1b[48;5;42m\x1b[K"u8);
        state.Update(terminal);

        using var reader = state.CreateFrameReader();
        if (!reader.NextRow())
        {
            error = "no rows in render state";
            return false;
        }

        bool sawBgRgb = false;
        bool sawBgPalette = false;
        bool sawStyled = false;

        reader.BindRowCells();
        int x = 0;
        while (reader.NextCell())
        {
            var cell = reader.Cell;

            byte hasText = 0;
            NativeMethods.ghostty_cell_get(cell.Bits, 4 /* HAS_TEXT */, &hasText);
            int wide = 0;
            NativeMethods.ghostty_cell_get(cell.Bits, 3 /* WIDE */, &wide);
            byte hasStyling = 0;
            NativeMethods.ghostty_cell_get(cell.Bits, 5 /* HAS_STYLING */, &hasStyling);
            uint codepoint = 0;
            NativeMethods.ghostty_cell_get(cell.Bits, 1 /* CODEPOINT */, &codepoint);
            ushort styleId = 0;
            NativeMethods.ghostty_cell_get(cell.Bits, 6 /* STYLE_ID */, &styleId);

            if (cell.HasText != (hasText != 0)
                || (int)cell.Wide != wide
                || cell.HasStyling != (hasStyling != 0)
                || cell.Codepoint != codepoint
                || cell.StyleId != styleId)
            {
                error = $"cell {x} decode mismatch (bits=0x{cell.Bits:X16}): "
                        + $"hasText {cell.HasText}/{hasText != 0}, wide {(int)cell.Wide}/{wide}, "
                        + $"styled {cell.HasStyling}/{hasStyling != 0}, cp {cell.Codepoint}/{codepoint}, "
                        + $"styleId {cell.StyleId}/{styleId}";
                return false;
            }

            if (cell.HasStyling && cell.StyleId != 0)
                sawStyled = true;

            // The erased cells must decode the RGB background written above.
            if (cell.ContentTag == RawCell.TagBgColorRgb)
            {
                var rgb = cell.BgRgb;
                if (rgb.R != 10 || rgb.G != 20 || rgb.B != 30)
                {
                    error = $"cell {x} bg-rgb decode mismatch (bits=0x{cell.Bits:X16}): "
                            + $"got ({rgb.R},{rgb.G},{rgb.B}), expected (10,20,30)";
                    return false;
                }
                sawBgRgb = true;
            }

            // The palette-erased cells must decode the 256-color index, and the
            // managed decode must agree with the native COLOR_PALETTE accessor.
            if (cell.ContentTag == RawCell.TagBgColorPalette)
            {
                byte nativeIdx = 0;
                NativeMethods.ghostty_cell_get(cell.Bits, 10 /* COLOR_PALETTE */, &nativeIdx);
                if (cell.BgPaletteIndex != nativeIdx || cell.BgPaletteIndex != 42)
                {
                    error = $"cell {x} bg-palette decode mismatch (bits=0x{cell.Bits:X16}): "
                            + $"managed {cell.BgPaletteIndex}, native {nativeIdx}, expected 42";
                    return false;
                }
                sawBgPalette = true;
            }

            x++;
        }

        // Each branch above must actually have run, or a drifted content-tag bit
        // layout would skip the check silently and the tripwire would pass blind.
        if (!sawStyled) { error = "no styled cell produced — STYLE_ID path unexercised"; return false; }
        if (!sawBgRgb) { error = "no bg_color_rgb cell produced — RGB-bg path unexercised"; return false; }
        if (!sawBgPalette) { error = "no bg_color_palette cell produced — palette-bg path unexercised"; return false; }
        return true;
    }
}
