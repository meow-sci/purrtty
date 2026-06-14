namespace PurrTTY.Terminal.Rendering;

/// <summary>
/// A decoded image (tightly-packed 32-bpp RGBA) the frontend has not uploaded yet.
/// The backend decodes kitty-graphics payloads (zlib inflate + PNG/raw) into this
/// renderer-neutral form so the frontend only has to upload + draw — no engine or
/// codec types cross the seam. Emitted once, the first frame an image id is seen
/// (or when its content changes, signalled by a new <see cref="ContentVersion"/>).
/// </summary>
public sealed class TerminalImage
{
    /// <summary>Source image id; matches <see cref="ImagePlacement.ImageId"/>.</summary>
    public int ImageId { get; init; }

    /// <summary>Bumps when the bytes for this id change, so a cached texture is invalidated.</summary>
    public long ContentVersion { get; init; }

    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Tightly packed RGBA8888, length == Width * Height * 4.</summary>
    public byte[] Rgba { get; init; } = [];
}
