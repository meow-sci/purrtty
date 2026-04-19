using System.Runtime.InteropServices;

namespace caTTY.TestApp;

/// <summary>
/// Console color test utility for testing ANSI color support in the host console.
/// Only outputs colors if the console supports ANSI escape sequences.
/// </summary>
public static class ConsoleColorTest
{
    private static bool? _ansiSupported;

    /// <summary>
    /// Check if the current console supports ANSI escape sequences.
    /// </summary>
    /// <returns>True if ANSI is supported, false otherwise</returns>
    public static bool IsAnsiSupported()
    {
        if (_ansiSupported.HasValue)
        {
            return _ansiSupported.Value;
        }

        try
        {
            // On Windows, check if we can enable virtual terminal processing
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _ansiSupported = EnableWindowsAnsiSupport();
            }
            else
            {
                // On Unix-like systems, assume ANSI is supported
                _ansiSupported = true;
            }
        }
        catch
        {
            _ansiSupported = false;
        }

        return _ansiSupported.Value;
    }

    /// <summary>
    /// Display a color test in the console if ANSI is supported.
    /// </summary>
    public static void DisplayColorTest()
    {
        if (!IsAnsiSupported())
        {
            Console.WriteLine("Console does not support ANSI colors - skipping color test");
            return;
        }

        Console.WriteLine("Console ANSI Color Test:");
        Console.WriteLine("========================");

        // Test standard colors
        Console.WriteLine("Standard Colors:");
        for (int i = 30; i <= 37; i++)
        {
            Console.Write($"\x1b[{i}m■\x1b[0m ");
        }
        Console.WriteLine();

        // Test bright colors
        Console.WriteLine("Bright Colors:");
        for (int i = 90; i <= 97; i++)
        {
            Console.Write($"\x1b[{i}m■\x1b[0m ");
        }
        Console.WriteLine();

        // Test background colors
        Console.WriteLine("Background Colors:");
        for (int i = 40; i <= 47; i++)
        {
            Console.Write($"\x1b[{i}m \x1b[0m");
        }
        Console.WriteLine();

        // Test some 256-color palette samples
        Console.WriteLine("256-Color Palette Samples:");
        var sampleColors = new[] { 196, 46, 21, 226, 201, 51 }; // Red, Green, Blue, Yellow, Pink, Cyan
        foreach (int color in sampleColors)
        {
            Console.Write($"\x1b[38;5;{color}m■\x1b[0m ");
        }
        Console.WriteLine();

        // Test RGB colors
        Console.WriteLine("RGB Color Samples:");
        var rgbSamples = new[] 
        { 
            (255, 0, 0),    // Red
            (0, 255, 0),    // Green  
            (0, 0, 255),    // Blue
            (255, 255, 0),  // Yellow
            (255, 0, 255),  // Magenta
            (0, 255, 255)   // Cyan
        };
        
        foreach (var (r, g, b) in rgbSamples)
        {
            Console.Write($"\x1b[38;2;{r};{g};{b}m■\x1b[0m ");
        }
        Console.WriteLine();

        // Test text styling
        Console.WriteLine("Text Styling:");
        Console.WriteLine($"\x1b[1mBold\x1b[0m \x1b[3mItalic\x1b[0m \x1b[4mUnderline\x1b[0m \x1b[9mStrikethrough\x1b[0m");

        Console.WriteLine();
    }

    /// <summary>
    /// Enable ANSI support on Windows by setting console mode.
    /// </summary>
    private static bool EnableWindowsAnsiSupport()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            if (!GetConsoleMode(handle, out uint mode))
            {
                return false;
            }

            // Enable virtual terminal processing (ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004)
            mode |= 0x0004;
            return SetConsoleMode(handle, mode);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}