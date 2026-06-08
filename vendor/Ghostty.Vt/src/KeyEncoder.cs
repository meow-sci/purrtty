using Ghostty.Vt.Internals;
using Ghostty.Vt.Native;

namespace Ghostty.Vt;

public sealed class KeyEncoder : IDisposable
{
    private readonly KeyEncoderSafeHandle _handle;

    public unsafe KeyEncoder()
    {
        nint handle;
        var result = NativeMethods.ghostty_key_encoder_new(nint.Zero, &handle);
        GhosttyException.ThrowIfFailure(result);
        _handle = new KeyEncoderSafeHandle(handle);
    }

    public void ConfigureFromTerminal(Terminal terminal)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        NativeMethods.ghostty_key_encoder_setopt_from_terminal(
            _handle.DangerousGetHandle(), terminal.NativeHandle);
    }

    public unsafe byte[] Encode(KeyEvent keyEvent)
    {
        ObjectDisposedException.ThrowIf(_handle.IsInvalid, this);
        Span<byte> buf = stackalloc byte[256];
        fixed (byte* ptr = buf)
        {
            nuint len;
            var result = NativeMethods.ghostty_key_encoder_encode(
                _handle.DangerousGetHandle(), keyEvent.NativeHandle, ptr, 256, &len);
            GhosttyException.ThrowIfFailure(result);
            // Copy out of the stack buffer while it is still alive. Returning a
            // span into `buf` is a use-after-scope (the stackalloc is freed when
            // this method returns); the caller then reads clobbered stack memory.
            // That was the real cause of the cross-platform "first-use misfire"
            // (garbage byte read: 0x00 on macOS, 0xB0 on Windows x64).
            return buf[..(int)len].ToArray();
        }
    }

    public void Dispose() => _handle.Dispose();

    private sealed class KeyEncoderSafeHandle : GhosttySafeHandle
    {
        public KeyEncoderSafeHandle(nint handle) { SetHandle(handle); }
        protected override void Free(nint h) => NativeMethods.ghostty_key_encoder_free(h);
        public new nint DangerousGetHandle() => handle;
    }
}
