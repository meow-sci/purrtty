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
        GridRenderStats stats)
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
        ImString l2 = $"submit {submitMs:F2}ms  state {state}  in {_hudBytesPerSec / 1048576.0:F2} MB/s";
        ImString l3 = $"draws bg:{stats.BackgroundRects} blk:{stats.BlockRects} runs:{stats.GlyphRuns} cell:{stats.GlyphCells} deco:{stats.DecorationLines} = {stats.TotalCalls}";

        float lineH = ImGui.GetTextLineHeight();
        float w = Math.Max(ImGui.CalcTextSize(l1).X, Math.Max(ImGui.CalcTextSize(l2).X, ImGui.CalcTextSize(l3).X));
        const float pad = 6f;

        var drawList = ImGui.GetWindowDrawList();
        var p0 = new float2(canvasPos.X + avail.X - w - pad * 2 - 4f, canvasPos.Y + 4f);
        var p1 = new float2(p0.X + w + pad * 2, p0.Y + lineH * 3 + pad * 2);
        drawList.AddRectFilled(p0, p1, 0xB0000000u);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad), 0xFF7FFF7Fu, l1);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad + lineH), 0xFF7FDFFFu, l2);
        drawList.AddText(new float2(p0.X + pad, p0.Y + pad + lineH * 2), 0xFFFFBF7Fu, l3);
    }
}
