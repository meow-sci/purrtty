using System.Runtime.InteropServices;
using Ghostty.Vt.Enums;
using Ghostty.Vt.Native;

namespace Ghostty.Vt;

// purrtty addition: managed wrapper over libghostty's read-only kitty graphics
// C API. The engine parses and stores images + placements from APC graphics
// commands inside Terminal.VTWrite; this surface only *reads* them so the
// frontend can composite the bitmaps. There is no command-injection API —
// images come from the shell. All access must be on the engine's tick thread
// (gotcha 1): the storage/image handles are raw pointers into engine-owned maps,
// valid only until the next mutating engine call.

/// <summary>Static metadata about a stored kitty graphics image.</summary>
public readonly struct KittyImageInfo
{
    public uint ImageId { get; init; }
    public uint Width { get; init; }
    public uint Height { get; init; }
    public KittyImageFormat Format { get; init; }
    public KittyImageCompression Compression { get; init; }
}

/// <summary>
/// Render-ready info for one placement, computed by libghostty against the live
/// terminal (cell metrics, scroll position). <see cref="ViewportVisible"/> is
/// false for placements scrolled off-screen and for virtual (Unicode-placeholder)
/// placements, which have no fixed screen position.
/// </summary>
public readonly struct KittyPlacementRenderInfo
{
    public uint ImageId { get; init; }
    public uint PlacementId { get; init; }
    public bool IsVirtual { get; init; }
    public int Z { get; init; }
    public uint PixelWidth { get; init; }
    public uint PixelHeight { get; init; }
    public uint GridCols { get; init; }
    public uint GridRows { get; init; }
    /// <summary>Top-left column of the placement in the viewport (0-based).</summary>
    public int ViewportCol { get; init; }
    /// <summary>Top-left row in the viewport; negative when scrolled partly above the top.</summary>
    public int ViewportRow { get; init; }
    public bool ViewportVisible { get; init; }
    /// <summary>Source crop rect within the (decoded) image, in pixels. 0-size means full image.</summary>
    public uint SourceX { get; init; }
    public uint SourceY { get; init; }
    public uint SourceWidth { get; init; }
    public uint SourceHeight { get; init; }
}

// Mirrors the extern struct GhosttyKittyGraphicsPlacementRenderInfo
// (kitty_graphics.zig: size,u32×4,i32×2,bool(1B),u32×4). Field order/offsets must
// match exactly; `Size` is set to sizeof before the call (native rejects undersize).
[StructLayout(LayoutKind.Sequential)]
internal struct KittyPlacementRenderInfoNative
{
    public nuint Size;
    public uint PixelWidth;
    public uint PixelHeight;
    public uint GridCols;
    public uint GridRows;
    public int ViewportCol;
    public int ViewportRow;
    public byte ViewportVisible; // Zig bool = 1 byte
    public uint SourceX;
    public uint SourceY;
    public uint SourceWidth;
    public uint SourceHeight;
}

// Native GhosttyKittyGraphicsImageData indices (kitty_graphics.zig ImageData).
internal enum KittyImageDataKey
{
    Id = 1,
    Number = 2,
    Width = 3,
    Height = 4,
    Format = 5,
    Compression = 6,
    DataPtr = 7,
    DataLen = 8,
}

// Native GhosttyKittyGraphicsPlacementData indices (the subset we read).
internal enum KittyPlacementDataKey
{
    ImageId = 1,
    PlacementId = 2,
    IsVirtual = 3,
    Z = 12,
}

/// <summary>
/// Reusable cursor over the active screen's kitty graphics placements. The
/// single-threaded surface owns one and reuses it across frames: each tick call
/// <see cref="Reset"/> once, then <see cref="MoveNext"/> + <see cref="Current"/>
/// in a loop, optionally pulling image bytes via <see cref="CopyImageData"/>.
/// </summary>
public sealed unsafe class KittyPlacementCursor : IDisposable
{
    private readonly Terminal _terminal;
    private nint _iterator;
    private nint _storage;

    public KittyPlacementCursor(Terminal terminal)
    {
        _terminal = terminal;
        nint iter;
        var result = NativeMethods.ghostty_kitty_graphics_placement_iterator_new(nint.Zero, &iter);
        GhosttyException.ThrowIfFailure(result);
        _iterator = iter;
    }

    /// <summary>
    /// Rebinds the cursor to the active screen's current placement set and resets
    /// traversal. Call once per tick before iterating. Returns false if the engine
    /// has no kitty graphics storage (built without support) — treat as "no images".
    /// </summary>
    public bool Reset(KittyPlacementLayer layer = KittyPlacementLayer.All)
    {
        ThrowIfDisposed();

        // Storage handle for the *active* screen (changes across main/alt switches),
        // so it must be re-fetched every tick.
        nint storage = nint.Zero;
        NativeMethods.ghostty_terminal_get(
            _terminal.NativeHandle, (int)TerminalData.KittyGraphics, &storage);
        if (storage == nint.Zero)
        {
            return false;
        }

        _storage = storage;

        uint layerValue = (uint)layer;
        // Iterator option 0 = layer (kitty_graphics.PlacementIteratorOption.layer).
        NativeMethods.ghostty_kitty_graphics_placement_iterator_set(_iterator, 0, &layerValue);

        // Data index 1 = placement_iterator: binds the iterator to the storage's
        // placement map and resets it (preserving the layer filter).
        nint iter = _iterator;
        var result = NativeMethods.ghostty_kitty_graphics_get(_storage, 1, &iter);
        return result == 0;
    }

