using System.Text;
using NUnit.Framework;
using PurrTTY.Terminal.Ghostty;
using StbImageSharp;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// End-to-end validation of the gatOS screen-stream frames against the REAL purrTTY decode
/// path (gatOS STREAM_PLAN.md §11, tiers 4–5): real captured frames (vendored under
/// <c>Assets/</c>, see its README) are driven through a live libghostty-vt terminal in the
/// exact shape gatOS emits per video frame — <c>ESC 7</c> · <c>ESC [H</c> · delete
/// (<c>a=d,d=I,i=1</c>) · chunked transmit+display (<c>a=T,q=2,f=32,…,C=1</c>) · <c>ESC 8</c> —
/// and the renderer-neutral frame must carry the placement plus RGBA pixels exactly equal to
/// the ground-truth PNG. The producer side is already proven (the vendored pairs passed
/// gatOS's strict offline protocol validation), so failures here localize to purrTTY/libghostty.
/// </summary>
/// <remarks>
/// These tests build <b>raw f=32</b> units from the vendored ground-truth PNGs. The vendored
/// <c>.kitty</c> files themselves are <c>o=z</c> (zlib) units, and the pinned libghostty-vt
/// native <b>memory-corrupts / segfaults</b> when committing a zlib payload of compressible
/// real-world data (highly reproducible; minimal repro: zlib of 230 KB of zeros) — see
/// <see cref="ZlibRealFrame_CrashesPinnedNative_KnownBug"/>. Until the native pin is bumped
/// past a fix, <c>o=z</c> must not be sent to purrTTY, and the gatOS stream defaults to raw.
/// </remarks>
[TestFixture]
public sealed class KittyScreenStreamAssetTests
{
    private static readonly string AssetsDir =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Assets");

    private static byte[] Asset(string name) => File.ReadAllBytes(Path.Combine(AssetsDir, name));

    /// <summary>Ground-truth RGBA from a vendored PNG (decoded via StbImageSharp, forced 4-channel).</summary>
    private static (int Width, int Height, byte[] Rgba) Png(string name)
    {
        var result = ImageResult.FromMemory(Asset(name), ColorComponents.RedGreenBlueAlpha);
        return (result.Width, result.Height, result.Data);
    }

    private static GhosttyTerminalSurface NewSurface()
    {
        // 80x24 cells of 10x20 px = an 800x480 px viewport; a 320x180 px image needs
        // 32x9 cells and is fully visible at the home position.
        var surface = new GhosttyTerminalSurface(80, 24);
        surface.Resize(80, 24, 10, 20);
        return surface;
    }

    [Test]
    public void RealFramePixels_RawUnit_DecodeToGroundTruth()
    {
        var (w, h, expected) = Png("gatos-frame-a.png");
        using var surface = NewSurface();
        surface.Write(RawVideoUnit(expected, w, h));
        var frame = surface.BuildFrame();

        Assert.That(frame.ImagePlacements.Length, Is.EqualTo(1), "one visible placement");
        var p = frame.ImagePlacements[0];
        Assert.That(p.ImageId, Is.EqualTo(1));
        Assert.That((p.Col, p.Row), Is.EqualTo((0, 0)), "placed at home (ESC [H)");
        Assert.That((p.PixelWidth, p.PixelHeight), Is.EqualTo((w, h)), "1:1 pixel placement");

        Assert.That(frame.NewImages.Length, Is.EqualTo(1), "the image is decoded on first sighting");
        var img = frame.NewImages[0];
        Assert.That((img.Width, img.Height), Is.EqualTo((w, h)));
        Assert.That(img.Rgba, Is.EqualTo(expected),
            "decoded kitty pixels must equal the ground-truth PNG of the same frame");
    }

    [Test]
    public void RawVideo_DeleteRetransmitSameId_ReEmitsNewPixels()
    {
        // Two REAL consecutive-ish frames. Raw units of equal dims have IDENTICAL byte
        // length, so this is exactly the case that froze on frame 1 under the old
        // length-as-change-proxy: only a content signal re-decodes frame B.
        var (w, h, frameA) = Png("gatos-frame-a.png");
        var (_, _, frameB) = Png("gatos-frame-b.png");

        using var surface = NewSurface();
        surface.Write(RawVideoUnit(frameA, w, h));
        var first = surface.BuildFrame();
        Assert.That(first.NewImages.Length, Is.EqualTo(1), "frame A decoded");

        surface.Write(RawVideoUnit(frameB, w, h));
        var second = surface.BuildFrame();

        Assert.That(second.ImagePlacements.Length, Is.EqualTo(1), "still one placement");
        Assert.That(second.NewImages.Length, Is.EqualTo(1),
            "the equal-length delete + re-transmit must re-emit pixels (or the video freezes)");
        Assert.That(second.NewImages[0].Rgba, Is.EqualTo(frameB), "the re-emitted pixels are frame B's");
    }

