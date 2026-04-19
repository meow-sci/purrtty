using System;
using System.Runtime.InteropServices;
using System.Text;

namespace caTTY.Display.Utils;

/// <summary>
/// Manages clipboard operations for the terminal controller.
/// Provides cross-platform clipboard access with Windows-specific optimizations.
/// </summary>
public static class ClipboardManager
{
    /// <summary>
    /// Sets text to the system clipboard.
    /// </summary>
    /// <param name="text">The text to copy to the clipboard</param>
    /// <returns>True if the operation succeeded, false otherwise</returns>
    public static bool SetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            // Try Windows-specific clipboard API first (most reliable for KSA game context)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return SetTextWindows(text);
            }

            // Fallback for other platforms (though KSA is Windows-only)
            return SetTextFallback(text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ClipboardManager: Failed to set clipboard text: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets text from the system clipboard.
    /// </summary>
    /// <returns>The clipboard text, or null if the operation failed</returns>
    public static string? GetText()
    {
        try
        {
            // Try Windows-specific clipboard API first
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetTextWindows();
            }

            // Fallback for other platforms
            return GetTextFallback();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ClipboardManager: Failed to get clipboard text: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Windows-specific clipboard implementation using Win32 API.
    /// </summary>
    private static bool SetTextWindows(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return false;
            }

            try
            {
                if (!EmptyClipboard())
                {
                    return false;
                }

                // Convert text to UTF-16 (Windows Unicode)
                byte[] bytes = Encoding.Unicode.GetBytes(text + '\0');
                IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes.Length);
                
                if (hGlobal == IntPtr.Zero)
                {
                    return false;
                }

                try
                {
                    IntPtr pGlobal = GlobalLock(hGlobal);
                    if (pGlobal == IntPtr.Zero)
                    {
                        return false;
                    }

                    try
                    {
                        Marshal.Copy(bytes, 0, pGlobal, bytes.Length);
                    }
                    finally
                    {
                        GlobalUnlock(hGlobal);
                    }

                    if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                    {
                        return false;
                    }

                    // hGlobal is now owned by the clipboard, don't free it
                    hGlobal = IntPtr.Zero;
                    return true;
                }
                finally
                {
                    if (hGlobal != IntPtr.Zero)
                    {
                        GlobalFree(hGlobal);
                    }
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ClipboardManager: Windows clipboard set failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Windows-specific clipboard text retrieval.
    /// </summary>
    private static string? GetTextWindows()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return null;
            }

            try
            {
                IntPtr hData = GetClipboardData(CF_UNICODETEXT);
                if (hData == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr pData = GlobalLock(hData);
                if (pData == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUni(pData);
                }
                finally
                {
                    GlobalUnlock(hData);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ClipboardManager: Windows clipboard get failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fallback clipboard implementation for non-Windows platforms.
    /// Note: KSA is Windows-only, so this is primarily for completeness.
    /// </summary>
    private static bool SetTextFallback(string text)
    {
        // For non-Windows platforms, we could implement platform-specific clipboard access
        // or use a cross-platform library. For now, just log and return false.
        Console.WriteLine("ClipboardManager: Clipboard operations not supported on this platform");
        return false;
    }

    /// <summary>
    /// Fallback clipboard text retrieval for non-Windows platforms.
    /// </summary>
    private static string? GetTextFallback()
    {
        Console.WriteLine("ClipboardManager: Clipboard operations not supported on this platform");
        return null;
    }

    // Windows API constants
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    // Windows API functions
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}