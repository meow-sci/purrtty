using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.Core.Types;

namespace caTTY.Display.Rendering;

/// <summary>
///     Abstracts the target for terminal rendering operations.
///     Allows rendering to be either executed immediately (ImGui) or recorded for caching.
/// </summary>
public interface ITerminalDrawTarget
{
    /// <summary>
    ///     Draws a filled rectangle (usually for cell backgrounds).
    /// </summary>
    void AddRectFilled(float2 pMin, float2 pMax, uint col);

    /// <summary>
    ///     Draws text at the specified position.
    ///     Note: The font must be handled by the implementation (pushed/popped or stored).
    /// </summary>
    void AddText(float2 pos, uint col, string text, ImFontPtr font, float fontSize);

    /// <summary>
    ///     Draws a line decoration (underline or strikethrough).
    /// </summary>
    void DrawLine(float2 p1, float2 p2, uint col, float thickness);
    
    /// <summary>
    ///     Draws a curly underline decoration.
    /// </summary>
    void DrawCurlyUnderline(float2 pos, float4 color, float thickness, float width, float height);

    /// <summary>
    ///     Draws a dotted underline decoration.
    /// </summary>
    void DrawDottedUnderline(float2 pos, float4 color, float thickness, float width, float height);

    /// <summary>
    ///     Draws a dashed underline decoration.
    /// </summary>
    void DrawDashedUnderline(float2 pos, float4 color, float thickness, float width, float height);
}
