using System.Runtime.InteropServices;

namespace caTTY.Core.Terminal.Process;

/// <summary>
///     Manages creation and cleanup of process thread attribute lists for ConPTY.
///     Handles unsafe attribute list operations for pseudoconsole attachment.
/// </summary>
internal static class AttributeListBuilder
{
    /// <summary>
    ///     Creates and initializes a process thread attribute list with the specified pseudoconsole.
    /// </summary>
    /// <param name="pseudoConsole">The pseudoconsole handle to attach to the attribute list</param>
    /// <returns>A pointer to the initialized attribute list that must be freed with FreeAttributeList</returns>
    /// <exception cref="ProcessStartException">Thrown if attribute list creation or initialization fails</exception>
    internal static IntPtr CreateAttributeListWithPseudoConsole(IntPtr pseudoConsole)
    {
        // Probe for the size of the attribute list
        IntPtr attributeListSize = IntPtr.Zero;
        ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);

        // Allocate memory for the attribute list
        IntPtr attributeList = Marshal.AllocHGlobal(attributeListSize);

        try
        {
            // Initialize the attribute list
            if (!ConPtyNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                int error = Marshal.GetLastWin32Error();
                Marshal.FreeHGlobal(attributeList);
                throw new ProcessStartException($"Failed to initialize attribute list: {error}");
            }

            // Set the pseudoconsole attribute
            if (!ConPtyNative.UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ConPtyNative.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    pseudoConsole,
                    IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                ConPtyNative.DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
                throw new ProcessStartException($"Failed to set pseudoconsole attribute: {error}");
            }

            return attributeList;
        }
        catch
        {
            // If any exception occurs after allocation, ensure cleanup
            try
            {
                Marshal.FreeHGlobal(attributeList);
            }
            catch
            {
                // Ignore cleanup errors during exception handling
            }
            throw;
        }
    }

    /// <summary>
    ///     Frees a process thread attribute list created by CreateAttributeListWithPseudoConsole.
    /// </summary>
    /// <param name="attributeList">The attribute list pointer to free</param>
    internal static void FreeAttributeList(IntPtr attributeList)
    {
        if (attributeList != IntPtr.Zero)
        {
            ConPtyNative.DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
        }
    }
}
