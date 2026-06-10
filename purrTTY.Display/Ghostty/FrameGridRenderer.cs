using Brutal.ImGuiApi;
using Brutal.Numerics;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Draws a renderer-neutral <see cref="TerminalFrame"/> to an ImGui draw list:
/// cell backgrounds (with per-row selection), glyphs, text decorations, and the
/// cursor. Colors are already resolved by the engine, so it just packs them.
/// </summary>
internal static class FrameGridRenderer
{
    public static uint ToU32(RgbaColor c)
        => c.R | ((uint)c.G << 8) | ((uint)c.B << 16) | ((uint)c.A << 24);

    public static uint ToU32(RgbaColor c, float opacity)
        => c.R | ((uint)c.G << 8) | ((uint)c.B << 16)
           | ((uint)(byte)Math.Clamp(c.A * opacity, 0f, 255f) << 24);

    public static void Render(
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
        var defaultBg = frame.Colors.DefaultBackground;

        // Pass 1: backgrounds + selection (no font needed).
        for (int r = 0; r < frame.Rows; r++)
        {
            var row = frame.RowData[r];
            float y = origin.Y + r * cellHeight;
            for (int c = 0; c < frame.Cols; c++)
            {
                var cell = row.Cells[c];
                if (cell.Width == CellWidth.Spacer)
                {
                    continue;
                }

                float span = cell.Width == CellWidth.Wide ? 2f : 1f;
                bool selected = row.HasSelection && c >= row.SelectionStart && c <= row.SelectionEnd;
                var bg = selected ? selectionColor : cell.Bg;

                if (selected || !bg.Equals(defaultBg))
                {
                    float x = origin.X + c * cellWidth;
                    drawList.AddRectFilled(
                        new float2(x, y),
                        new float2(x + cellWidth * span, y + cellHeight),
                        ToU32(bg, cellBackgroundOpacity));
                }
            }
        }

        // Pass 2: glyphs. The variant (regular/bold/italic/bold-italic) is chosen
        // per cell from its flags; the explicit-font AddText overload avoids any
        // PushFont/PopFont churn between cells.
        for (int r = 0; r < frame.Rows; r++)
        {
            var row = frame.RowData[r];
            float y = origin.Y + r * cellHeight;
            for (int c = 0; c < frame.Cols; c++)
            {
                var cell = row.Cells[c];
                if (cell.Width == CellWidth.Spacer
                    || string.IsNullOrEmpty(cell.Grapheme)
                    || cell.Grapheme == " "
                    || (cell.Flags & CellFlags.Invisible) != 0)
                {
                    continue;
                }

                float x = origin.X + c * cellWidth;
                drawList.AddText(fonts.Select(cell.Flags), fontSize, new float2(x, y), ToU32(cell.Fg, foregroundOpacity), cell.Grapheme);
            }
        }

        // Pass 3: text decorations (underline / strikethrough / overline).
        for (int r = 0; r < frame.Rows; r++)
        {
            var row = frame.RowData[r];
            float y = origin.Y + r * cellHeight;
            for (int c = 0; c < frame.Cols; c++)
            {
                var cell = row.Cells[c];
                if (cell.Width == CellWidth.Spacer)
                {
                    continue;
                }

                float span = cell.Width == CellWidth.Wide ? 2f : 1f;
                float x = origin.X + c * cellWidth;
                float x2 = x + cellWidth * span;
                uint lineColor = ToU32(cell.UnderlineColor ?? cell.Fg, foregroundOpacity);

                if (cell.Underline != UnderlineStyle.None)
                {
                    float uy = y + cellHeight - 1.5f;
                    drawList.AddLine(new float2(x, uy), new float2(x2, uy), lineColor, 1.0f);
                    if (cell.Underline == UnderlineStyle.Double)
                    {
                        drawList.AddLine(new float2(x, uy - 2f), new float2(x2, uy - 2f), lineColor, 1.0f);
                    }
                }

                if ((cell.Flags & CellFlags.Strikethrough) != 0)
                {
                    float sy = y + cellHeight * 0.5f;
                    drawList.AddLine(new float2(x, sy), new float2(x2, sy), ToU32(cell.Fg, foregroundOpacity), 1.0f);
                }

                if ((cell.Flags & CellFlags.Overline) != 0)
                {
                    drawList.AddLine(new float2(x, y + 1f), new float2(x2, y + 1f), ToU32(cell.Fg, foregroundOpacity), 1.0f);
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
                // Re-draw the glyph beneath in the background color for contrast.
                var cell = frame.RowData[frame.Cursor.Y].Cells[frame.Cursor.X];
                if (!string.IsNullOrEmpty(cell.Grapheme) && cell.Grapheme != " ")
                {
                    drawList.AddText(fonts.Select(cell.Flags), fontSize, min, ToU32(frame.Colors.DefaultBackground), cell.Grapheme);
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
