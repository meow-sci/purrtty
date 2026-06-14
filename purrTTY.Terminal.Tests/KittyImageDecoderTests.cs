using System.IO.Compression;
using Ghostty.Vt.Enums;
using NUnit.Framework;
using PurrTTY.Terminal.Ghostty;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Unit tests for the renderer-neutral kitty image decoder (zlib inflate + raw
/// channel expansion). PNG decoding is delegated to StbImageSharp and exercised
/// end-to-end by the in-game path; these pin the format handling we own.
/// </summary>
[TestFixture]
public sealed class KittyImageDecoderTests
{
    [Test]
    public void Rgb_ExpandsToOpaqueRgba()
    {
        // 2x1 RGB: red, green.
        byte[] rgb = { 255, 0, 0, 0, 255, 0 };
        var decoded = KittyImageDecoder.Decode(KittyImageFormat.Rgb, KittyImageCompression.None, rgb, 2, 1);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Value.Width, Is.EqualTo(2));
        Assert.That(decoded.Value.Height, Is.EqualTo(1));
        Assert.That(decoded.Value.Rgba, Is.EqualTo(new byte[] { 255, 0, 0, 255, 0, 255, 0, 255 }));
    }

    [Test]
    public void Gray_ExpandsToOpaqueGrayRgba()
    {
        byte[] gray = { 0x40, 0x80 }; // two gray pixels
        var decoded = KittyImageDecoder.Decode(KittyImageFormat.Gray, KittyImageCompression.None, gray, 2, 1);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Value.Rgba, Is.EqualTo(new byte[] { 0x40, 0x40, 0x40, 255, 0x80, 0x80, 0x80, 255 }));
    }

    [Test]
    public void Rgba_PassesThrough()
    {
        byte[] rgba = { 1, 2, 3, 4, 5, 6, 7, 8 }; // 2x1 RGBA
        var decoded = KittyImageDecoder.Decode(KittyImageFormat.Rgba, KittyImageCompression.None, rgba, 2, 1);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Value.Rgba, Is.EqualTo(rgba));
    }

    [Test]
    public void ZlibCompressedRgb_IsInflatedThenExpanded()
    {
        byte[] rgb = { 10, 20, 30, 40, 50, 60 }; // 2x1 RGB
        byte[] compressed = ZlibCompress(rgb);

        var decoded = KittyImageDecoder.Decode(KittyImageFormat.Rgb, KittyImageCompression.ZlibDeflate, compressed, 2, 1);

        Assert.That(decoded, Is.Not.Null);
        Assert.That(decoded!.Value.Rgba, Is.EqualTo(new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 }));
    }

    [Test]
    public void Oversize_IsRejected()
    {
        var decoded = KittyImageDecoder.Decode(KittyImageFormat.Rgb, KittyImageCompression.None, new byte[12], 100_000, 100_000);
        Assert.That(decoded, Is.Null);
    }

    [Test]
    public void TruncatedPayload_IsRejected()
    {
        // Claims 4x4 RGB (48 bytes) but supplies only 3.
        var decoded = KittyImageDecoder.Decode(KittyImageFormat.Rgb, KittyImageCompression.None, new byte[] { 1, 2, 3 }, 4, 4);
        Assert.That(decoded, Is.Null);
    }

    [Test]
    public void CorruptZlib_IsRejected()
    {
        var decoded = KittyImageDecoder.Decode(KittyImageFormat.Rgb, KittyImageCompression.ZlibDeflate, new byte[] { 0xFF, 0xFF, 0xFF }, 2, 1);
        Assert.That(decoded, Is.Null);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}
