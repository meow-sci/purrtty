using Ghostty.Vt.Native;

namespace Ghostty.Vt;

public static class Paste
{
    public static unsafe bool IsSafe(ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data)
        {
            return NativeMethods.ghostty_paste_is_safe(ptr, (nuint)data.Length);
        }
    }

    public static unsafe byte[] Encode(ReadOnlySpan<byte> data, bool bracketed = true)
    {
        // ghostty_paste_encode modifies its input buffer IN PLACE (control bytes
        // → spaces, \n → \r per paste.h) before writing the encoded result to the
        // output buffer. The caller's span may point at read-only memory (a u8
        // literal lives on a read-only page), so copy into a mutable scratch and
        // hand that to native instead of pinning the caller's span as mutable.
        byte[] src = data.ToArray();

        // Worst case: bracketed paste adds prefix + suffix around the data
        int maxLen = data.Length + 64;
        byte[] buf = new byte[maxLen];
        fixed (byte* srcPtr = src)
        fixed (byte* dstPtr = buf)
        {
            nuint written;
            var result = NativeMethods.ghostty_paste_encode(
                srcPtr, (nuint)src.Length, bracketed, dstPtr, (nuint)maxLen, &written);
            GhosttyException.ThrowIfFailure(result);
            if (written == 0) return [];
            var bytes = new byte[(int)written];
            new ReadOnlySpan<byte>(dstPtr, (int)written).CopyTo(bytes);
            return bytes;
        }
    }
}
