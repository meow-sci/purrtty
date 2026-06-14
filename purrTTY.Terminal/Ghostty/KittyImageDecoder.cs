using System.IO.Compression;
using Ghostty.Vt.Enums;
using StbImageSharp;

namespace PurrTTY.Terminal.Ghostty;

/// <summary>
/// Decodes a kitty-graphics image payload (as handed to us by libghostty — possibly
/// zlib-compressed, and PNG/raw per the transmitted format) into tightly-packed
/// RGBA8888. libghostty stores the bytes verbatim and does not decode them, so this
/// is renderer-neutral CPU work in the backend; the frontend only uploads + draws.
/// Returns null on anything unusable (corrupt/unsupported/oversize) so the caller
/// drops the image rather than crashing.
/// </summary>
internal static class KittyImageDecoder
{
    // Bound decoded memory so a hostile/huge transmit cannot exhaust RAM.
    // 4096x4096 RGBA = 64 MiB — generous for terminal image previews.
    internal const long MaxPixels = 4096L * 4096L;

    public static DecodedImage? Decode(
        KittyImageFormat format,
        KittyImageCompression compression,
        byte[] data,
        int width,
        int height)
    {
        try
        {
            var bytes = compression == KittyImageCompression.ZlibDeflate ? Inflate(data) : data;
            if (bytes is null)
            {
                return null;
            }

            return format switch
            {
                KittyImageFormat.Png => DecodePng(bytes),
                KittyImageFormat.Rgba => FromRaw(bytes, width, height, 4),
                KittyImageFormat.Rgb => FromRaw(bytes, width, height, 3),
                KittyImageFormat.GrayAlpha => FromRaw(bytes, width, height, 2),
                KittyImageFormat.Gray => FromRaw(bytes, width, height, 1),
                _ => null,
            };
        }
        catch
        {
            // Corrupt payload, bad zlib stream, decoder throw — drop the image.
            return null;
        }
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static DecodedImage? DecodePng(byte[] bytes)
    {
        // StbImageSharp handles PNG (and, as a bonus, JPEG); force 4-channel output.
        var result = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        if (result.Width <= 0 || result.Height <= 0)
        {
            return null;
        }
        if ((long)result.Width * result.Height > MaxPixels)
        {
            return null;
        }
        return new DecodedImage(result.Width, result.Height, result.Data);
    }

    private static DecodedImage? FromRaw(byte[] bytes, int width, int height, int channels)
    {
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
        {
            return null;
        }

        long pixels = (long)width * height;
        if (bytes.Length < pixels * channels)
        {
            return null;
        }

        var rgba = new byte[width * height * 4];
        if (channels == 4)
        {
            Array.Copy(bytes, rgba, rgba.Length); // payload may be padded past w*h*4
            return new DecodedImage(width, height, rgba);
        }

        int src = 0, dst = 0;
        for (long i = 0; i < pixels; i++)
        {
            byte r, g, b, a;
            switch (channels)
            {
                case 3: r = bytes[src]; g = bytes[src + 1]; b = bytes[src + 2]; a = 255; src += 3; break;
                case 2: r = g = b = bytes[src]; a = bytes[src + 1]; src += 2; break;
                default: r = g = b = bytes[src]; a = 255; src += 1; break; // gray
            }
            rgba[dst++] = r;
            rgba[dst++] = g;
            rgba[dst++] = b;
            rgba[dst++] = a;
        }
        return new DecodedImage(width, height, rgba);
    }
}

internal readonly struct DecodedImage
{
    public readonly int Width;
    public readonly int Height;
    public readonly byte[] Rgba;

    public DecodedImage(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }
}
