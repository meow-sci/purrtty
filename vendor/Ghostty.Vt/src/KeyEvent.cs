using System.Runtime.InteropServices;
using System.Text;
using Ghostty.Vt.Native;

namespace Ghostty.Vt;

public sealed class KeyEvent : IDisposable
{
    private nint _handle;

    // purrtty fix: the native event does NOT copy the utf8 text — it stores the
    // raw (ptr, len) and reads it later, at encode time (key/event.h: "The
    // caller must ensure the string remains valid for the lifetime needed by
    // the event"). Upstream pinned the bytes only for the duration of the
    // setter call (`fixed`), leaving the native side holding a pointer into an
    // unpinned, unreferenced array that the GC may move or collect before
    // encode — the same use-after-scope class as the fixed encoder bug
    // (CLAUDE.md gotcha 3). We keep a persistent pin, replaced on each set and
    // released in Dispose.
    private GCHandle _textPin;

    public unsafe KeyEvent()
    {
        nint handle;
        var result = NativeMethods.ghostty_key_event_new(nint.Zero, &handle);
        GhosttyException.ThrowIfFailure(result);
        _handle = handle;
    }

    private int _key;
    /// <summary>Physical key code (GhosttyKey enum value).</summary>
    public int Key
    {
        get => _key;
        set { _key = value; NativeMethods.ghostty_key_event_set_key(_handle, value); }
    }

    private int _action = 1; // press
    /// <summary>Action type: 0=release, 1=press, 2=repeat.</summary>
    public int Action
    {
        get => _action;
        set { _action = value; NativeMethods.ghostty_key_event_set_action(_handle, value); }
    }

    private ushort _modifiers;
    /// <summary>Modifier bitmask (GHOSTTY_MODS_* values).</summary>
    public ushort Modifiers
    {
        get => _modifiers;
        set { _modifiers = value; NativeMethods.ghostty_key_event_set_mods(_handle, value); }
    }

    public unsafe string? Text
    {
        get => _text;
        set
        {
            _text = value;
            if (_textPin.IsAllocated)
                _textPin.Free();
            if (string.IsNullOrEmpty(value))
            {
                // Clear any previously-installed pointer; without this a stale
                // (and now dangling) text pointer would survive a `Text = null`.
                _textPin = default;
                NativeMethods.ghostty_key_event_set_utf8(_handle, null, 0);
                return;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            _textPin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            NativeMethods.ghostty_key_event_set_utf8(
                _handle, (byte*)_textPin.AddrOfPinnedObject(), (nuint)bytes.Length);
        }
    }
    private string? _text;

    // Legacy property for backwards compatibility
    public uint KeyCode { get => (uint)Key; set => Key = (int)value; }

    internal nint NativeHandle => _handle;

    public void Dispose()
    {
        if (_handle != nint.Zero)
        {
            NativeMethods.ghostty_key_event_free(_handle);
            _handle = nint.Zero; // purrtty fix: guard against a double-free on a second Dispose
        }
        if (_textPin.IsAllocated)
            _textPin.Free();
    }
}
