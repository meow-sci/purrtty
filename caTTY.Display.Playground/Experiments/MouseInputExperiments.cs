using Brutal.ImGuiApi;
using KSA;
using ImGui = Brutal.ImGuiApi.ImGui;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Playground.Experiments;

public static class MouseInputExperiments
{
    private static float _accumWheel = 0.0f;
    private static int _scrollOffset = 0;

    public static void DrawExperiments()
    {
        ImGui.Begin("Mouse Input - Scrolling Test");

        ImGui.Text("This experiment demonstrates reading ImGui mouse wheel and hooking it to a scroll region.");
        ImGui.Separator();

        var io = ImGui.GetIO();
        ImGui.Text($"ImGui reports MousePos: {io.MousePos.X:F1}, {io.MousePos.Y:F1}");
        ImGui.Text($"ImGui reports MouseWheel: {io.MouseWheel:F3}  MouseWheelH: {io.MouseWheelH:F3}");
        ImGui.Text($"io.WantCaptureMouse: {io.WantCaptureMouse}");

        // Accumulate wheel and convert to integer scroll steps
        if (io.MouseWheel != 0.0f)
        {
            _accumWheel += io.MouseWheel;
            int steps = (int)Math.Floor(Math.Abs(_accumWheel));
            if (steps > 0)
            {
                int dir = _accumWheel > 0 ? 1 : -1;
                _scrollOffset -= dir * steps; // scroll content
                _accumWheel -= dir * steps;
            }
        }

        ImGui.Spacing();
        ImGui.Text($"Scroll offset: {_scrollOffset}");

        ImGui.Separator();
        ImGui.Text("Scrollable area (use mouse wheel while hovering):");

        // A simple scrollable area that moves lines based on _scrollOffset
        ImGui.BeginChild("scroll_area", new float2(400, 200));
        int totalLines = 50;
        for (int i = 0; i < totalLines; i++)
        {
            int displayIndex = i + _scrollOffset;
            ImGui.Text($"Line {i}: content index {displayIndex}");
        }
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.TextWrapped("Notes: ImGui will report mouse wheel values in the current frame via io.MouseWheel. If ImGui wants the mouse (io.WantCaptureMouse) then the event is consumed by UI; otherwise it can be forwarded to terminal logic.");

        ImGui.End();
    }
}
