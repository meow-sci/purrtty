using System.Buffers.Binary;
using System.IO.Compression;

namespace ImGuiPlayground;

/// <summary>Owned, top-to-bottom RGBA8 framebuffer pixels. Dimensions are physical pixels, not window points.</summary>
public sealed class CapturedFrame
{
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Rgba { get; }

    internal CapturedFrame(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("RGBA buffer size does not match its dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        Rgba = pixels;
    }

    /// <summary>Writes a lossless RGBA PNG using only .NET APIs; no desktop-capture permissions are needed.</summary>
    public void SavePng(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary)) WritePng(file);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void WritePng(Stream output)
    {
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], Height);
        header[8] = 8; // Eight bits per channel.
        header[9] = 6; // RGBA, non-interlaced.
        WriteChunk(output, "IHDR"u8, header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            int stride = checked(Width * 4);
            for (int y = 0; y < Height; ++y)
            {
                zlib.WriteByte(0); // PNG filter: None.
                zlib.Write(Rgba.Span.Slice(y * stride, stride));
            }
        }
        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, checked((int)compressed.Length)));
        WriteChunk(output, "IEND"u8, []);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        output.Write(number);
        output.Write(type);
        output.Write(data);
        uint crc = UpdateCrc(UpdateCrc(uint.MaxValue, type), data) ^ uint.MaxValue;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        output.Write(number);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; ++bit)
                crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        return crc;
    }
}
