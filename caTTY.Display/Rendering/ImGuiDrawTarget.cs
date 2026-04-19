using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace caTTY.Display.Rendering;

/// <summary>
///     Direct ImGui draw target that immediately executes draw commands.
///     This is used for non-cached rendering where commands are sent directly to ImGui's draw list.
/// </summary>
public class ImGuiDrawTarget : ITerminalDrawTarget
{
    private readonly ImDrawListPtr _drawList;

    public ImGuiDrawTarget(ImDrawListPtr drawList)
    {
        _drawList = drawList;
    }

    public void AddRectFilled(float2 pMin, float2 pMax, uint col)
    {
        _drawList.AddRectFilled(pMin, pMax, col);
    }

    public void AddText(float2 pos, uint col, string text, ImFontPtr font, float fontSize)
    {
        ImGui.PushFont(font, fontSize);
        _drawList.AddText(pos, col, text);
        ImGui.PopFont();
    }

    public void DrawLine(float2 p1, float2 p2, uint col, float thickness)
    {
        _drawList.AddLine(p1, p2, col, thickness);
    }

    public void DrawCurlyUnderline(float2 pos, float4 color, float thickness, float width, float height)
    {
        TerminalDecorationRenderers.RenderCurlyUnderline(_drawList, pos, color, thickness, width, height);
    }

    public void DrawDottedUnderline(float2 pos, float4 color, float thickness, float width, float height)
    {
        TerminalDecorationRenderers.RenderDottedUnderline(_drawList, pos, color, thickness, width, height);
    }

    public void DrawDashedUnderline(float2 pos, float4 color, float thickness, float width, float height)
    {
        TerminalDecorationRenderers.RenderDashedUnderline(_drawList, pos, color, thickness, width, height);
    }
}
