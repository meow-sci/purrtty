using System.Runtime.InteropServices;

namespace Ghostty.Vt.Native;

// purrtty addition: native imports + structs for selection and per-row selection
// ranges. These wrap symbols that exist in the pinned libghostty-vt build
// (verified via `nm`) but were not surfaced by the upstream binding.
internal static unsafe partial class NativeMethods
{
    // --- Selection derivation (snapshots; not installed until ghostty_terminal_set OPT_SELECTION=21) ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_select_word(
        nint terminal, GhosttyTerminalSelectWordOptionsNative* options, GhosttySelectionNative* outSelection);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_select_line(
        nint terminal, GhosttyTerminalSelectLineOptionsNative* options, GhosttySelectionNative* outSelection);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_select_all(
        nint terminal, GhosttySelectionNative* outSelection);

    // --- Selection formatting (one-shot; selection==NULL uses the active selection) ---

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_selection_format_alloc(
        nint terminal, nint allocator, GhosttyTerminalSelectionFormatOptionsNative options,
        byte** outPtr, nuint* outLen);
}

// Note: GhosttySelectionNative is already defined in NativeMethods.cs (used by KittyGraphics).

// GhosttyTerminalSelectWordOptions: { size_t size; GhosttyGridRef ref; const uint32_t* boundary; size_t boundary_len; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyTerminalSelectWordOptionsNative
{
    public nuint Size;
    public GhosttyGridRefNative Ref;
    public uint* BoundaryCodepoints;
    public nuint BoundaryCodepointsLen;
}

// GhosttyTerminalSelectLineOptions:
//   { size_t size; GhosttyGridRef ref; const uint32_t* whitespace; size_t whitespace_len; bool semantic_prompt_boundary; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyTerminalSelectLineOptionsNative
{
    public nuint Size;
    public GhosttyGridRefNative Ref;
    public uint* Whitespace;
    public nuint WhitespaceLen;
    public byte SemanticPromptBoundary;
}

// GhosttyTerminalSelectionFormatOptions:
//   { size_t size; GhosttyFormatterFormat emit; bool unwrap; bool trim; const GhosttySelection* selection; }
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GhosttyTerminalSelectionFormatOptionsNative
{
    public nuint Size;
    public int Emit;
    public byte Unwrap;
    public byte Trim;
    public GhosttySelectionNative* Selection;
}

// GhosttyRenderStateRowSelection: { size_t size; uint16_t start_x; uint16_t end_x; }
[StructLayout(LayoutKind.Sequential)]
internal struct GhosttyRenderStateRowSelectionNative
{
    public nuint Size;
    public ushort StartX;
    public ushort EndX;
}
