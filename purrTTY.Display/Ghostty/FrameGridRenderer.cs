using Brutal.ImGuiApi;
using Brutal.Numerics;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Ghostty;

/// <summary>Draw-call counts from one <see cref="FrameGridRenderer.Render"/> (for the perf HUD).</summary>
internal struct GridRenderStats
{
    public int BackgroundRects;
    public int BlockRects;
    public int GlyphRuns;
    public int GlyphCells;
    public int DecorationLines;

    public readonly int TotalCalls => BackgroundRects + BlockRects + GlyphRuns + GlyphCells + DecorationLines;
}

/// <summary>
/// Draws a renderer-neutral <see cref="TerminalFrame"/> to an ImGui draw list:
/// cell backgrounds (with per-row selection), glyphs, text decorations, and the
/// cursor. Colors are already resolved by the engine, so it just packs them.
///
/// Submission is batched to keep the per-frame draw-call count proportional to
/// visual runs rather than cells: consecutive same-color backgrounds merge into
/// one rect, and consecutive ASCII glyphs sharing a font variant and color
/// merge into one AddText (only for variants whose ASCII advance was validated
/// to match the cell width — see <see cref="FrameFonts.CanBatchVariant"/>).
/// Block Elements (U+2580–U+259F) are drawn as exact rects instead of font
/// glyphs — pixel-perfect cell coverage with no hinting seams between cells,
/// and half-block "pixel" output (doom, chafa, notcurses) collapses into merged
/// color strips. The decoration pass is skipped for rows without decorations.
/// </summary>
internal static class FrameGridRenderer
{
    /// <summary>UTF-8 scratch for batched glyph runs. Render-thread only.</summary>
    private static byte[] s_runBuffer = new byte[512];

    public static uint ToU32(RgbaColor c)
        => c.R | ((uint)c.G << 8) | ((uint)c.B << 16) | ((uint)c.A << 24);

    public static uint ToU32(RgbaColor c, float opacity)
        => c.R | ((uint)c.G << 8) | ((uint)c.B << 16)
           | ((uint)(byte)Math.Clamp(c.A * opacity, 0f, 255f) << 24);

