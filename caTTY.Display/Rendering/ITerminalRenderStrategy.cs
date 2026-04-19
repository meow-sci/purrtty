using Brutal.Numerics;
using caTTY.Core.Terminal;
using caTTY.Display.Types;

namespace caTTY.Display.Rendering;

/// <summary>
///     Strategy interface for different terminal rendering approaches (direct, cached, etc.).
/// </summary>
internal interface ITerminalRenderStrategy
{
    /// <summary>
    ///     Renders the terminal grid content using the strategy's specific approach.
    /// </summary>
    void RenderGrid(
        TerminalSession session,
        float2 drawPos,
        float charWidth,
        float lineHeight,
        TextSelection selection,
        RenderContext context);

    /// <summary>
    ///     Invalidates any cached content, forcing a refresh on the next render.
    /// </summary>
    void InvalidateCache();
}
