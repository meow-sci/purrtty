using System.Runtime.InteropServices;
using Ghostty.Vt.Enums;
using Ghostty.Vt.Native;
using Ghostty.Vt.Types;

namespace Ghostty.Vt;

// purrtty additions to Terminal: default cursor style/blink and an installable
// selection API (cell / word / line / all + clipboard formatting). These wrap
// fully-supported libghostty-vt symbols. The richer gesture state machine
// (ghostty_selection_gesture_*) is intentionally not wrapped yet; the
// grid-ref + word/line/all path below covers cell-drag, double/triple-click,
// select-all, and copy without the gesture timing machinery.
public sealed unsafe partial class Terminal
{
    private const int OptSelection = 21;        // GHOSTTY_TERMINAL_OPT_SELECTION
    private const int OptDefaultCursorStyle = 22; // GHOSTTY_TERMINAL_OPT_DEFAULT_CURSOR_STYLE
    private const int OptDefaultCursorBlink = 23; // GHOSTTY_TERMINAL_OPT_DEFAULT_CURSOR_BLINK
    private const int DataSelection = 31;       // GHOSTTY_TERMINAL_DATA_SELECTION

    /// <summary>
    /// Sets the default cursor style applied on DECSCUSR reset (CSI 0 q).
    /// Pass <see langword="null"/> to revert to libghostty's built-in default
    /// (block). The native enum is BAR=0, BLOCK=1, UNDERLINE=2, BLOCK_HOLLOW=3,
    /// matching <see cref="CursorVisualStyle"/>.
    /// </summary>
    public void SetDefaultCursorStyle(CursorVisualStyle? style)
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        if (style is null)
        {
            NativeMethods.ghostty_terminal_set(NativeHandle, OptDefaultCursorStyle, null);
            return;
        }

