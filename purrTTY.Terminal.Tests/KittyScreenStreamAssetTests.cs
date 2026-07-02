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
/// Raw <c>f=32</c> units are built from the vendored ground-truth PNGs; the vendored
/// <c>.kitty</c> files themselves are <c>o=z</c> (zlib) units and are driven verbatim by the
/// zlib tests. The 7092b39-pin native memory-corrupted on any compressible <c>o=z</c> payload
/// (a zig 0.15.2 std.compress.flate bug — gotcha 34); the current native carries the purrtty
/// decompressZlib patch (ghostty branch <c>purrtty/vt-video-fixes</c>), so
/// <see cref="ZlibRealFrame_DecodesToGroundTruth"/> is the standing regression gate: it must
/// stay green on every native pin bump or <c>o=z</c> goes back on the banned list.
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
    public void DeleteFreeRetransmit_ReplacesTheImage_AndReEmits()
    {
        // The streaming pattern gatOS uses: NO delete — a=T with an existing id replaces the
        // image atomically at commit (ghostty ImageStorage.addImage frees the old data), so the
        // previous frame stays visible while the next one loads.
        var (w, h, frameA) = Png("gatos-frame-a.png");
        var (_, _, frameB) = Png("gatos-frame-b.png");

        using var surface = NewSurface();
        surface.Write(RawVideoUnit(frameA, w, h, withDelete: false));
        Assert.That(surface.BuildFrame().NewImages.Length, Is.EqualTo(1), "frame A decoded");

        surface.Write(RawVideoUnit(frameB, w, h, withDelete: false));
        var second = surface.BuildFrame();
        Assert.That(second.ImagePlacements.Length, Is.EqualTo(1));
        Assert.That(second.NewImages.Length, Is.EqualTo(1), "replace must re-emit new pixels");
        Assert.That(second.NewImages[0].Rgba, Is.EqualTo(frameB));
    }

    [Test]
    public void MidTransmission_ThePreviousFrameStaysVisible()
    {
        // THE streaming-visibility property (the reason gatOS sends no delete): a render tick
        // that lands mid-transmission must still see the previous frame's placement. With a
        // per-frame delete, ~every tick lands between the delete and the commit (the unit spans
        // several ticks at real data rates) and the video is permanently invisible.
        var (w, h, frameA) = Png("gatos-frame-a.png");
        var (_, _, frameB) = Png("gatos-frame-b.png");

        using var surface = NewSurface();
        surface.Write(RawVideoUnit(frameA, w, h, withDelete: false));
        Assert.That(surface.BuildFrame().NewImages.Length, Is.EqualTo(1), "frame A decoded");

        // Feed only the front half of frame B's unit (ends inside an m=1 chunk chain).
        var unitB = RawVideoUnit(frameB, w, h, withDelete: false);
        var half = unitB.Length / 2;
        surface.Write(unitB.AsSpan(0, half).ToArray());
        var mid = surface.BuildFrame();
        Assert.That(mid.ImagePlacements.Length, Is.EqualTo(1), "frame A must STILL be placed mid-load");
        Assert.That(mid.NewImages.Length, Is.EqualTo(0), "no new pixels yet (A unchanged)");

        surface.Write(unitB.AsSpan(half).ToArray());
        var done = surface.BuildFrame();
        Assert.That(done.NewImages.Length, Is.EqualTo(1), "commit re-emits");
        Assert.That(done.NewImages[0].Rgba, Is.EqualTo(frameB));
    }

    [Test]
    public void ZlibRealFrame_DecodesToGroundTruth()
    {
        // Historically [Explicit]: the 7092b39-pin native SEGFAULTED committing any
        // compressible o=z payload (zig 0.15.2 std flate, gotcha 34). The patched native
        // (ghostty purrtty/vt-video-fixes) must decode the real vendored o=z unit
        // pixel-exact — this is the gate for keeping zlib enabled in the gatOS stream.
        using var surface = NewSurface();
        surface.Write(Asset("gatos-frame-a.kitty")); // the real o=z unit, byte-for-byte
        var frame = surface.BuildFrame();

        var (w, h, expected) = Png("gatos-frame-a.png");
        Assert.That(frame.NewImages.Length, Is.EqualTo(1));
        Assert.That((frame.NewImages[0].Width, frame.NewImages[0].Height), Is.EqualTo((w, h)));
        Assert.That(frame.NewImages[0].Rgba, Is.EqualTo(expected));
    }

    [Test]
    [Explicit("Throughput probe, not a correctness gate: reports the VTWrite-path rate for the "
              + "raw video shape (Write + BuildFrame incl. hash + decode). Run before/after a "
              + "native pin bump to quantify the APC bulk lane (gatOS perf plan P7).")]
    public void VtWriteThroughput_RawVideoUnits_Probe()
    {
        var (w, h, frameA) = Png("gatos-frame-a.png");
        var (_, _, frameB) = Png("gatos-frame-b.png");
        using var surface = NewSurface();
        var unitA = RawVideoUnit(frameA, w, h, withDelete: false);
        var unitB = RawVideoUnit(frameB, w, h, withDelete: false);

        // Warm the native storage, the decode path, and the JIT.
        surface.Write(unitA);
        surface.BuildFrame();
        surface.Write(unitB);
        surface.BuildFrame();

        const int iterations = 200;
        long bytes = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            var unit = (i & 1) == 0 ? unitA : unitB;
            surface.Write(unit);
            surface.BuildFrame();
            bytes += unit.Length;
        }

        sw.Stop();
        var mibps = bytes / (1024.0 * 1024.0) / sw.Elapsed.TotalSeconds;
        Assert.Pass($"{iterations} units, {bytes / (1024.0 * 1024.0):F1} MiB in "
                    + $"{sw.Elapsed.TotalSeconds:F2} s = {mibps:F0} MiB/s");
    }

    [Test]
    public void ZlibVideo_KeyframeThenTransmitOnlyReplace_ReEmitsPixelExact()
    {
        // The exact post-P6 gatOS wire shape: an o=z a=T keyframe establishes the placement,
        // then o=z a=t transmit-only replaces swap the pixels under it (perf plan P0.3 + P6).
        // Real frames, real zlib, the real native decode path.
        var (w, h, frameA) = Png("gatos-frame-a.png");
        var (_, _, frameB) = Png("gatos-frame-b.png");

        using var surface = NewSurface();
        surface.Write(RawVideoUnit(frameA, w, h, withDelete: false, zlib: true));
        var first = surface.BuildFrame();
        Assert.That(first.ImagePlacements.Length, Is.EqualTo(1), "keyframe places");
        Assert.That(first.NewImages.Length, Is.EqualTo(1), "keyframe decodes");
        Assert.That(first.NewImages[0].Rgba, Is.EqualTo(frameA));

        surface.Write(RawVideoUnit(frameB, w, h, withDelete: false, zlib: true, display: false));
        var second = surface.BuildFrame();
        Assert.That(second.ImagePlacements.Length, Is.EqualTo(1), "the keyframe's placement survives a=t");
        Assert.That(second.NewImages.Length, Is.EqualTo(1), "the replace re-emits new pixels");
        Assert.That(second.NewImages[0].Rgba, Is.EqualTo(frameB));
    }

    /// <summary>
    ///     One gatOS-shaped video frame: ESC7 · ESC[H · [optional delete i=1] · chunked transmit
    ///     (optionally <c>o=z</c>) · ESC8. gatOS streams delete-free (replace-on-retransmit keeps
    ///     the previous frame visible mid-load) as an <c>a=T</c> keyframe or an <c>a=t</c>
    ///     transmit-only replace (perf plan P0.3); the delete variant covers the
    ///     terminal-doom-style pattern.
    /// </summary>
    private static byte[] RawVideoUnit(
        byte[] rgba, int w, int h, bool withDelete = true, bool zlib = false, bool display = true)
    {
        var payload = zlib ? ZlibDeflate(rgba) : rgba;
        var b64 = Convert.ToBase64String(payload);
        var sb = new StringBuilder("\x1b7\x1b[H");
        if (withDelete)
            sb.Append("\x1b_Ga=d,d=I,i=1\x1b\\");
        var offset = 0;
        var first = true;
        do
        {
            var take = Math.Min(4000, b64.Length - offset);
            var last = offset + take >= b64.Length;
            sb.Append("\x1b_G");
            // Keyframes (a=T) carry the placement keys p/C; steady-state replaces (a=t) must
            // not — mirroring the two gatOS KittyEncoder unit forms.
            sb.Append(first
                ? display
                    ? $"a=T,q=2,i=1,p=1,f=32{(zlib ? ",o=z" : "")},s={w},v={h},C=1,m={(last ? 0 : 1)}"
                    : $"a=t,q=2,i=1,f=32{(zlib ? ",o=z" : "")},s={w},v={h},m={(last ? 0 : 1)}"
                : $"m={(last ? 0 : 1)}");
            sb.Append(';').Append(b64, offset, take).Append('\x1b').Append('\\');
            offset += take;
            first = false;
        }
        while (offset < b64.Length);
        sb.Append("\x1b8");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static byte[] ZlibDeflate(byte[] data)
    {
        // The exact compressor class the gatOS encoder uses, so these tests drive the same
        // family of zlib streams the live /sim/display feed produces.
        using var output = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(
                   output, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            z.Write(data);
        return output.ToArray();
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
