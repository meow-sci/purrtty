using System.Buffers.Binary;
using System.IO.Compression;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using ImGuiPlayground;

// Console checks intentionally run on Main: macOS GLFW cannot run on NUnit worker threads.
// Quiet on pass, no sleeps, hidden windows; independent of any mod test suite.
try
{
    if (args is ["--full-qualification", _, _, _, _, _, _])
    {
        if (!FullQualificationChecks.Run(args[1], args[2], args[3], args[4], args[5], args[6])) return 0;
        args = []; // Existing renderer/input checks continue synchronously below.
    }
    if (args is ["--combined-components", _, _, _])
    {
        CombinedComponentChecks.Run(args[1], args[2], args[3]);
        return 0;
    }
    if (args is ["--export-binding-contract", _, _])
    {
        ImGuiPlayground.Checks.BindingContractExporter.Run(args[1], args[2]);
        return 0;
    }
    if (args is ["--binding-contract-self-test"] or ["--binding-contract-self-test", _])
    {
        ImGuiPlayground.Checks.BindingContractExporterChecks.Run(selectedDirectory: args.Length == 2 ? args[1] : null);
        return 0;
    }
    if (args is ["--packaging"] or ["--packaging", _])
    {
        PackagingChecks.Run(args.Length == 2 ? args[1] : null);
        return 0;
    }
    if (args is ["--prototype-abi", _, _, _, _, _])
    {
        PrototypeAbiChecks.Run(args[1], args[2], args[3], args[4], args[5]);
        return 0;
    }
    if (args.Length != 0) throw new ArgumentException("Usage: ImGuiPlayground.Checks [--combined-components <profile|manual|accessor|composition> <stage-receipt.json> <report.json> | --export-binding-contract <assembly-directory> <output.json> | --binding-contract-self-test [assembly-directory] | --packaging [path/to/ImGuiPlayground.csproj] | --prototype-abi <native-library> <metadata-dir> <report-path> <target-rid> <managed-sha256>]");
    int frames = 0;
    try
    {
        PlaygroundHost.Capture(() => { if (++frames == 1) DrawPattern(); }, 200, 150);
        throw new Exception("An empty second frame incorrectly passed validation.");
    }
    catch (InvalidOperationException error) when (error.Message.Contains("without UI geometry"))
    {
        Require(frames == 2, "Expected failure on the second callback.");
    }

    var sentinel = new ApplicationException("callback-sentinel");
    try
    {
        PlaygroundHost.Capture(() => throw sentinel, 200, 150);
        throw new Exception("Callback exception was swallowed.");
    }
    catch (ApplicationException error) when (ReferenceEquals(error, sentinel)) { }

    // Sequential runs after both failure paths also prove teardown/guard recovery.
    var first = PlaygroundHost.Capture(DrawPattern, 200, 150);
    CheckPattern(first);
    CheckPng(first);
    var second = PlaygroundHost.Capture(DrawPattern, 200, 150);
    Require(first.Width == second.Width && first.Height == second.Height, "Capture dimensions changed.");
    Require(first.Rgba.Span.SequenceEqual(second.Rgba.Span), "Deterministic fixture pixels changed between runs.");

    frames = 0;
    bool sawTextureUpdate = false;
    var fonts = PlaygroundHost.Capture(() =>
    {
        ++frames;
        Require(ImGui.GetFont().GetDebugName().ToString().Contains("JetBrainsMono-Regular", StringComparison.Ordinal),
            "The bundled game font is not the active default.");
        ImGui.SetNextWindowPos(new float2(5, 5));
        ImGui.SetNextWindowSize(new float2(300, 180));
        ImGui.Begin("Dynamic font atlas"u8);
        try
        {
            ImGui.PushFont(default, 18 + frames * 6);
            try { ImGui.Text(frames == 1 ? "A"u8 : frames == 2 ? "B"u8 : "C"u8); }
            finally { ImGui.PopFont(); }
            ImGui.Button("Original BRUTAL widgets"u8);
        }
        finally { ImGui.End(); }
        foreach (var texture in ImGui.GetPlatformIO().Textures.Span)
            sawTextureUpdate |= texture.Status == ImTextureStatus.WantUpdates;
    }, 320, 200);
    Require(frames == 3 && sawTextureUpdate, "Fixture did not exercise dynamic texture updates.");
    Require(fonts.Rgba.Span.Contains((byte)255), "No bright font pixels rendered.");
    InteropChecks.CheckInput();
    FullQualificationChecks.CompleteRenderer();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static void DrawPattern()
{
    var draw = ImGui.GetBackgroundDrawList();
    InteropChecks.AddVertexResetFixture(draw);
    draw.AddRectFilled(new float2(8, 8), new float2(72, 48), new ImColor8(255, 0, 0));
    draw.AddRectFilled(new float2(8, 80), new float2(72, 120), new ImColor8(0, 0, 255));
    draw.PushClipRect(new float2(100, 10), new float2(140, 60), true);
    draw.AddRectFilled(new float2(90, 8), new float2(180, 80), new ImColor8(0, 255, 0));
    draw.PopClipRect();
    // Cross the 16-bit vertex boundary to exercise ImDrawCmd.VtxOffset.
    for (int i = 0; i < 17_000; ++i)
        draw.AddRectFilled(new float2(180, 130), new float2(190, 140), new ImColor8(255, 0, 0));
    draw.AddRectFilled(new float2(145, 100), new float2(170, 120), new ImColor8(255, 255, 0));
}

static void CheckPattern(CapturedFrame frame)
{
    Require(frame.Width > 0 && frame.Height > 0, "Empty framebuffer.");
    Require(frame.Rgba.Length == checked(frame.Width * frame.Height * 4), "Bad RGBA length.");
    Pixel(frame, 20, 20, 255, 0, 0);    // Red at top, blue below catches upside-down readback.
    Pixel(frame, 20, 100, 0, 0, 255);
    Pixel(frame, 120, 25, 0, 255, 0);   // Inside scissor.
    Pixel(frame, 95, 25, 20, 23, 31);   // Outside scissor: clear color.
    Pixel(frame, 155, 110, 255, 255, 0); // Draw after the base-vertex rollover.
}

static void Pixel(CapturedFrame frame, int x, int y, byte r, byte g, byte b)
{
    int px = x * frame.Width / 200, py = y * frame.Height / 150;
    var rgba = frame.Rgba.Span.Slice((py * frame.Width + px) * 4, 4);
    Require(Math.Abs(rgba[0] - r) <= 1 && Math.Abs(rgba[1] - g) <= 1 && Math.Abs(rgba[2] - b) <= 1 && rgba[3] == 255,
        $"Unexpected pixel at logical ({x},{y}): {rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}.");
}

static void CheckPng(CapturedFrame frame)
{
    string path = Path.Combine(Path.GetTempPath(), "imgui-check-" + Guid.NewGuid().ToString("N") + ".png");
    try
    {
        frame.SavePng(path);
        byte[] png = File.ReadAllBytes(path);
        Require(png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), "Invalid PNG signature.");
        using var idat = new MemoryStream();
        bool header = false, end = false;
        int offset = 8;
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            var type = png.AsSpan(offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            // Independent polynomial CRC check over type + data.
            uint crc = uint.MaxValue;
            foreach (byte value in png.AsSpan(offset + 4, length + 4))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; ++bit) crc = (crc & 1) == 0 ? crc / 2 : (crc / 2) ^ 0xEDB88320u;
            }
            Require(~crc == BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length, 4)), "PNG CRC mismatch.");
            if (type.SequenceEqual("IHDR"u8))
            {
                header = true;
                Require(BinaryPrimitives.ReadInt32BigEndian(data) == frame.Width && BinaryPrimitives.ReadInt32BigEndian(data[4..]) == frame.Height,
                    "PNG dimensions differ from framebuffer.");
                Require(data[8] == 8 && data[9] == 6, "PNG must be RGBA8.");
            }
            else if (type.SequenceEqual("IDAT"u8)) idat.Write(data);
            else if (type.SequenceEqual("IEND"u8)) end = true;
            offset += checked(length + 12);
        }
        Require(header && end, "Missing required PNG chunks.");
        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        byte[] row = new byte[frame.Width * 4];
        for (int y = 0; y < frame.Height; ++y)
        {
            Require(zlib.ReadByte() == 0, "Unexpected PNG filter.");
            zlib.ReadExactly(row);
            Require(row.AsSpan().SequenceEqual(frame.Rgba.Span.Slice(y * row.Length, row.Length)), "PNG pixels differ from RGBA data.");
        }
        Require(zlib.ReadByte() == -1, "Unexpected trailing PNG pixel data.");
    }
    finally { if (File.Exists(path)) File.Delete(path); }
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