    /// <summary>Advances to the next placement matching the layer filter.</summary>
    public bool MoveNext()
    {
        ThrowIfDisposed();
        return NativeMethods.ghostty_kitty_graphics_placement_next(_iterator);
    }

    /// <summary>
    /// Computed render info for the current placement (valid after <see cref="MoveNext"/>
    /// returned true), or null if the placement's backing image is gone.
    /// </summary>
    public KittyPlacementRenderInfo? Current
    {
        get
        {
            ThrowIfDisposed();

            uint imageId = 0, placementId = 0;
            byte isVirtual = 0;
            int z = 0;
            NativeMethods.ghostty_kitty_graphics_placement_get(_iterator, (int)KittyPlacementDataKey.ImageId, &imageId);
            NativeMethods.ghostty_kitty_graphics_placement_get(_iterator, (int)KittyPlacementDataKey.PlacementId, &placementId);
            NativeMethods.ghostty_kitty_graphics_placement_get(_iterator, (int)KittyPlacementDataKey.IsVirtual, &isVirtual);
            NativeMethods.ghostty_kitty_graphics_placement_get(_iterator, (int)KittyPlacementDataKey.Z, &z);

            nint image = NativeMethods.ghostty_kitty_graphics_image(_storage, imageId);
            if (image == nint.Zero)
            {
                return null;
            }

            var native = new KittyPlacementRenderInfoNative
            {
                Size = (nuint)sizeof(KittyPlacementRenderInfoNative),
            };
            var result = NativeMethods.ghostty_kitty_graphics_placement_render_info(
                _iterator, image, _terminal.NativeHandle, &native);
            if (result != 0)
            {
                return null;
            }

            return new KittyPlacementRenderInfo
            {
                ImageId = imageId,
                PlacementId = placementId,
                IsVirtual = isVirtual != 0,
                Z = z,
                PixelWidth = native.PixelWidth,
                PixelHeight = native.PixelHeight,
                GridCols = native.GridCols,
                GridRows = native.GridRows,
                ViewportCol = native.ViewportCol,
                ViewportRow = native.ViewportRow,
                ViewportVisible = native.ViewportVisible != 0,
                SourceX = native.SourceX,
                SourceY = native.SourceY,
                SourceWidth = native.SourceWidth,
                SourceHeight = native.SourceHeight,
            };
        }
    }

    /// <summary>Static info for a stored image by id (uses the last <see cref="Reset"/> storage); null if absent.</summary>
    public KittyImageInfo? GetImage(uint imageId)
    {
        ThrowIfDisposed();
        if (_storage == nint.Zero)
        {
            return null;
        }

        nint image = NativeMethods.ghostty_kitty_graphics_image(_storage, imageId);
        if (image == nint.Zero)
        {
            return null;
        }

        uint id = 0, width = 0, height = 0;
        int format = 0, compression = 0;
        NativeMethods.ghostty_kitty_graphics_image_get(image, (int)KittyImageDataKey.Id, &id);
        NativeMethods.ghostty_kitty_graphics_image_get(image, (int)KittyImageDataKey.Width, &width);
        NativeMethods.ghostty_kitty_graphics_image_get(image, (int)KittyImageDataKey.Height, &height);
        NativeMethods.ghostty_kitty_graphics_image_get(image, (int)KittyImageDataKey.Format, &format);
        NativeMethods.ghostty_kitty_graphics_image_get(image, (int)KittyImageDataKey.Compression, &compression);

        return new KittyImageInfo
        {
            ImageId = id,
            Width = width,
            Height = height,
            Format = (KittyImageFormat)format,
            Compression = (KittyImageCompression)compression,
        };
    }

    /// <summary>
    /// Copies the raw (possibly compressed/encoded) image payload for <paramref name="imageId"/>
    /// into a new array, or null if absent/empty. The engine owns the buffer only for
    /// the current tick, so the copy must happen before any further engine mutation.
    /// </summary>
    public byte[]? CopyImageData(uint imageId)
    {
        ThrowIfDisposed();
        if (_storage == nint.Zero)
        {
            return null;
        }

        nint image = NativeMethods.ghostty_kitty_graphics_image(_storage, imageId);
        if (image == nint.Zero)
        {
            return null;
        }

        nint dataPtr = nint.Zero;
        nuint dataLen = 0;
        NativeMethods.ghostty_kitty_graphics_image_get(image, (int)KittyImageDataKey.DataPtr, &dataPtr);
        NativeMethods.ghostty_kitty_graphics_image_get(image, (int)KittyImageDataKey.DataLen, &dataLen);
        if (dataPtr == nint.Zero || dataLen == 0)
        {
            return null;
        }

        var bytes = new byte[(int)dataLen];
        new ReadOnlySpan<byte>((void*)dataPtr, (int)dataLen).CopyTo(bytes);
        return bytes;
    }

    private void ThrowIfDisposed()
    {
        if (_iterator == nint.Zero)
        {
            throw new ObjectDisposedException(nameof(KittyPlacementCursor));
        }
    }

    public void Dispose()
    {
        if (_iterator != nint.Zero)
        {
            NativeMethods.ghostty_kitty_graphics_placement_iterator_free(_iterator);
            _iterator = nint.Zero;
        }
    }
}
