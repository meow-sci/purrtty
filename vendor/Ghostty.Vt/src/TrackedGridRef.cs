using Ghostty.Vt.Native;
using Ghostty.Vt.Types;

namespace Ghostty.Vt;

/// <summary>
/// purrtty addition: a tracked grid reference. Unlike <see cref="GridRef"/>
/// (an untracked snapshot valid only until the next mutating terminal call),
/// a tracked reference is owned by the caller, follows its cell across normal
/// terminal mutations (output, scrolling, reflow), and reports
/// <see cref="HasValue"/> = false when the tracked location is discarded —
/// e.g. when scrollback pruning frees its page. This makes it the correct
/// anchor for state held across ticks, such as a drag-selection anchor.
///
/// Created via <see cref="Terminal.TrackGridRef"/>. Must be disposed; like the
/// rest of the engine surface it is single-threaded — dispose it on the same
/// thread that mutates the owning terminal (no finalizer on purpose).
/// </summary>
public sealed unsafe class TrackedGridRef : IDisposable
{
    private readonly Terminal _terminal;
    private nint _handle;

    internal TrackedGridRef(nint handle, Terminal terminal)
    {
        _handle = handle;
        _terminal = terminal;
    }

    /// <summary>
    /// Whether the reference still points at live content. False once the
    /// tracked location has been discarded (scrollback pruning, clear, ...).
    /// </summary>
    public bool HasValue
        => _handle != nint.Zero && NativeMethods.ghostty_tracked_grid_ref_has_value(_handle);

    /// <summary>
    /// Moves the reference to a new point on the owning terminal, clearing any
    /// prior "no value" state. Returns false on failure (the native contract
    /// leaves the reference unchanged in that case).
    /// </summary>
    public bool Set(Point point)
    {
        ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
        var nativePoint = new GhosttyPointNative { Tag = point.NativeTag, X = point.NativeX, Y = point.NativeY };
        return NativeMethods.ghostty_tracked_grid_ref_set(_handle, _terminal.NativeHandle, nativePoint) == 0;
    }

    /// <summary>
    /// Snapshots the tracked reference into an untracked <see cref="GridRef"/>
    /// (valid only until the next mutating terminal call — use it immediately).
    /// Returns false when the tracked location no longer has a value.
    /// </summary>
    public bool TrySnapshot(out GridRef gridRef)
    {
        ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
        var native = new GhosttyGridRefNative { Size = (nuint)sizeof(GhosttyGridRefNative) };
        if (NativeMethods.ghostty_tracked_grid_ref_snapshot(_handle, &native) != 0 || native.Node == nint.Zero)
        {
            gridRef = default;
            return false;
        }

        gridRef = new GridRef(native, _terminal);
        return true;
    }

    public void Dispose()
    {
        if (_handle != nint.Zero)
        {
            // Safe even after the owning terminal is freed, per the native docs.
            NativeMethods.ghostty_tracked_grid_ref_free(_handle);
            _handle = nint.Zero;
        }
    }
}
