using Ghostty.Vt;
using Ghostty.Vt.Enums;
using NUnit.Framework;
using VtTerminal = Ghostty.Vt.Terminal;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Phase-0 de-risk: exercises the restored kitty graphics binding directly
/// against the real native libghostty-vt, independent of the purrtty seam.
/// Confirms placement enumeration + computed render info + image bytes work at
/// the pinned native commit. (Engine VT/graphics behavior itself is trusted —
/// these assert the C-API plumbing our renderer depends on.)
/// </summary>
[TestFixture]
public sealed class KittyGraphicsBindingTests
{
    // a=T transmit+display, t=d direct, f=24 RGB, i=1, p=1, s=1 v=2 (1x2 px),
    // c=10 r=1 (10 cols x 1 row); "////////" = 8 base64 chars = 6 bytes = 1*2*3 RGB.
    private const string TransmitAndDisplay =
        "\x1b_Ga=T,t=d,f=24,i=1,p=1,s=1,v=2,c=10,r=1;////////\x1b\\";

    [Test]
    public void Placement_EnumeratesWithComputedRenderInfo()
    {
        using var terminal = new VtTerminal(80, 24);
        terminal.Resize(80, 24, 10, 20); // 10x20 px cells → deterministic geometry
        terminal.VTWrite(TransmitAndDisplay);

        using var cursor = new KittyPlacementCursor(terminal);
        Assert.That(cursor.Reset(), Is.True, "kitty graphics storage should be available");
        Assert.That(cursor.MoveNext(), Is.True, "one placement should have been created");

        var info = cursor.Current;
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.ImageId, Is.EqualTo(1u));
        Assert.That(info.Value.PlacementId, Is.EqualTo(1u));
        Assert.That(info.Value.IsVirtual, Is.False);
        Assert.That(info.Value.ViewportVisible, Is.True);
        Assert.That(info.Value.ViewportCol, Is.EqualTo(0));
        Assert.That(info.Value.ViewportRow, Is.EqualTo(0));
        Assert.That(info.Value.GridCols, Is.EqualTo(10u));
        Assert.That(info.Value.GridRows, Is.EqualTo(1u));
        // 10 cols * 10px = 100px wide; 1 row * 20px = 20px tall.
        Assert.That(info.Value.PixelWidth, Is.EqualTo(100u));
        Assert.That(info.Value.PixelHeight, Is.EqualTo(20u));

        Assert.That(cursor.MoveNext(), Is.False, "exactly one placement expected");
    }

    [Test]
    public void Image_ExposesMetadataAndRawBytes()
    {
        using var terminal = new VtTerminal(80, 24);
        terminal.Resize(80, 24, 10, 20);
        terminal.VTWrite(TransmitAndDisplay);

        using var cursor = new KittyPlacementCursor(terminal);
        Assert.That(cursor.Reset(), Is.True);

        var image = cursor.GetImage(1);
        Assert.That(image, Is.Not.Null);
        Assert.That(image!.Value.Width, Is.EqualTo(1u));
        Assert.That(image.Value.Height, Is.EqualTo(2u));
        Assert.That(image.Value.Format, Is.EqualTo(KittyImageFormat.Rgb));
        Assert.That(image.Value.Compression, Is.EqualTo(KittyImageCompression.None));

        var bytes = cursor.CopyImageData(1);
        Assert.That(bytes, Is.Not.Null);
        Assert.That(bytes!.Length, Is.EqualTo(6), "1x2 RGB = 6 bytes");
    }

    [Test]
    public void Reset_NoImages_EnumeratesNothing()
    {
        using var terminal = new VtTerminal(80, 24);
        terminal.Resize(80, 24, 10, 20);

        using var cursor = new KittyPlacementCursor(terminal);
        Assert.That(cursor.Reset(), Is.True);
        Assert.That(cursor.MoveNext(), Is.False);
    }

    [Test]
    public void LayerFilter_AboveText_SelectsOnlyNonNegativeZ()
    {
        using var terminal = new VtTerminal(80, 24);
        terminal.Resize(80, 24, 10, 20);
        // Transmit image 1, then place it twice: z=5 (above text), z=-1 (below text).
        terminal.VTWrite("\x1b_Ga=t,t=d,f=24,i=1,s=1,v=2;////////\x1b\\");
        terminal.VTWrite("\x1b_Ga=p,i=1,p=1,z=5;\x1b\\");
        terminal.VTWrite("\x1b_Ga=p,i=1,p=2,z=-1;\x1b\\");

        using var cursor = new KittyPlacementCursor(terminal);
        Assert.That(cursor.Reset(KittyPlacementLayer.AboveText), Is.True);

        int count = 0;
        while (cursor.MoveNext())
        {
            var info = cursor.Current;
            Assert.That(info, Is.Not.Null);
            Assert.That(info!.Value.Z, Is.GreaterThanOrEqualTo(0));
            count++;
        }

        Assert.That(count, Is.EqualTo(1), "only the z=5 placement is above text");
    }
}
