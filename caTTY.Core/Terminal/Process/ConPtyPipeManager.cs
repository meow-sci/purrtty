using System.Runtime.InteropServices;

namespace caTTY.Core.Terminal.Process;

/// <summary>
///     Manages creation and initialization of ConPTY communication pipes and pseudoconsole.
///     Handles the low-level Windows API calls for pipe creation and pseudoconsole setup.
/// </summary>
internal static class ConPtyPipeManager
{
    /// <summary>
    ///     Result of pipe and pseudoconsole creation.
    /// </summary>
    internal record PipeHandles(
        IntPtr InputReadHandle,
        IntPtr InputWriteHandle,
        IntPtr OutputReadHandle,
        IntPtr OutputWriteHandle,
        IntPtr PseudoConsole);

    /// <summary>
    ///     Creates communication pipes and pseudoconsole for ConPTY.
    /// </summary>
    /// <param name="size">The initial terminal size</param>
    /// <returns>The created pipe handles and pseudoconsole</returns>
    /// <exception cref="ProcessStartException">Thrown if pipe or pseudoconsole creation fails</exception>
    internal static PipeHandles CreatePipesAndPseudoConsole(ConPtyNative.COORD size)
    {
        IntPtr inputReadHandle = IntPtr.Zero;
        IntPtr inputWriteHandle = IntPtr.Zero;
        IntPtr outputReadHandle = IntPtr.Zero;
        IntPtr outputWriteHandle = IntPtr.Zero;
        IntPtr pseudoConsole = IntPtr.Zero;

        try
        {
            // Create input pipe
            if (!ConPtyNative.CreatePipe(out inputReadHandle, out inputWriteHandle, IntPtr.Zero, 0))
            {
                throw new ProcessStartException($"Failed to create input pipe: {Marshal.GetLastWin32Error()}");
            }

            // Create output pipe
            if (!ConPtyNative.CreatePipe(out outputReadHandle, out outputWriteHandle, IntPtr.Zero, 0))
            {
                throw new ProcessStartException($"Failed to create output pipe: {Marshal.GetLastWin32Error()}");
            }

            // Create pseudoconsole
            int result = ConPtyNative.CreatePseudoConsole(size, inputReadHandle, outputWriteHandle, 0, out pseudoConsole);
            if (result != 0)
            {
                throw new ProcessStartException($"Failed to create pseudoconsole: {result}");
            }

            // Close the handles that were passed to the pseudoconsole (as per Microsoft docs)
            ConPtyNative.CloseHandle(inputReadHandle);
            ConPtyNative.CloseHandle(outputWriteHandle);

            // Return only the handles we keep (input write and output read)
            return new PipeHandles(
                IntPtr.Zero,  // inputReadHandle closed
                inputWriteHandle,
                outputReadHandle,
                IntPtr.Zero,  // outputWriteHandle closed
                pseudoConsole);
        }
        catch
        {
            // Clean up on error
            if (pseudoConsole != IntPtr.Zero)
            {
                ConPtyNative.ClosePseudoConsole(pseudoConsole);
            }
            if (inputReadHandle != IntPtr.Zero)
            {
                ConPtyNative.CloseHandle(inputReadHandle);
            }
            if (inputWriteHandle != IntPtr.Zero)
            {
                ConPtyNative.CloseHandle(inputWriteHandle);
            }
            if (outputReadHandle != IntPtr.Zero)
            {
                ConPtyNative.CloseHandle(outputReadHandle);
            }
            if (outputWriteHandle != IntPtr.Zero)
            {
                ConPtyNative.CloseHandle(outputWriteHandle);
            }
            throw;
        }
    }
}
