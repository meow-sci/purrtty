using System.Runtime.InteropServices;

namespace Ghostty.Vt.Native;

internal static unsafe partial class NativeMethods
{
    private const string LibraryName = "ghostty-vt";

    // --- Terminal lifecycle ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_new(
        nint allocator, nint* terminal, GhosttyTerminalOptionsNative options);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_terminal_free(nint terminal);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_terminal_reset(nint terminal);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_resize(
        nint terminal, ushort cols, ushort rows, uint cell_width_px, uint cell_height_px);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_terminal_vt_write(
        nint terminal, byte* data, nuint len);

    // --- Terminal state queries ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_get(nint terminal, int data, void* @out);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_set(
        nint terminal, int option, void* value);

    // --- Terminal grid ref ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_grid_ref(
        nint terminal, GhosttyPointNative point, GhosttyGridRefNative* out_ref);

    // --- Terminal mode ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_mode_get(
        nint terminal, uint mode, byte* out_value);

    // --- Terminal scroll ---

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_terminal_scroll_viewport(
        nint terminal, GhosttyTerminalScrollViewportNative behavior);

    // --- RenderState lifecycle ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_new(
        nint allocator, nint* state);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_render_state_free(nint state);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_update(
        nint state, nint terminal);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_get(
        nint state, int data, void* @out);

    // purrtty addition: GhosttyRenderStateOption (DIRTY=0). The engine never
    // clears its dirty flags itself — the renderer must reset them after
    // consuming a frame, or Dirty reads FULL forever.
    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_set(
        nint state, int option, void* value);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_colors_get(
        nint state, void* out_colors);

    // --- RenderState row iterator ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_row_iterator_new(
        nint allocator, nint* out_iterator);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_render_state_row_iterator_free(nint iterator);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ghostty_render_state_row_iterator_next(nint iterator);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_row_get(
        nint iterator, int data, void* @out);

    // purrtty addition: GhosttyRenderStateRowOption (DIRTY=0) — clears the
    // per-row dirty flag for the row the iterator is positioned on.
    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_row_set(
        nint iterator, int option, void* value);

    // --- RenderState row cell iterator ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_row_cells_new(
        nint allocator, nint* out_cells);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_render_state_row_cells_free(nint cells);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ghostty_render_state_row_cells_next(nint cells);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_render_state_row_cells_get(
        nint cells, int data, void* @out);

    // --- Row introspection (GhosttyRow is opaque handle, query via ghostty_row_get) ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_row_get(ulong row, int data, void* @out);

    // --- Cell introspection (GhosttyCell is uint64_t, query via ghostty_cell_get) ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_cell_get(ulong cell, int data, void* @out);

    // --- Key encoder ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_key_encoder_new(
        nint allocator, nint* encoder);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_encoder_free(nint encoder);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_encoder_setopt(
        nint encoder, int option, void* value);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_encoder_setopt_from_terminal(
        nint encoder, nint terminal);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_key_encoder_encode(
        nint encoder, nint key_event, byte* out_buf, nuint out_buf_size, nuint* out_len);

    // --- Mouse encoder ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_mouse_encoder_new(
        nint allocator, nint* encoder);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_encoder_free(nint encoder);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_encoder_setopt(
        nint encoder, int option, void* value);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_encoder_setopt_from_terminal(
        nint encoder, nint terminal);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_encoder_reset(nint encoder);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_mouse_encoder_encode(
        nint encoder, nint mouse_event, byte* out_buf, nuint out_buf_size, nuint* out_len);

    // --- Mouse event ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_mouse_event_new(
        nint allocator, nint* mouse_event);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_event_free(nint mouse_event);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_event_set_action(nint mouse_event, int action);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_event_set_button(nint mouse_event, int button);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_event_clear_button(nint mouse_event);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_event_set_mods(nint mouse_event, ushort mods);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_mouse_event_set_position(nint mouse_event, GhosttyMousePositionNative position);

    // --- Key event ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_key_event_new(
        nint allocator, nint* key_event);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_event_free(nint key_event);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_event_set_action(nint key_event, int action);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_event_set_key(nint key_event, int key);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_event_set_mods(nint key_event, ushort mods);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_event_set_utf8(nint key_event, byte* utf8, nuint len);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_key_event_set_unshifted_codepoint(nint key_event, uint codepoint);

    // --- Grid ref introspection ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_grid_ref_cell(void* gridRef, void* @out);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_grid_ref_row(void* gridRef, void* @out);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_grid_ref_graphemes(
        void* gridRef, uint* buf, nuint buf_len, nuint* out_len);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_grid_ref_style(void* gridRef, void* @out);

    // --- Allocator ---
    // Only the free side is wrapped: the one native-allocated buffer purrtty
    // receives is the selection-format result (Terminal.GetSelectionText).

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_free(nint allocator, byte* ptr, nuint len);

    // --- Paste ---

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ghostty_paste_is_safe(byte* data, nuint len);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_paste_encode(
        byte* data, nuint data_len, [MarshalAs(UnmanagedType.Bool)] bool bracketed,
        byte* buf, nuint buf_len, nuint* out_written);

    // --- Kitty graphics (purrtty addition) ---
    // The storage handle comes from ghostty_terminal_get(KittyGraphics). The engine
    // parses/stores images + placements from APC graphics commands inside vt_write;
    // these read-only getters surface them. Symbols verified exported at the pinned
    // native commit (vendor README). See KittyGraphics.cs for the managed wrapper.

    [LibraryImport(LibraryName)]
    internal static partial nint ghostty_kitty_graphics_image(nint kitty, uint image_id);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_kitty_graphics_image_get(nint image, int data, void* @out);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_kitty_graphics_get(nint kitty, int data, void* @out);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_kitty_graphics_placement_iterator_new(nint allocator, nint* iter);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_kitty_graphics_placement_iterator_free(nint iter);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_kitty_graphics_placement_iterator_set(nint iter, int option, void* value);

    // Zig `bool` is a single byte — marshal as U1 (matches NativeMethods.TrackedGridRef).
    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ghostty_kitty_graphics_placement_next(nint iter);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_kitty_graphics_placement_get(nint iter, int data, void* @out);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_kitty_graphics_placement_render_info(
        nint iter, nint image, nint terminal, void* @out);
}

