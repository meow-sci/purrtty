using Brutal.ImGuiApi;
using Brutal.Numerics;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Draws a frame's kitty-graphics image placements into the window draw list,
/// one z-band at a time. <c>belowText=true</c> draws z&lt;0 placements (under the
/// glyphs); <c>belowText=false</c> draws z&gt;=0 (over the glyphs). Images are
/// sized to their cell span using the frontend's (fractional) cell metrics so
/// they stay grid-aligned, and clipped to the grid rect so a partially-scrolled
/// image can't bleed over the window chrome.
/// </summary>
internal static class KittyImageRenderer
{
    public static void Draw(
        TerminalFrame frame,
        ImageTextureCache cache,
        ImDrawListPtr drawList,
        float2 origin,
        float cellWidth,
        float cellHeight,
        int gridCols,
        int gridRows,
        float opacity,
        bool belowText)
    {
        var placements = frame.ImagePlacements;
        if (placements.Length == 0)
        {
            return;
        }

        byte alpha = (byte)Math.Clamp(opacity * 255f, 0f, 255f);
        if (alpha == 0)
        {
            return;
        }
        var tint = new ImColor8(255, 255, 255, alpha);

        var clipMin = origin;
        var clipMax = new float2(origin.X + gridCols * cellWidth, origin.Y + gridRows * cellHeight);
        drawList.PushClipRect(clipMin, clipMax, intersectWithCurrentClipRect: true);

        for (int i = 0; i < placements.Length; i++)
        {
            ref readonly var p = ref placements[i];
            if (p.Z < 0 != belowText)
            {
                continue;
            }

            if (!cache.TryGet(p.ImageId, out var texRef))
            {
                continue; // not uploaded (decode failed / evicted) — skip this frame
            }

            // Size by cell span in fractional metrics so the image lands exactly on
            // the grid the engine reserved for it (kitty scales the image to c x r
            // cells; pixel-exact natural sizing for no-c/r placements is Phase 3).
            float x0 = origin.X + p.Col * cellWidth;
            float y0 = origin.Y + p.Row * cellHeight;
            float x1 = x0 + p.WidthCells * cellWidth;
            float y1 = y0 + p.HeightCells * cellHeight;

            drawList.AddImage(texRef, new float2(x0, y0), new float2(x1, y1), null, null, tint);
        }

        drawList.PopClipRect();
    }
}
