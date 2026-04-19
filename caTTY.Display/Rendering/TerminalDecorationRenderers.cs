using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace caTTY.Display.Rendering;

/// <summary>
///     Shared helper methods for rendering terminal decorations.
///     Used by both direct rendering and command buffer replay.
/// </summary>
public static class TerminalDecorationRenderers
{
    public static void RenderCurlyUnderline(ImDrawListPtr drawList, float2 pos, float4 color, float thickness, float width, float height)
    {
        float underlineY = pos.Y + height - 2;
        uint col = ImGui.ColorConvertFloat4ToU32(color);
        float curlyThickness = Math.Max(3.0f, thickness);

        // Create a wavy line using multiple bezier curve segments with much higher amplitude
        float waveHeight = 4.0f; // Much bigger amplitude for very visible waves
        float segmentWidth = width / 2.0f; // 2 wave segments per character ISH, actually extracting from original logic:
        // Original logic: float segmentWidth = currentCharacterWidth / 2.0f;
        // Here we have 'width' which is currentCharacterWidth.
        segmentWidth = width / 2.0f;

        for (int i = 0; i < 2; i++)
        {
          float startX = pos.X + (i * segmentWidth);
          float endX = pos.X + ((i + 1) * segmentWidth);

          // Alternate wave direction for each segment to create continuous wave
          float controlOffset = (i % 2 == 0) ? -waveHeight : waveHeight;

          var p1 = new float2(startX, underlineY);
          var p2 = new float2(startX + segmentWidth * 0.3f, underlineY + controlOffset);
          var p3 = new float2(startX + segmentWidth * 0.7f, underlineY - controlOffset);
          var p4 = new float2(endX, underlineY);

          drawList.AddBezierCubic(p1, p2, p3, p4, col, curlyThickness);
        }
    }

    public static void RenderDottedUnderline(ImDrawListPtr drawList, float2 pos, float4 underlineColor, float thickness, float width, float height)
    {
      float underlineY = pos.Y + height - 2;
      uint color = ImGui.ColorConvertFloat4ToU32(underlineColor);
      float dottedThickness = Math.Max(3.0f, thickness);

      float dotSize = 3.0f; // Increased dot size for better visibility
      float spacing = 3.0f; // Increased spacing for clearer separation
      float totalStep = dotSize + spacing;

      for (float x = pos.X; x < pos.X + width - dotSize; x += totalStep)
      {
        float dotEnd = Math.Min(x + dotSize, pos.X + width);
        var dotStart = new float2(x, underlineY);
        var dotEndPos = new float2(dotEnd, underlineY);
        drawList.AddLine(dotStart, dotEndPos, color, dottedThickness);
      }
    }

    public static void RenderDashedUnderline(ImDrawListPtr drawList, float2 pos, float4 underlineColor, float thickness, float width, float height)
    {
      float underlineY = pos.Y + height - 2;
      uint color = ImGui.ColorConvertFloat4ToU32(underlineColor);
      float dashedThickness = Math.Max(3.0f, thickness);

      float dashSize = 6.0f; // Increased dash length for better visibility
      float spacing = 4.0f; // Increased spacing for clearer separation
      float totalStep = dashSize + spacing;

      for (float x = pos.X; x < pos.X + width - dashSize; x += totalStep)
      {
        float dashEnd = Math.Min(x + dashSize, pos.X + width);
        var dashStart = new float2(x, underlineY);
        var dashEndPos = new float2(dashEnd, underlineY);
        drawList.AddLine(dashStart, dashEndPos, color, dashedThickness);
      }
    }
}