// Native struct matching GhosttyTerminalOptions: { uint16_t cols, uint16_t rows, size_t max_scrollback }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyTerminalOptionsNative
{
    public ushort Cols;
    public ushort Rows;
    public nuint MaxScrollback;
}

// Native struct matching GhosttyPoint: { GhosttyPointTag tag, GhosttyPointValue value }
// GhosttyPointValue is a union { GhosttyPointCoordinate coordinate, uint64_t _padding[2] } = 16 bytes
// GhosttyPointCoordinate is { uint16_t x, uint32_t y } = 8 bytes
// Total: 4 (tag) + 4 (pad) + 16 (union) = 24 bytes
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyPointNative
{
    public int Tag;
    private int _pad0;    // alignment padding for 8-byte aligned union
    public ushort X;      // inside union, at union offset 0
    private ushort _pad1; // padding after x for y's 4-byte alignment
    public uint Y;        // inside union, at union offset 4
    private ulong _pad2;  // remaining 8 bytes of 16-byte union
}

// Native struct matching GhosttyTerminalScrollViewport: { tag (int), value (union with intptr_t delta + padding) }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyTerminalScrollViewportNative
{
    public int Tag; // GhosttyTerminalScrollViewportTag
    public nint Delta; // intptr_t delta (in the union)
    public nuint _padding1; // padding for the union
}

// Native struct matching GhosttySizeReportSize: { uint16_t rows, uint16_t columns, uint32_t cell_width, uint32_t cell_height }
// Used by the Size (in-band size report) terminal callback.
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttySizeReportSizeNative
{
    public ushort Rows;
    public ushort Columns;
    public uint CellWidth;
    public uint CellHeight;
}

// Native struct matching GhosttyString: { const uint8_t* ptr, size_t len }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyStringNative
{
    public nint Ptr;
    public nuint Len;
}

// Native struct matching GhosttyGridRef: { size_t size, void* node, uint16_t x, uint16_t y }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyGridRefNative
{
    public nuint Size;
    public nint Node;
    public ushort X;
    public ushort Y;
}

// Native struct matching GhosttyColorRgb: { uint8_t r, uint8_t g, uint8_t b }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyColorRgbNative
{
    public byte R;
    public byte G;
    public byte B;
}

// purrtty addition. Native struct matching GhosttyBuffer (lib.Buffer):
// { uint8_t* ptr, size_t cap, size_t len } — used by GRAPHEMES_UTF8 reads.
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyBufferNative
{
    public nint Ptr;
    public nuint Cap;
    public nuint Len;
}

// Native struct matching GhosttySelection (sized struct, 64 bytes, align 8).
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttySelectionNative
{
    public nuint Size;
    public GhosttyGridRefNative Start;
    public GhosttyGridRefNative End;
    public byte Rectangle;
    private fixed byte _padding[7];
}

// Native struct matching GhosttyMousePosition: { float x, float y }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyMousePositionNative
{
    public float X;
    public float Y;
}

// Native struct matching GhosttyDeviceAttributesPrimary: { uint16_t conformance_level, uint16_t features[64], size_t num_features }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyDeviceAttributesPrimaryNative
{
    public ushort ConformanceLevel;
    public fixed ushort Features[64];
    public nuint NumFeatures;
}

// Native struct matching GhosttyDeviceAttributesSecondary: { uint16_t device_type, uint16_t firmware_version, uint16_t rom_cartridge }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyDeviceAttributesSecondaryNative
{
    public ushort DeviceType;
    public ushort FirmwareVersion;
    public ushort RomCartridge;
}

// Native struct matching GhosttyDeviceAttributesTertiary: { uint32_t unit_id }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyDeviceAttributesTertiaryNative
{
    public uint UnitId;
}

// Native struct matching GhosttyDeviceAttributes: { primary, secondary, tertiary }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyDeviceAttributesNative
{
    public GhosttyDeviceAttributesPrimaryNative Primary;
    public GhosttyDeviceAttributesSecondaryNative Secondary;
    public GhosttyDeviceAttributesTertiaryNative Tertiary;
}
