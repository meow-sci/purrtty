using System.Runtime.InteropServices;
using Ghostty.Vt.Enums;
using Ghostty.Vt.Internals;
using Ghostty.Vt.Native;
using Ghostty.Vt.Types;

namespace Ghostty.Vt;

public sealed unsafe partial class Terminal : IDisposable
{
    private readonly TerminalSafeHandle _handle;
    private readonly TerminalOptions? _options;

    // purrtty fix: persistent pins for the Enquiry/Xtversion return-value
    // callbacks. The native side reads the returned GhosttyString after the
    // managed callback returns, so the backing array must outlive the callback
    // (see RegisterCallbacks); upstream returned a pin freed inside the callback.
    private GCHandle _enquiryReplyPin;
    private GCHandle _xtversionReplyPin;

    private static GhosttyStringNative PinReply(ref GCHandle pin, byte[]? bytes)
    {
        if (pin.IsAllocated)
            pin.Free();
        if (bytes is null || bytes.Length == 0)
        {
            pin = default;
            return new GhosttyStringNative { Ptr = nint.Zero, Len = 0 };
        }
        pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        return new GhosttyStringNative
        {
            Ptr = pin.AddrOfPinnedObject(),
            Len = (nuint)bytes.Length,
        };
    }

    public Terminal(int cols, int rows, Action<TerminalOptions>? configure = null)
    {
        if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));

        var options = new TerminalOptions();
        configure?.Invoke(options);
        _options = options;

        var nativeOpts = options.BuildNativeOptions(cols, rows);
        nint handle = nint.Zero;
        var result = NativeMethods.ghostty_terminal_new(
            nint.Zero, // default allocator
            &handle,
            nativeOpts);
        if (result != 0 || handle == nint.Zero)
            throw new GhosttyException($"Failed to create terminal (result={result})");

        _handle = new TerminalSafeHandle(handle);

        // Register callbacks via ghostty_terminal_set
        RegisterCallbacks(options, handle);
    }

    private unsafe void RegisterCallbacks(TerminalOptions options, nint handle)
    {
        // Ord 1: WritePty — void (terminal, userdata, const uint8_t* data, size_t len)
        if (options.OnWritePty is not null)
        {
            var del = new GhosttyTerminalWritePtyFn((_, _, data, len) =>
            {
                var span = new ReadOnlySpan<byte>(data, (int)len);
                options.OnWritePty(span);
            });
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 1, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

        // Ord 2: Bell — void (terminal, userdata)
        if (options.OnBell is not null)
        {
            var del = new GhosttyTerminalNotifyFn((_, _) => options.OnBell());
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 2, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

        // Ord 3: Enquiry — GhosttyString (terminal, userdata)
        // Managed code must RETURN the ENQ response bytes. The native trampoline
        // reads the returned (ptr,len) AFTER this callback returns, so the buffer
        // must stay pinned past the return — freeing it inside the callback (the
        // gotcha-3 use-after-scope class) would dangle the read. We keep a single
        // persistent pin per reply slot, replaced on the next call and released in
        // Dispose.
        if (options.OnEnquiry is not null)
        {
            var del = new GhosttyTerminalStringFn((_, _) =>
                PinReply(ref _enquiryReplyPin, options.OnEnquiry()));
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 3, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

        // Ord 4: Xtversion — GhosttyString (terminal, userdata)
        // Managed code must RETURN the version string bytes. Same pin lifetime as
        // Enquiry above.
        if (options.OnXtversion is not null)
        {
            var del = new GhosttyTerminalStringFn((_, _) =>
                PinReply(ref _xtversionReplyPin, options.OnXtversion()));
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 4, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

        // Ord 5: TitleChanged — void (terminal, userdata)
        if (options.OnTitleChanged is not null)
        {
            var del = new GhosttyTerminalNotifyFn((_, _) => options.OnTitleChanged());
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 5, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

        // Ord 6: Size — bool (terminal, userdata, GhosttySizeReportSize* out_size)
        // Managed code fills the out pointer and returns true/false.
        if (options.OnSize is not null)
        {
            var del = new GhosttyTerminalSizeFn((_, _, outSize) =>
            {
                var size = options.OnSize();
                if (size == null)
                    return (byte)0; // false — ignore query
                *outSize = new GhosttySizeReportSizeNative
                {
                    Rows = size.Value.Rows,
                    Columns = size.Value.Cols,
                    CellWidth = size.Value.CellWidth,
                    CellHeight = size.Value.CellHeight,
                };
                return (byte)1; // true — handled
            });
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 6, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

        // Ord 7: ColorScheme — bool (terminal, userdata, GhosttyColorScheme* out_scheme)
        // GhosttyColorScheme is an int enum: Light=0, Dark=1
        if (options.OnColorScheme is not null)
        {
            var del = new GhosttyTerminalColorSchemeFn((_, _, outScheme) =>
            {
                var scheme = options.OnColorScheme();
                if (scheme == null)
                    return (byte)0; // false — ignore query
                *outScheme = (int)scheme.Value;
                return (byte)1; // true — handled
            });
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 7, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

        // Ord 8: DeviceAttributes — bool (terminal, userdata, GhosttyDeviceAttributes* out_attrs)
        // Managed code fills the complex struct and returns true/false.
        if (options.OnDeviceAttributes is not null)
        {
            var del = new GhosttyTerminalDeviceAttributesFn((_, _, outAttrs) =>
            {
                var attrs = options.OnDeviceAttributes();
                if (attrs == null)
                    return (byte)0; // false — ignore query

                var primary = new GhosttyDeviceAttributesPrimaryNative
                {
                    ConformanceLevel = attrs.ConformanceLevel,
                    NumFeatures = (nuint)Math.Min(attrs.Features.Length, 64),
                };
                // Copy features into the fixed-size native array
                for (int i = 0; i < Math.Min(attrs.Features.Length, 64); i++)
                    primary.Features[i] = attrs.Features[i];

                *outAttrs = new GhosttyDeviceAttributesNative
                {
                    Primary = primary,
                    Secondary = new GhosttyDeviceAttributesSecondaryNative
                    {
                        DeviceType = attrs.DeviceType,
                        FirmwareVersion = attrs.FirmwareVersion,
                        RomCartridge = attrs.RomCartridge,
                    },
                    Tertiary = new GhosttyDeviceAttributesTertiaryNative
                    {
                        UnitId = attrs.UnitId,
                    },
                };
                return (byte)1; // true — handled
            });
            options.Pinner.Pin(del);
            NativeMethods.ghostty_terminal_set(handle, 8, (void*)Marshal.GetFunctionPointerForDelegate(del));
        }

    }

    // --- VT Input ---
    public unsafe void VTWrite(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        fixed (byte* ptr = data)
        {
            NativeMethods.ghostty_terminal_vt_write(
                _handle.DangerousGetHandle(), ptr, (nuint)data.Length);
        }
    }

    public void VTWrite(byte[] data) => VTWrite(data.AsSpan());

    public void VTWrite(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        VTWrite(bytes);
    }

    // --- State queries (typed properties) ---
    public int Cols => QueryInt(TerminalData.Cols);
    public int Rows => QueryInt(TerminalData.Rows);
    public int CursorX => QueryInt(TerminalData.CursorX);
    public int CursorY => QueryInt(TerminalData.CursorY);
    public bool CursorPendingWrap => QueryInt(TerminalData.CursorPendingWrap) != 0;
    public bool CursorVisible => QueryInt(TerminalData.CursorVisible) != 0;
    public TerminalScreen ActiveScreen => (TerminalScreen)QueryInt(TerminalData.ActiveScreen);
    public int TotalRows => QueryInt(TerminalData.TotalRows);
    public int ScrollbackRows => QueryInt(TerminalData.ScrollbackRows);
    public int WidthPx => QueryInt(TerminalData.WidthPx);
    public int HeightPx => QueryInt(TerminalData.HeightPx);
    public bool MouseTracking => QueryInt(TerminalData.MouseTracking) != 0;

    public unsafe Types.Scrollbar Scrollbar
    {
        get
        {
            var native = QueryScrollbarNative();
            return new Types.Scrollbar
            {
                Offset = (int)native.Offset,
                ViewportHeight = (int)native.Len,
                ScrollbackHeight = (int)(native.Total - native.Len),
                Progress = native.Total > 0 ? (float)native.Offset / native.Total : 0f,
            };
        }
    }

    public string? Title
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
            return QueryString(TerminalData.Title);
        }
    }

    public unsafe void SetForegroundColor(ColorRgb? c)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        if (c == null)
        {
            NativeMethods.ghostty_terminal_set(
                _handle.DangerousGetHandle(), 11 /* OPT_COLOR_FOREGROUND */, null);
            return;
        }
        var native = new GhosttyColorRgbNative { R = c.Value.R, G = c.Value.G, B = c.Value.B };
        NativeMethods.ghostty_terminal_set(
            _handle.DangerousGetHandle(), 11 /* OPT_COLOR_FOREGROUND */, &native);
    }

    public unsafe void SetBackgroundColor(ColorRgb? c)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        if (c == null)
        {
            NativeMethods.ghostty_terminal_set(
                _handle.DangerousGetHandle(), 12 /* OPT_COLOR_BACKGROUND */, null);
            return;
        }
        var native = new GhosttyColorRgbNative { R = c.Value.R, G = c.Value.G, B = c.Value.B };
        NativeMethods.ghostty_terminal_set(
            _handle.DangerousGetHandle(), 12 /* OPT_COLOR_BACKGROUND */, &native);
    }

    public unsafe void SetCursorColor(ColorRgb? c)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        if (c == null)
        {
            NativeMethods.ghostty_terminal_set(
                _handle.DangerousGetHandle(), 13 /* OPT_COLOR_CURSOR */, null);
            return;
        }
        var native = new GhosttyColorRgbNative { R = c.Value.R, G = c.Value.G, B = c.Value.B };
        NativeMethods.ghostty_terminal_set(
            _handle.DangerousGetHandle(), 13 /* OPT_COLOR_CURSOR */, &native);
    }

    public unsafe void SetColorPalette(ColorRgb[]? palette)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        if (palette == null)
        {
            NativeMethods.ghostty_terminal_set(
                _handle.DangerousGetHandle(), 14 /* OPT_COLOR_PALETTE */, null);
            return;
        }
        // Convert to array of GhosttyColorRgbNative (up to 256 entries)
        var native = new GhosttyColorRgbNative[256];
        for (int i = 0; i < Math.Min(palette.Length, 256); i++)
            native[i] = new GhosttyColorRgbNative { R = palette[i].R, G = palette[i].G, B = palette[i].B };
        fixed (GhosttyColorRgbNative* ptr = native)
        {
            NativeMethods.ghostty_terminal_set(
                _handle.DangerousGetHandle(), 14 /* OPT_COLOR_PALETTE */, ptr);
        }
    }

    // --- Operations ---
    public void Resize(int cols, int rows, int cellWidthPx = 0, int cellHeightPx = 0)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        // A swallowed failure here silently desyncs the engine grid from the PTY
        // winsize; surface it instead.
        var result = NativeMethods.ghostty_terminal_resize(
            _handle.DangerousGetHandle(),
            (ushort)cols, (ushort)rows,
            (uint)cellWidthPx, (uint)cellHeightPx);
        GhosttyException.ThrowIfFailure(result);
    }

    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        NativeMethods.ghostty_terminal_reset(_handle.DangerousGetHandle());
    }

    public bool ModeGet(TerminalMode mode)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        byte value = 0;
        NativeMethods.ghostty_terminal_mode_get(
            _handle.DangerousGetHandle(), (uint)mode, &value);
        return value != 0;
    }

    public void ScrollViewportToTop()
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        var behavior = new GhosttyTerminalScrollViewportNative { Tag = 0 }; // GHOSTTY_SCROLL_VIEWPORT_TOP
        NativeMethods.ghostty_terminal_scroll_viewport(_handle.DangerousGetHandle(), behavior);
    }

    public void ScrollViewportToBottom()
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        var behavior = new GhosttyTerminalScrollViewportNative { Tag = 1 }; // GHOSTTY_SCROLL_VIEWPORT_BOTTOM
        NativeMethods.ghostty_terminal_scroll_viewport(_handle.DangerousGetHandle(), behavior);
    }

    public void ScrollViewportBy(int delta)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        var behavior = new GhosttyTerminalScrollViewportNative
        {
            Tag = 2, // GHOSTTY_SCROLL_VIEWPORT_DELTA
            Delta = (nint)delta,
        };
        NativeMethods.ghostty_terminal_scroll_viewport(_handle.DangerousGetHandle(), behavior);
    }

    // --- Grid access ---
    public unsafe GridRef GetGridRef(Point point)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        var nativePoint = new GhosttyPointNative { Tag = point.NativeTag, X = point.NativeX, Y = point.NativeY };
        // Sized struct: must initialize size before calling
        var gridRef = new GhosttyGridRefNative { Size = (nuint)sizeof(GhosttyGridRefNative) };
        NativeMethods.ghostty_terminal_grid_ref(
            _handle.DangerousGetHandle(), nativePoint, &gridRef);
        return new GridRef(gridRef, this);
    }

    // --- Internal ---
    internal nint NativeHandle => _handle.DangerousGetHandle();

    private unsafe int QueryInt(TerminalData data)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        // Allocate 8 bytes to safely receive any scalar type:
        // uint16_t, bool, int enum, size_t, uint32_t. The explicit `= 0` is
        // load-bearing: the native side writes only sizeof(actual type) bytes,
        // and the upper bytes must be zero for the (int) narrowing to be
        // correct on every key (do not rely on implicit .locals init).
        long value = 0;
        NativeMethods.ghostty_terminal_get(
            _handle.DangerousGetHandle(), (int)data, &value);
        return (int)value;
    }

    private unsafe string? QueryString(TerminalData data)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        GhosttyStringNative gs;
        NativeMethods.ghostty_terminal_get(
            _handle.DangerousGetHandle(), (int)data, &gs);
        if (gs.Ptr == 0 || gs.Len == 0) return null;
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(gs.Ptr, (int)gs.Len);
    }

    private unsafe GhosttyScrollbarNative QueryScrollbarNative()
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        GhosttyScrollbarNative sb;
        NativeMethods.ghostty_terminal_get(
            _handle.DangerousGetHandle(), (int)TerminalData.Scrollbar, &sb);
        return sb;
    }

    public void Dispose()
    {
        _handle.Dispose();
        _options?.Pinner.Dispose();
        if (_enquiryReplyPin.IsAllocated)
            _enquiryReplyPin.Free();
        if (_xtversionReplyPin.IsAllocated)
            _xtversionReplyPin.Free();
    }

    // Nested SafeHandle
    private sealed class TerminalSafeHandle : GhosttySafeHandle
    {
        public TerminalSafeHandle(nint handle) { SetHandle(handle); }
        protected override void Free(nint handle) => NativeMethods.ghostty_terminal_free(handle);
        public new nint DangerousGetHandle() => handle;
    }

    // Callback delegate types matching native signatures

    // void (terminal, userdata, const uint8_t* data, size_t len) — WritePty
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void GhosttyTerminalWritePtyFn(nint terminal, void* userdata, byte* data, nuint len);

    // void (terminal, userdata) — Bell, TitleChanged
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate void GhosttyTerminalNotifyFn(nint terminal, void* userdata);

    // GhosttyStringNative (terminal, userdata) — Enquiry, Xtversion (return-value callbacks)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate GhosttyStringNative GhosttyTerminalStringFn(nint terminal, void* userdata);

    // bool (terminal, userdata, GhosttySizeReportSizeNative*) — Size
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate byte GhosttyTerminalSizeFn(nint terminal, void* userdata, GhosttySizeReportSizeNative* outSize);

    // bool (terminal, userdata, int*) — ColorScheme (it's just an int enum)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate byte GhosttyTerminalColorSchemeFn(nint terminal, void* userdata, int* outScheme);

    // bool (terminal, userdata, GhosttyDeviceAttributesNative*) — DeviceAttributes
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private unsafe delegate byte GhosttyTerminalDeviceAttributesFn(nint terminal, void* userdata, GhosttyDeviceAttributesNative* outAttrs);
}

// GhosttyScrollbar: { uint64_t total, uint64_t offset, uint64_t len }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyScrollbarNative
{
    public ulong Total;
    public ulong Offset;
    public ulong Len;
}
