using System.Runtime.InteropServices;

namespace caTTY.Core.Terminal.Process;

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
        return startupInfo;
    }
}
