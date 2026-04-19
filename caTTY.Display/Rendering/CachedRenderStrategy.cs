using Brutal.Numerics;
using caTTY.Core.Terminal;
using caTTY.Display.Types;

namespace caTTY.Display.Rendering;

/// <summary>
///     Cached rendering strategy that records terminal content to a backing store
///     and replays it on subsequent frames until content changes.
///     Provides significant performance improvement for static or slowly-changing content.
/// </summary>
internal class CachedRenderStrategy : ITerminalRenderStrategy
{
    private readonly TerminalViewportRenderCache _cache;
    private readonly TerminalGridRenderer _gridRenderer;

    public CachedRenderStrategy(
        TerminalViewportRenderCache cache,
        TerminalGridRenderer gridRenderer)
    {
        _cache = cache ?? throw new System.ArgumentNullException(nameof(cache));
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
        bool hasSelection = !selection.IsEmpty && selection.Start != selection.End;

        if (_cache.IsValid(context.RenderKey))
        {
            // Cache hit - draw cached content
            _cache.Draw(context.DrawList, drawPos);

            // Render selection overlay if needed
            if (hasSelection)
            {
                _gridRenderer.PopulateViewportCache(session);
                _gridRenderer.RenderSelectionOverlay(session, context.DirectTarget, drawPos,
                                                    charWidth, lineHeight, selection);
            }
        }
        else
        {
            // Cache miss - capture new content
            if (_cache.BeginCapture(context.RenderKey))
            {
                try
                {
                    // Render to backing store WITHOUT selection (clean state for caching)
                    var captureTarget = _cache.GetBackingStore()!.GetDrawTarget();
                    _gridRenderer.Render(session, captureTarget, drawPos, charWidth, lineHeight,
                                       default(TextSelection));

                    _cache.EndCapture();

                    // Draw the newly captured content
                    _cache.Draw(context.DrawList, drawPos);

                    // Render selection overlay on top if needed
                    if (hasSelection)
                    {
                        // Viewport cache is already populated by Render()
                        _gridRenderer.RenderSelectionOverlay(session, context.DirectTarget, drawPos,
                                                            charWidth, lineHeight, selection);
                    }
                }
                catch
                {
                    // Capture failed - fall back to direct rendering
                    _gridRenderer.Render(session, context.DirectTarget, drawPos, charWidth,
                                       lineHeight, selection);
                }
            }
            else
            {
                // Capture initialization failed - fall back to direct rendering
                _gridRenderer.Render(session, context.DirectTarget, drawPos, charWidth,
                                   lineHeight, selection);
            }
        }
    }

    public void InvalidateCache()
    {
        _cache.Invalidate();
    }
}
