namespace Ghostty.Vt.Enums;

/// <summary>
/// Pixel/encoding format of a stored kitty graphics image. Mirrors libghostty's
/// <c>kitty.graphics_command.Transmission.Format</c> (a 0-based C enum). Only
/// Rgb/Rgba/Png are transmittable over the protocol (f=24/32/100); GrayAlpha/Gray
/// exist as PNG-decode targets. Verify against the pinned headers on a pin bump.
/// </summary>
public enum KittyImageFormat
{
    Rgb = 0,
    Rgba = 1,
    Png = 2,
    GrayAlpha = 3,
    Gray = 4,
}
