namespace PurrTTY.Terminal.Rendering;

/// <summary>
/// A renderer-neutral kitty-graphics image placement for the current frame. Carries
/// only ids + geometry (no engine/GPU/ImGui types), so the frontend can resolve a
/// texture for <see cref="ImageId"/> and draw it at the given viewport cell. Geometry
/// is computed by the engine against the live terminal, so placements track scroll
/// and resize automatically — only visible placements are emitted.
/// </summary>
public readonly record struct ImagePlacement
{
    /// <summary>Identity of the source image; the frontend caches one texture per id.</summary>
    public int ImageId { get; init; }

    /// <summary>Top-left column of the placement in the viewport (0-based).</summary>
    public int Col { get; init; }

    /// <summary>Top-left row in the viewport; may be negative when scrolled partly above the top.</summary>
    public int Row { get; init; }

    /// <summary>Rendered width/height in whole cells.</summary>
    public int WidthCells { get; init; }
    public int HeightCells { get; init; }

    /// <summary>Rendered size in pixels (engine-resolved against the cell metrics).</summary>
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }

    /// <summary>Source crop within the decoded image, in pixels (the region to draw).</summary>
    public int SrcX { get; init; }
    public int SrcY { get; init; }
    public int SrcWidth { get; init; }
    public int SrcHeight { get; init; }

    /// <summary>Z-order: &lt; 0 draws below text, &gt;= 0 above text.</summary>
    public int Z { get; init; }
}