        int value = (int)style.Value;
        NativeMethods.ghostty_terminal_set(NativeHandle, OptDefaultCursorStyle, &value);
    }

    /// <summary>
    /// Sets whether the default cursor blinks on DECSCUSR reset. Pass
    /// <see langword="null"/> to revert to the built-in default (no blink).
    /// </summary>
    public void SetDefaultCursorBlink(bool? blink)
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        if (blink is null)
        {
            NativeMethods.ghostty_terminal_set(NativeHandle, OptDefaultCursorBlink, null);
            return;
        }

        byte value = (byte)(blink.Value ? 1 : 0);
        NativeMethods.ghostty_terminal_set(NativeHandle, OptDefaultCursorBlink, &value);
    }

    /// <summary>
    /// Creates a tracked grid reference at <paramref name="point"/>. The
    /// reference follows its cell across terminal mutations and reports loss of
    /// value when the location is discarded (e.g. scrollback pruning) — use it
    /// for anchors held across ticks. Returns <see langword="null"/> on failure
    /// (out of memory / unrepresentable point). The caller owns the reference
    /// and must dispose it.
    /// </summary>
    public unsafe TrackedGridRef? TrackGridRef(Point point)
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        var nativePoint = new GhosttyPointNative { Tag = point.NativeTag, X = point.NativeX, Y = point.NativeY };
        nint handle = nint.Zero;
        if (NativeMethods.ghostty_terminal_grid_ref_track(NativeHandle, nativePoint, &handle) != 0
            || handle == nint.Zero)
        {
            return null;
        }

        return new TrackedGridRef(handle, this);
    }

    /// <summary>
    /// Installs a raw two-endpoint selection as the terminal's active selection.
    /// Endpoints are inclusive grid references obtained from <see cref="GetGridRef"/>.
    /// </summary>
    public void SetSelection(GridRef start, GridRef end, bool rectangle = false)
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        var sel = new GhosttySelectionNative
        {
            Size = (nuint)sizeof(GhosttySelectionNative),
            Start = start.Native,
            End = end.Native,
            Rectangle = (byte)(rectangle ? 1 : 0),
        };
        NativeMethods.ghostty_terminal_set(NativeHandle, OptSelection, &sel);
    }

    /// <summary>
    /// True when the active screen has a selection. A cheap probe: the native
    /// getter returns NO_VALUE without formatting any text (the snapshot it
    /// writes is discarded — its untracked grid refs are never used).
    /// </summary>
    public bool HasSelection
    {
        get
        {
            ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
            // Sized struct: the documented ABI requires Size set even on out
            // params (the current pin happens to overwrite without checking,
            // but honor the contract so a pin bump can't break this).
            var sel = new GhosttySelectionNative { Size = (nuint)sizeof(GhosttySelectionNative) };
            return NativeMethods.ghostty_terminal_get(NativeHandle, DataSelection, &sel) == 0;
        }
    }

    /// <summary>Clears the terminal's active selection.</summary>
    public void ClearSelection()
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        NativeMethods.ghostty_terminal_set(NativeHandle, OptSelection, null);
    }

    /// <summary>
    /// Derives a word selection at <paramref name="reference"/> and installs it.
    /// Returns false if there is no selectable word content there.
    /// </summary>
    public bool SelectWord(GridRef reference)
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        var opts = new GhosttyTerminalSelectWordOptionsNative
        {
            Size = (nuint)sizeof(GhosttyTerminalSelectWordOptionsNative),
            Ref = reference.Native,
            BoundaryCodepoints = null,
            BoundaryCodepointsLen = 0,
        };
        var sel = new GhosttySelectionNative { Size = (nuint)sizeof(GhosttySelectionNative) };
        if (NativeMethods.ghostty_terminal_select_word(NativeHandle, &opts, &sel) != 0)
            return false;
        NativeMethods.ghostty_terminal_set(NativeHandle, OptSelection, &sel);
        return true;
    }

    /// <summary>
    /// Derives a line selection at <paramref name="reference"/> and installs it.
    /// Returns false if there is no selectable line content there.
    /// </summary>
    public bool SelectLine(GridRef reference)
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        var opts = new GhosttyTerminalSelectLineOptionsNative
        {
            Size = (nuint)sizeof(GhosttyTerminalSelectLineOptionsNative),
            Ref = reference.Native,
            Whitespace = null,
            WhitespaceLen = 0,
            SemanticPromptBoundary = 0,
        };
        var sel = new GhosttySelectionNative { Size = (nuint)sizeof(GhosttySelectionNative) };
        if (NativeMethods.ghostty_terminal_select_line(NativeHandle, &opts, &sel) != 0)
            return false;
        NativeMethods.ghostty_terminal_set(NativeHandle, OptSelection, &sel);
        return true;
    }

    /// <summary>
    /// Selects all selectable terminal content and installs it. Returns false if
    /// there is no selectable content.
    /// </summary>
    public bool SelectAll()
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        var sel = new GhosttySelectionNative { Size = (nuint)sizeof(GhosttySelectionNative) };
        if (NativeMethods.ghostty_terminal_select_all(NativeHandle, &sel) != 0)
            return false;
        NativeMethods.ghostty_terminal_set(NativeHandle, OptSelection, &sel);
        return true;
    }

    /// <summary>
    /// Formats the terminal's active selection as plain text for the clipboard.
    /// Returns <see langword="null"/> when there is no active selection.
    /// </summary>
    public string? GetSelectionText(bool unwrap = true, bool trim = true)
    {
        ObjectDisposedException.ThrowIf(NativeHandle == nint.Zero, this);
        var opts = new GhosttyTerminalSelectionFormatOptionsNative
        {
            Size = (nuint)sizeof(GhosttyTerminalSelectionFormatOptionsNative),
            Emit = (int)FormatterFormat.PlainText,
            Unwrap = (byte)(unwrap ? 1 : 0),
            Trim = (byte)(trim ? 1 : 0),
            Selection = null, // use active selection
        };

        byte* ptr = null;
        nuint len = 0;
        int result = NativeMethods.ghostty_terminal_selection_format_alloc(
            NativeHandle, nint.Zero, opts, &ptr, &len);
        if (result != 0 || ptr is null || len == 0)
            return null;

        try
        {
            return Marshal.PtrToStringUTF8((nint)ptr, (int)len);
        }
        finally
        {
            NativeMethods.ghostty_free(nint.Zero, ptr, len);
        }
    }
}