    [Test]
    public void StillImage_NotReDecodedEveryTick()
    {
        var (w, h, pixels) = Png("gatos-frame-a.png");
        using var surface = NewSurface();
        surface.Write(RawVideoUnit(pixels, w, h));
        Assert.That(surface.BuildFrame().NewImages.Length, Is.EqualTo(1));

        // No new transmission: subsequent ticks must not re-emit (the content hash is
        // stable), so a still image costs no texture re-upload.
        Assert.That(surface.BuildFrame().NewImages.Length, Is.EqualTo(0));
        Assert.That(surface.BuildFrame().NewImages.Length, Is.EqualTo(0));
    }

    [Test]
    public void EqualLengthRetransmit_SmallSynthetic_StillReEmitsNewPixels()
    {
        using var surface = NewSurface();
        surface.Write(RawVideoUnit(Solid(8, 4, r: 255, g: 0, b: 0), 8, 4));
        var first = surface.BuildFrame();
        Assert.That(first.NewImages.Length, Is.EqualTo(1), "first frame decoded");
        Assert.That(first.NewImages[0].Rgba[0], Is.EqualTo(255), "first frame is red");

        surface.Write(RawVideoUnit(Solid(8, 4, r: 0, g: 0, b: 255), 8, 4));
        var second = surface.BuildFrame();

        Assert.That(second.NewImages.Length, Is.EqualTo(1),
            "same-length re-transmit must still re-emit — otherwise raw-rgba video freezes on frame 1");
        var img = second.NewImages[0];
        Assert.That((img.Rgba[0], img.Rgba[2]), Is.EqualTo(((byte)0, (byte)255)),
            "the re-emitted pixels must be the second (blue) frame");
    }

    [Test]
    [Explicit("SEGFAULTS the test host: the pinned libghostty-vt native memory-corrupts when "
              + "committing a kitty o=z payload of compressible data (minimal repro: zlib of "
              + "230 KB of zeros; nondeterministic crash point — heap corruption). Run manually "
              + "after a native pin bump; when it survives AND decodes to the ground-truth PNG, "
              + "zlib is safe to re-enable in the gatOS stream and this Explicit can be removed.")]
    public void ZlibRealFrame_CrashesPinnedNative_KnownBug()
    {
        using var surface = NewSurface();
        surface.Write(Asset("gatos-frame-a.kitty")); // the real o=z unit, byte-for-byte
        var frame = surface.BuildFrame();

        var (w, h, expected) = Png("gatos-frame-a.png");
        Assert.That(frame.NewImages.Length, Is.EqualTo(1));
        Assert.That((frame.NewImages[0].Width, frame.NewImages[0].Height), Is.EqualTo((w, h)));
        Assert.That(frame.NewImages[0].Rgba, Is.EqualTo(expected));
    }

    /// <summary>One gatOS-shaped raw video frame: ESC7 · ESC[H · delete i=1 · chunked a=T f=32 · ESC8.</summary>
    private static byte[] RawVideoUnit(byte[] rgba, int w, int h)
    {
        var b64 = Convert.ToBase64String(rgba);
        var sb = new StringBuilder("\x1b7\x1b[H\x1b_Ga=d,d=I,i=1\x1b\\");
        var offset = 0;
        var first = true;
        do
        {
            var take = Math.Min(4000, b64.Length - offset);
            var last = offset + take >= b64.Length;
            sb.Append("\x1b_G");
            sb.Append(first
                ? $"a=T,q=2,i=1,p=1,f=32,s={w},v={h},C=1,m={(last ? 0 : 1)}"
                : $"m={(last ? 0 : 1)}");
            sb.Append(';').Append(b64, offset, take).Append('\x1b').Append('\\');
            offset += take;
            first = false;
        }
        while (offset < b64.Length);
        sb.Append("\x1b8");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] Solid(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = r;
            px[i + 1] = g;
            px[i + 2] = b;
            px[i + 3] = 255;
        }

        return px;
    }
}
