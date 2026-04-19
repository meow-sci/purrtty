using Brutal.Numerics;
using caTTY.Core.Terminal;
using caTTY.Display.Types;

namespace caTTY.Display.Rendering;

/// <summary>
///     Direct rendering strategy that immediately renders terminal content without caching.
///     Best for scenarios where content changes frequently or caching is disabled.
/// </summary>
internal class DirectRenderStrategy : ITerminalRenderStrategy
{
    private readonly TerminalGridRenderer _gridRenderer;

    public DirectRenderStrategy(TerminalGridRenderer gridRenderer)
    {
        _gridRenderer = gridRenderer ?? throw new System.ArgumentNullException(nameof(gridRenderer));
    }

    public void RenderGrid(
        TerminalSession session,
        float2 drawPos,
        float charWidth,
        float lineHeight,
        TextSelection selection,
        RenderContext context)
    {
        // Simple direct rendering - no caching involved
        _gridRenderer.Render(session, context.DirectTarget, drawPos, charWidth, lineHeight, selection);
    }

    public void InvalidateCache()
    {
        // No-op for direct rendering (no cache to invalidate)
    }
}
