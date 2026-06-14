namespace Ghostty.Vt.Enums;

/// <summary>
/// Z-layer filter applied when enumerating kitty graphics placements. Mirrors
/// libghostty's <c>kitty_graphics.PlacementLayer</c>. The split points are
/// z &lt; INT32_MIN/2 (below background), INT32_MIN/2 ≤ z &lt; 0 (below text),
/// and z ≥ 0 (above text).
/// </summary>
public enum KittyPlacementLayer
{
    All = 0,
    BelowBackground = 1,
    BelowText = 2,
    AboveText = 3,
}
