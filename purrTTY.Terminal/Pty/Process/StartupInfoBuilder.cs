using System.Runtime.InteropServices;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Creates properly initialized STARTUPINFOEX structures for ConPTY process creation.
///     Handles initialization of the structure size field required by CreateProcessW.
/// </summary>
internal static class StartupInfoBuilder
{
    /// <summary>
    ///     Creates a new STARTUPINFOEX structure with cb field initialized.
    /// </summary>
    /// <returns>An initialized STARTUPINFOEX structure ready for process creation</returns>
    internal static ConPtyNative.STARTUPINFOEX Create()
    {
        var startupInfo = new ConPtyNative.STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<ConPtyNative.STARTUPINFOEX>();

        // STARTF_USESTDHANDLES with null std handles, exactly like node-pty and
        // Windows Terminal: without it, CreateProcess clones the PARENT's std
        // handles into the child (Win 8.1+ behavior when the parent's handles are
        // non-console, even with bInheritHandles=false). A parent with redirected
        // stdio — a test host, CI runner, or the game launched with pipes — then
        // hands the shell ITS pipes instead of the pseudoconsole: console-API
        // calls (title) still reach conpty, but all text output and stdin bypass
        // it, rendering an empty terminal. With the flag set and the handles
        // null, console initialization binds the conpty as the std handles.
        startupInfo.StartupInfo.dwFlags = ConPtyNative.STARTF_USESTDHANDLES;
        return startupInfo;
    }
}