    public static GridRenderStats Render(
        TerminalFrame frame,
        ImDrawListPtr drawList,
        float2 origin,
        float cellWidth,
        float cellHeight,
        FrameFonts fonts,
        float fontSize,
        RgbaColor selectionColor,
        bool cursorOn,
        float foregroundOpacity = 1f,
        float cellBackgroundOpacity = 1f)
    {
        var stats = default(GridRenderStats);
        int cols = frame.Cols;
        if (s_runBuffer.Length < cols * 3)
        {
            // 3 bytes per column upper bound: batchable glyphs are single
            // UTF-16 units, which encode to at most 3 UTF-8 bytes.
            s_runBuffer = new byte[Math.Max(cols * 3, s_runBuffer.Length * 2)];
        }

        uint defaultBg32 = ToU32(frame.Colors.DefaultBackground, cellBackgroundOpacity);
        // Selection alpha comes from the selection color itself (not the cell
        // background opacity): it is composited *over* the cell backgrounds
        // below, so it stays visible even at CellBackgroundOpacity = 0 and
        // tints the underlying colors instead of replacing them.
        uint selection32 = ToU32(selectionColor);

        // Pass 1: backgrounds merged into horizontal runs, then the row's
        // selection highlight composited on top.
        for (int r = 0; r < frame.Rows; r++)
        {
            var row = frame.RowData[r];
            var cells = row.Cells;
            float y = origin.Y + r * cellHeight;
            float y2 = y + cellHeight;

            int runStart = -1;
            int runEnd = 0;
            uint runColor = 0;

            for (int c = 0; c < cols; c++)
            {
                ref readonly var cell = ref cells[c];
                if (cell.Width == CellWidth.Spacer)
                {
                    continue; // covered by the preceding wide cell's span
                }

                int span = cell.Width == CellWidth.Wide ? 2 : 1;
                uint color = ToU32(cell.Bg, cellBackgroundOpacity);
                bool draw = color != defaultBg32;

                if (draw && runStart >= 0 && c == runEnd && color == runColor)
                {
                    runEnd = c + span;
                    continue;
                }

                if (runStart >= 0)
                {
                    drawList.AddRectFilled(
                        new float2(origin.X + runStart * cellWidth, y),
                        new float2(origin.X + runEnd * cellWidth, y2),
                        runColor);
                    stats.BackgroundRects++;
                }

                if (draw)
                {
                    runStart = c;
                    runEnd = c + span;
                    runColor = color;
                }
                else
                {
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                drawList.AddRectFilled(
                    new float2(origin.X + runStart * cellWidth, y),
                    new float2(origin.X + runEnd * cellWidth, y2),
                    runColor);
                stats.BackgroundRects++;
            }

            if (row.HasSelection && row.SelectionEnd >= row.SelectionStart)
            {
                int selStart = Math.Clamp(row.SelectionStart, 0, cols - 1);
                int selLast = Math.Clamp(row.SelectionEnd, selStart, cols - 1);
                int selEnd = selLast + (cells[selLast].Width == CellWidth.Wide ? 2 : 1);
                drawList.AddRectFilled(
                    new float2(origin.X + selStart * cellWidth, y),
                    new float2(origin.X + Math.Min(selEnd, cols) * cellWidth, y2),
                    selection32);
                stats.BackgroundRects++;
            }
        }

        // Pass 2: glyphs. Consecutive batchable ASCII cells with the same font
        // variant + color become one AddText; blank gaps inside a run are
        // bridged with spaces so a typical text row costs one call per color
        // change instead of one per character. Block Elements become merged
        // rect strips. Anything else flushes and draws individually (per-cell
        // placement guarantees grid alignment where advances might not match
        // the cell width).
        var runScratch = s_runBuffer;
        for (int r = 0; r < frame.Rows; r++)
        {
            var row = frame.RowData[r];
            var cells = row.Cells;
            float y = origin.Y + r * cellHeight;

            int runStart = -1;   // first glyph column of the active text run
            int runNext = 0;     // next contiguous column the text run can absorb
            int runLen = 0;      // bytes used in runScratch
            int runVariant = 0;
            uint runColor = 0;

            // Active block-element strip run (full-width strips only).
            int blockStart = -1;
            int blockEnd = 0;
            uint blockColor = 0;
            float blockY0 = 0f;
            float blockY1 = 0f;

            for (int c = 0; c < cols; c++)
            {
                ref readonly var cell = ref cells[c];
                if (cell.Width == CellWidth.Spacer)
                {
                    continue;
                }

                var grapheme = cell.Grapheme;
                if (grapheme is null
                    || (cell.Flags & CellFlags.Invisible) != 0
                    || (grapheme.Length == 1 && grapheme[0] == ' '))
                {
                    continue; // nothing to draw; an active run may bridge over it
                }

                char ch = grapheme.Length == 1 ? grapheme[0] : '\0';

                // Block Elements: exact rects instead of font glyphs.
                if (ch >= '▀' && ch <= '▟' && cell.Width == CellWidth.Narrow)
                {
                    if (runStart >= 0)
                    {
                        stats.GlyphRuns++;
                        FlushRun(drawList, fonts, fontSize, origin, y, cellWidth, runStart, runVariant, runColor, runScratch, runLen);
                        runStart = -1;
                        runLen = 0;
                    }

                    if (TryGetBlockStrip(ch, out float fy0, out float fy1, out float shade))
                    {
                        uint col32 = shade >= 1f
                            ? ToU32(cell.Fg, foregroundOpacity)
                            : ToU32(cell.Fg, foregroundOpacity * shade);

                        if (blockStart >= 0 && c == blockEnd && col32 == blockColor && fy0 == blockY0 && fy1 == blockY1)
                        {
                            blockEnd = c + 1;
                        }
                        else
                        {
                            if (blockStart >= 0)
                            {
                                drawList.AddRectFilled(
                                    new float2(origin.X + blockStart * cellWidth, y + blockY0 * cellHeight),
                                    new float2(origin.X + blockEnd * cellWidth, y + blockY1 * cellHeight),
                                    blockColor);
                                stats.BlockRects++;
                            }

                            blockStart = c;
                            blockEnd = c + 1;
                            blockColor = col32;
                            blockY0 = fy0;
                            blockY1 = fy1;
                        }
                    }
                    else
                    {
                        if (blockStart >= 0)
                        {
                            drawList.AddRectFilled(
                                new float2(origin.X + blockStart * cellWidth, y + blockY0 * cellHeight),
                                new float2(origin.X + blockEnd * cellWidth, y + blockY1 * cellHeight),
                                blockColor);
                            stats.BlockRects++;
                            blockStart = -1;
                        }

                        stats.BlockRects += DrawBlockCell(
                            drawList, ch,
                            origin.X + c * cellWidth, y, cellWidth, cellHeight,
                            ToU32(cell.Fg, foregroundOpacity));
                    }

                    continue;
                }

                int variant = FrameFonts.VariantIndex(cell.Flags);
                uint fg32 = ToU32(cell.Fg, foregroundOpacity);

                // ASCII batches when the variant's measured advance matches the
                // cell width; other single-unit glyphs batch when their
                // individual advance has been validated.
                bool batchable = cell.Width == CellWidth.Narrow
                    && fonts.CanBatchVariant(variant)
                    && (ch > ' ' && ch < (char)127
                        || (ch >= (char)127 && !char.IsSurrogate(ch)
                            && fonts.BatchCache?.CanBatch(ch, variant, fonts.SelectVariant(variant), fontSize, cellWidth) == true));

                if (batchable)
                {
                    if (runStart >= 0 && variant == runVariant && fg32 == runColor && c >= runNext)
                    {
                        for (int gap = runNext; gap < c; gap++)
                        {
                            runScratch[runLen++] = (byte)' ';
                        }

                        runLen += AppendUtf8(runScratch, runLen, ch);
                        runNext = c + 1;
                        continue;
                    }

                    if (runStart >= 0)
                    {
                        stats.GlyphRuns++;
                        FlushRun(drawList, fonts, fontSize, origin, y, cellWidth, runStart, runVariant, runColor, runScratch, runLen);
                    }

                    runStart = c;
                    runNext = c + 1;
                    runVariant = variant;
                    runColor = fg32;
                    runLen = AppendUtf8(runScratch, 0, ch);
                    continue;
                }

                if (runStart >= 0)
                {
                    stats.GlyphRuns++;
                    FlushRun(drawList, fonts, fontSize, origin, y, cellWidth, runStart, runVariant, runColor, runScratch, runLen);
                    runStart = -1;
                    runLen = 0;
                }

                drawList.AddText(
                    fonts.SelectVariant(variant), fontSize,
                    new float2(origin.X + c * cellWidth, y), fg32, grapheme);
                stats.GlyphCells++;
            }

            if (runStart >= 0)
            {
                stats.GlyphRuns++;
                FlushRun(drawList, fonts, fontSize, origin, y, cellWidth, runStart, runVariant, runColor, runScratch, runLen);
            }

            if (blockStart >= 0)
            {
                drawList.AddRectFilled(
                    new float2(origin.X + blockStart * cellWidth, y + blockY0 * cellHeight),
                    new float2(origin.X + blockEnd * cellWidth, y + blockY1 * cellHeight),
                    blockColor);
                stats.BlockRects++;
            }
        }

        // Pass 3: text decorations (underline / strikethrough / overline) —
        // only for rows that have any.
        for (int r = 0; r < frame.Rows; r++)
        {
            var row = frame.RowData[r];
            if (!row.HasDecorations)
            {
                continue;
            }

            var cells = row.Cells;
            float y = origin.Y + r * cellHeight;
            for (int c = 0; c < cols; c++)
            {
                ref readonly var cell = ref cells[c];
                if (cell.Width == CellWidth.Spacer)
                {
                    continue;
                }

                bool decorated = cell.Underline != UnderlineStyle.None
                    || (cell.Flags & (CellFlags.Strikethrough | CellFlags.Overline)) != 0;
                if (!decorated || (cell.Flags & CellFlags.Invisible) != 0)
                {
                    continue; // invisible cells hide their decorations too
                }

                float span = cell.Width == CellWidth.Wide ? 2f : 1f;
                float x = origin.X + c * cellWidth;
                float x2 = x + cellWidth * span;

                if (cell.Underline != UnderlineStyle.None)
                {
                    uint lineColor = ToU32(cell.UnderlineColor ?? cell.Fg, foregroundOpacity);
                    float uy = y + cellHeight - 1.5f;
                    stats.DecorationLines += DrawUnderline(drawList, cell.Underline, x, x2, uy, lineColor);
                }

                if ((cell.Flags & CellFlags.Strikethrough) != 0)
                {
                    float sy = y + cellHeight * 0.5f;
                    drawList.AddLine(new float2(x, sy), new float2(x2, sy), ToU32(cell.Fg, foregroundOpacity), 1.0f);
                    stats.DecorationLines++;
                }

                if ((cell.Flags & CellFlags.Overline) != 0)
                {
                    drawList.AddLine(new float2(x, y + 1f), new float2(x2, y + 1f), ToU32(cell.Fg, foregroundOpacity), 1.0f);
                    stats.DecorationLines++;
                }
            }
        }

        // Pass 4: cursor.
        if (frame.Cursor.Visible && cursorOn
            && frame.Cursor.X >= 0 && frame.Cursor.X < frame.Cols
            && frame.Cursor.Y >= 0 && frame.Cursor.Y < frame.Rows)
        {
            DrawCursor(frame, drawList, origin, cellWidth, cellHeight, fonts, fontSize, foregroundOpacity);
        }

        return stats;
    }

    /// <summary>
    /// Draws one cell-span of underline in the requested SGR style and returns
    /// the primitive count. Single/double are solid lines; dotted/dashed are
    /// segment runs; curly is a small zigzag — all anchored at <paramref name="uy"/>.
    /// </summary>
    private static int DrawUnderline(ImDrawListPtr drawList, UnderlineStyle style, float x, float x2, float uy, uint color)
    {
        switch (style)
        {
            case UnderlineStyle.Double:
                drawList.AddLine(new float2(x, uy), new float2(x2, uy), color, 1.0f);
                drawList.AddLine(new float2(x, uy - 2f), new float2(x2, uy - 2f), color, 1.0f);
                return 2;

            case UnderlineStyle.Dotted:
            {
                int count = 0;
                for (float dx = x; dx < x2; dx += 3f)
                {
                    drawList.AddLine(new float2(dx, uy), new float2(Math.Min(dx + 1f, x2), uy), color, 1.0f);
                    count++;
                }

                return count;
            }

            case UnderlineStyle.Dashed:
            {
                // Two dashes per cell with a gap between (xterm-ish proportions).
                float width = x2 - x;
                float dash = width * 0.35f;
                drawList.AddLine(new float2(x, uy), new float2(x + dash, uy), color, 1.0f);
                drawList.AddLine(new float2(x + width * 0.5f, uy), new float2(x + width * 0.5f + dash, uy), color, 1.0f);
                return 2;
            }

            case UnderlineStyle.Curly:
            {
                // Zigzag approximation of the curl: half-cell period, ±1.5px amplitude.
                float width = x2 - x;
                float half = width * 0.25f;
                int count = 0;
                float prevX = x;
                float prevY = uy + 1.5f;
                for (float dx = x + half; dx <= x2 + 0.01f; dx += half)
                {
                    float ny = prevY > uy ? uy - 1.5f : uy + 1.5f;
                    drawList.AddLine(new float2(prevX, prevY), new float2(dx, ny), color, 1.0f);
                    prevX = dx;
                    prevY = ny;
                    count++;
                }

                return count;
            }

            default:
                drawList.AddLine(new float2(x, uy), new float2(x2, uy), color, 1.0f);
                return 1;
        }
    }

    /// <summary>
    /// Full-cell-width Block Element strips: vertical fraction [fy0, fy1) of the
    /// cell in the foreground color, with <paramref name="shade"/> as an alpha
    /// multiplier (for ░▒▓). These merge horizontally. Returns false for the
    /// partial-width / quadrant blocks handled by <see cref="DrawBlockCell"/>.
    /// </summary>
    private static bool TryGetBlockStrip(char ch, out float fy0, out float fy1, out float shade)
    {
        shade = 1f;
        switch (ch)
        {
            case '▀': // ▀ upper half
                fy0 = 0f;
                fy1 = 0.5f;
                return true;

            case >= '▁' and <= '█': // ▁..▇ lower eighths, █ full
                fy0 = 1f - (ch - 0x2580) / 8f;
                fy1 = 1f;
                return true;

            case '▔': // ▔ upper eighth
                fy0 = 0f;
                fy1 = 0.125f;
                return true;

            case '░': // ░ light shade
            case '▒': // ▒ medium shade
            case '▓': // ▓ dark shade
                fy0 = 0f;
                fy1 = 1f;
                shade = (ch - 0x2590) * 0.25f;
                return true;

            default:
                fy0 = 0f;
                fy1 = 0f;
                return false;
        }
    }

    // Quadrant occupancy masks for U+2596..U+259F (UL=1, UR=2, LL=4, LR=8).
    private static ReadOnlySpan<byte> QuadrantMasks => new byte[]
    {
        4,  // 2596 ▖
        8,  // 2597 ▗
        1,  // 2598 ▘
        13, // 2599 ▙
        9,  // 259A ▚
        7,  // 259B ▛
        11, // 259C ▜
        2,  // 259D ▝
        6,  // 259E ▞
        14, // 259F ▟
    };

    /// <summary>Draws the partial-width / quadrant Block Elements. Returns the rect count.</summary>
    private static int DrawBlockCell(
        ImDrawListPtr drawList, char ch, float x, float y, float cellWidth, float cellHeight, uint fg)
    {
        switch (ch)
        {
            case >= '▉' and <= '▏': // ▉..▏ left fractions 7/8..1/8
            {
                float w = (0x2590 - ch) / 8f * cellWidth;
                drawList.AddRectFilled(new float2(x, y), new float2(x + w, y + cellHeight), fg);
                return 1;
            }

            case '▐': // ▐ right half
                drawList.AddRectFilled(new float2(x + cellWidth * 0.5f, y), new float2(x + cellWidth, y + cellHeight), fg);
                return 1;

            case '▕': // ▕ right eighth
                drawList.AddRectFilled(new float2(x + cellWidth * 0.875f, y), new float2(x + cellWidth, y + cellHeight), fg);
                return 1;

            case >= '▖' and <= '▟': // quadrants
            {
                int mask = QuadrantMasks[ch - 0x2596];
                float mx = x + cellWidth * 0.5f;
                float my = y + cellHeight * 0.5f;
                int rects = 0;
                if ((mask & 1) != 0) { drawList.AddRectFilled(new float2(x, y), new float2(mx, my), fg); rects++; }
                if ((mask & 2) != 0) { drawList.AddRectFilled(new float2(mx, y), new float2(x + cellWidth, my), fg); rects++; }
                if ((mask & 4) != 0) { drawList.AddRectFilled(new float2(x, my), new float2(mx, y + cellHeight), fg); rects++; }
                if ((mask & 8) != 0) { drawList.AddRectFilled(new float2(mx, my), new float2(x + cellWidth, y + cellHeight), fg); rects++; }
                return rects;
            }

            default:
                return 0;
        }
    }

    /// <summary>Encodes a single UTF-16 unit (never a surrogate) as UTF-8; returns bytes written.</summary>
    private static int AppendUtf8(byte[] buffer, int offset, char ch)
    {
        if (ch < 0x80)
        {
            buffer[offset] = (byte)ch;
            return 1;
        }

        if (ch < 0x800)
        {
            buffer[offset] = (byte)(0xC0 | (ch >> 6));
            buffer[offset + 1] = (byte)(0x80 | (ch & 0x3F));
            return 2;
        }

        buffer[offset] = (byte)(0xE0 | (ch >> 12));
        buffer[offset + 1] = (byte)(0x80 | ((ch >> 6) & 0x3F));
        buffer[offset + 2] = (byte)(0x80 | (ch & 0x3F));
        return 3;
    }

    private static void FlushRun(
        ImDrawListPtr drawList,
        in FrameFonts fonts,
        float fontSize,
        float2 origin,
        float y,
        float cellWidth,
        int startCol,
        int variant,
        uint color,
        byte[] buffer,
        int length)
    {
        drawList.AddText(
            fonts.SelectVariant(variant), fontSize,
            new float2(origin.X + startCol * cellWidth, y), color,
            (ReadOnlySpan<byte>)buffer.AsSpan(0, length));
    }

    private static void DrawCursor(
        TerminalFrame frame,
        ImDrawListPtr drawList,
        float2 origin,
        float cellWidth,
        float cellHeight,
        FrameFonts fonts,
        float fontSize,
        float foregroundOpacity)
    {
        float x = origin.X + frame.Cursor.X * cellWidth;
        float y = origin.Y + frame.Cursor.Y * cellHeight;
        uint cursorColor = ToU32(frame.Colors.Cursor, foregroundOpacity);
        var min = new float2(x, y);
        var max = new float2(x + cellWidth, y + cellHeight);

        switch (frame.Cursor.Shape)
        {
            case CursorShape.Block:
                drawList.AddRectFilled(min, max, cursorColor);
                // Re-draw the glyph beneath in the background color for contrast,
                // at the same opacity the glyph itself was drawn with.
                var cell = frame.RowData[frame.Cursor.Y].Cells[frame.Cursor.X];
                if (!string.IsNullOrEmpty(cell.Grapheme) && cell.Grapheme != " ")
                {
                    uint glyphColor = ToU32(frame.Colors.DefaultBackground, foregroundOpacity);
                    char ch = cell.Grapheme.Length == 1 ? cell.Grapheme[0] : '\0';
                    if (ch >= '▀' && ch <= '▟' && cell.Width == CellWidth.Narrow)
                    {
                        // Block Elements are drawn as exact rects everywhere else;
                        // going through the font here would reintroduce hinting
                        // seams under the cursor.
                        if (TryGetBlockStrip(ch, out float fy0, out float fy1, out float shade))
                        {
                            drawList.AddRectFilled(
                                new float2(x, y + fy0 * cellHeight),
                                new float2(x + cellWidth, y + fy1 * cellHeight),
                                shade >= 1f ? glyphColor : ToU32(frame.Colors.DefaultBackground, foregroundOpacity * shade));
                        }
                        else
                        {
                            DrawBlockCell(drawList, ch, x, y, cellWidth, cellHeight, glyphColor);
                        }
                    }
                    else
                    {
                        drawList.AddText(fonts.Select(cell.Flags), fontSize, min, glyphColor, cell.Grapheme);
                    }
                }
                break;

            case CursorShape.BlockHollow:
                drawList.AddRect(min, max, cursorColor);
                break;

            case CursorShape.Bar:
                drawList.AddRectFilled(min, new float2(x + 2f, y + cellHeight), cursorColor);
                break;

            case CursorShape.Underline:
                drawList.AddRectFilled(new float2(x, y + cellHeight - 2f), max, cursorColor);
                break;
        }
    }
}
