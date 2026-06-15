using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using PurrTTY.Terminal.Ghostty;
using PurrTTY.Terminal.Sessions;

namespace purrTTY.Display.Ghostty;

// The performance HUD overlay (Window menu → "Show performance HUD").
public sealed partial class TerminalWindow
{
    /// <summary>Debug overlay with frame-build / submit timings and draw-call counts (all windows).</summary>
    public static bool ShowPerfHud { get; set; }

    /// <summary>Tunable kitty image GPU texture cache size limit (shared across all windows).</summary>
    public static int KittyCacheLimit
    {
        get => ImageTextureCache.MaxTextures;
        set => ImageTextureCache.MaxTextures = value;
    }

    // Perf-HUD throughput tracking (bytes consumed per second, ~500ms window).
    private long _hudAccumBytes;
    private long _hudWindowStartTs;
    private double _hudBytesPerSec;

    /// <summary>
    /// Debug overlay (top-right of the grid): frame-build vs ImGui-submit cost,
    /// dirty state, PTY throughput, and draw-call counts from the renderer.
    /// </summary>
    private void DrawPerfHud(
        TerminalSession session,
        float2 canvasPos,
        float2 avail,
        int cols,
        int rows,
        double buildMs,
        double submitMs,
        GridRenderStats stats,
        int imagePlacements,
        int imageTextures,
        int newImages,
        long vramBytes)
    {
        var frameStats = (session.Surface as GhosttyTerminalSurface)?.LastFrameStats ?? default;

        _hudAccumBytes += frameStats.BytesConsumed;
        if (_hudWindowStartTs == 0)
        {
            _hudWindowStartTs = Stopwatch.GetTimestamp();
        }

        var elapsed = Stopwatch.GetElapsedTime(_hudWindowStartTs);
        if (elapsed.TotalMilliseconds >= 500)
        {
            _hudBytesPerSec = _hudAccumBytes / elapsed.TotalSeconds;
            _hudAccumBytes = 0;
            _hudWindowStartTs = Stopwatch.GetTimestamp();
        }

        string state = frameStats.SyncPaused
            ? "sync-hold"
            : frameStats.DirtyState switch { 0 => "clean", 1 => "partial", _ => "full" };

        // ImString interpolation writes UTF-8 into the binding's per-frame
        // shared buffer — zero managed allocation. The HUD exists to observe
        // the render path; it must not perturb it with three strings per frame.
        ImString l1 = $"grid {cols}x{rows}  build {buildMs:F2}ms (vt {frameStats.WriteMs:F2} upd {frameStats.UpdateMs:F2} pop {frameStats.PopulateMs:F2})";
        bool hasDrops = frameStats.InboxDropTotal > 0;
        ImString l2 = (frameStats.KittyApcStarts >= 0, hasDrops) switch
        {
            (true,  true)  => $"submit {submitMs:F2}ms  state {state}  in {_hudBytesPerSec / 1048576.0:F2} MB/s  apc:{frameStats.KittyApcStarts}  DROP:{frameStats.InboxDropTotal}",
            (true,  false) => $"submit {submitMs:F2}ms  state {state}  in {_hudBytesPerSec / 1048576.0:F2} MB/s  apc:{frameStats.KittyApcStarts}",
            (false, true)  => $"submit {submitMs:F2}ms  state {state}  in {_hudBytesPerSec / 1048576.0:F2} MB/s  DROP:{frameStats.InboxDropTotal}",
            _              => $"submit {submitMs:F2}ms  state {state}  in {_hudBytesPerSec / 1048576.0:F2} MB/s",
        };
        ImString l3 = $"draws bg:{stats.BackgroundRects} blk:{stats.BlockRects} runs:{stats.GlyphRuns} cell:{stats.GlyphCells} deco:{stats.DecorationLines} = {stats.TotalCalls}  img:{imagePlacements}/{imageTextures}+{newImages}  vram:{vramBytes / 1048576.0:F1}MB";

        // Line 4: kitty diagnostics (only when the diag flag is on).
        // Shows storage existence, placement counts, and the first 32 bytes after
        // the first \x1b_G in the most recent VTWrite — enough to confirm whether
        // a real APC body follows the header or the sequence ends immediately.
        bool showKittyDiag = frameStats.KittyApcStarts >= 0;
        ImString l4 = default;
        if (showKittyDiag)
        {
            string storage = frameStats.KittyStorageExists ? "T" : "F";
            string sample = frameStats.KittyApcSample ?? "?";
            l4 = $"kitty: storage={storage} total={frameStats.KittyTotalPlacements} vis={imagePlacements} dec={newImages} | {sample}";
        }

        float lineH = ImGui.GetTextLineHeight();
        float w = Math.Max(ImGui.CalcTextSize(l1).X, Math.Max(ImGui.CalcTextSize(l2).X, ImGui.CalcTextSize(l3).X));
        if (showKittyDiag) w = Math.Max(w, ImGui.CalcTextSize(l4).X);
        const float pad = 6f;
        int lineCount = showKittyDiag ? 4 : 3;

        var drawList = ImGui.GetWindowDrawList();
        var p0 = new float2(canvasPos.X + avail.X - w - pad * 2 - 4f, canvasPos.Y + 4f);
        var p1 = new float2(p0.X + w + pad * 2, p0.Y + lineH * lineCount + pad * 2);
        drawList.AddRectFilled(p0, p1, 0xB0000000u);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad), 0xFF7FFF7Fu, l1);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad + lineH), 0xFF7FDFFFu, l2);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad + lineH * 2), 0xFFFFBF7Fu, l3);
        if (showKittyDiag)
            drawList.AddText(new float2(p0.X + pad, p0.Y + pad + lineH * 3), 0xFFFF9FFFu, l4);
    }
}
