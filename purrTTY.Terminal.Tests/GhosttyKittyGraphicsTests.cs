using System.Text;
using NUnit.Framework;
using PurrTTY.Terminal.Ghostty;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Surface-level integration tests for kitty graphics: feed APC graphics commands
/// through <see cref="GhosttyTerminalSurface"/> and assert the renderer-neutral
/// frame carries the placements + decoded RGBA the frontend needs.
/// </summary>
[TestFixture]
public sealed class GhosttyKittyGraphicsTests
{
    // 1x2 RGB image, displayed as 10 cols x 1 row at the cursor (image_id=1).
    private const string TransmitAndDisplay =
        "\x1b_Ga=T,t=d,f=24,i=1,p=1,s=1,v=2,c=10,r=1;////////\x1b\\";

    private static GhosttyTerminalSurface NewSurface()
    {
        var surface = new GhosttyTerminalSurface(80, 24);
        surface.Resize(80, 24, 10, 20); // 10x20 px cells
        return surface;
    }

    private static void Write(GhosttyTerminalSurface surface, string s)
        => surface.Write(Encoding.UTF8.GetBytes(s));

    [Test]
    public void Placement_AppearsInFrameWithGeometry()
    {
        using var surface = NewSurface();
        Write(surface, TransmitAndDisplay);
        var frame = surface.BuildFrame();

        Assert.That(frame.ImagePlacements.Length, Is.EqualTo(1));
        var p = frame.ImagePlacements[0];
        Assert.That(p.ImageId, Is.EqualTo(1));
        Assert.That(p.Col, Is.EqualTo(0));
        Assert.That(p.Row, Is.EqualTo(0));
        Assert.That(p.WidthCells, Is.EqualTo(10));
        Assert.That(p.HeightCells, Is.EqualTo(1));
        Assert.That(p.PixelWidth, Is.EqualTo(100)); // 10 cols * 10px
        Assert.That(p.PixelHeight, Is.EqualTo(20)); // 1 row * 20px
    }

    [Test]
    public void NewImage_DecodedToRgba()
    {
        using var surface = NewSurface();
        Write(surface, TransmitAndDisplay);
        var frame = surface.BuildFrame();

        Assert.That(frame.NewImages.Length, Is.EqualTo(1));
        var img = frame.NewImages[0];
        Assert.That(img.ImageId, Is.EqualTo(1));
        Assert.That(img.Width, Is.EqualTo(1));
        Assert.That(img.Height, Is.EqualTo(2));
        // 1x2 RGB expands to 1*2*4 = 8 RGBA bytes; alpha filled opaque.
        Assert.That(img.Rgba.Length, Is.EqualTo(8));
        Assert.That(img.Rgba[3], Is.EqualTo(255));
        Assert.That(img.Rgba[7], Is.EqualTo(255));
    }

    [Test]
    public void Image_DecodedOnce_NotReEmittedNextFrame()
    {
        using var surface = NewSurface();
        Write(surface, TransmitAndDisplay);

        var first = surface.BuildFrame();
        Assert.That(first.NewImages.Length, Is.EqualTo(1), "image decoded on first sighting");

        var second = surface.BuildFrame();
        Assert.That(second.NewImages.Length, Is.EqualTo(0), "already-known image not re-decoded");
        Assert.That(second.ImagePlacements.Length, Is.EqualTo(1), "placement still present");
    }

    [Test]
    public void DeleteAll_RemovesPlacements()
    {
        using var surface = NewSurface();
        Write(surface, TransmitAndDisplay);
        Assert.That(surface.BuildFrame().ImagePlacements.Length, Is.EqualTo(1));

        // a=d with default deletes all visible placements.
        Write(surface, "\x1b_Ga=d\x1b\\");
        Assert.That(surface.BuildFrame().ImagePlacements.Length, Is.EqualTo(0));
    }

    [Test]
    public void NoImages_FrameHasEmptyPlacements()
    {
        using var surface = NewSurface();
        Write(surface, "hello world");
        var frame = surface.BuildFrame();

        Assert.That(frame.ImagePlacements, Is.Empty);
        Assert.That(frame.NewImages, Is.Empty);
    }
}
