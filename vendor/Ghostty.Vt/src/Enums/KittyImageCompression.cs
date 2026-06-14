namespace Ghostty.Vt.Enums;

/// <summary>
/// Compression applied to a stored kitty graphics image payload. Mirrors
/// libghostty's <c>kitty.graphics_command.Transmission.Compression</c> (0-based
/// C enum). <see cref="ZlibDeflate"/> payloads must be inflated before decode.
/// </summary>
public enum KittyImageCompression
{
    None = 0,
    ZlibDeflate = 1,
}
