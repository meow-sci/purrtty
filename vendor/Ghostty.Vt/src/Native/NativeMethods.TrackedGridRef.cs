using System.Runtime.InteropServices;

namespace Ghostty.Vt.Native;

// purrtty addition: tracked grid references (include/ghostty/vt/grid_ref_tracked.h
// + ghostty_terminal_grid_ref_track in terminal.h). Unlike the untracked
// GhosttyGridRef (a raw page-node snapshot valid only until the next mutating
// terminal call), a tracked reference is owned by the caller, follows its cell
// across terminal mutations, and reports "no value" when the tracked location
// is discarded (scrollback pruning, reflow, clear). Symbols verified present in
// all three vendored native builds (osx-arm64 / win-x64 / linux-x64).
internal static unsafe partial class NativeMethods
{
    [LibraryImport(LibraryName)]
    internal static partial int ghostty_terminal_grid_ref_track(
        nint terminal, GhosttyPointNative point, nint* outRef);

    [LibraryImport(LibraryName)]
    internal static partial void ghostty_tracked_grid_ref_free(nint trackedRef);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool ghostty_tracked_grid_ref_has_value(nint trackedRef);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_tracked_grid_ref_set(
        nint trackedRef, nint terminal, GhosttyPointNative point);

    [LibraryImport(LibraryName)]
    internal static partial int ghostty_tracked_grid_ref_snapshot(
        nint trackedRef, GhosttyGridRefNative* outRef);
}
