using caTTY.Playground.Experiments;

namespace caTTY.Playground;

/// <summary>
///     ImGui playground application for experimenting with terminal rendering techniques.
///     This application provides a standalone environment for testing ImGui rendering
///     approaches before implementing the full terminal controller.
/// </summary>
public class Program
{
    /// <summary>
    ///     Main entry point for the ImGui playground application.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    public static void Main(string[] args)
    {
        Console.WriteLine("caTTY ImGui Playground - Terminal Rendering Experiments");
        Console.WriteLine("========================================================");
        Console.WriteLine();
        Console.WriteLine("Available experiments:");
        Console.WriteLine("- Character Grid Basic");
        Console.WriteLine("- Fixed-Width Font Test");
        Console.WriteLine("- Color Experiments");
        Console.WriteLine("- Grid Alignment Test");
        Console.WriteLine("- Performance Comparison");
        Console.WriteLine("- Text Styling Experiments (NEW - Task 1.6)");
        Console.WriteLine("- Mouse Input: Scrolling Test");
        Console.WriteLine("- Window Resize Detection Test (NEW)");
        Console.WriteLine("- Text Selection Experiments (NEW - Addresses selection issue)");
        Console.WriteLine();

        TerminalRenderingExperiments.Run();

        Console.WriteLine();
        Console.WriteLine("Exiting...");
        // Console.WriteLine("Press any key to exit...");
        // Console.ReadKey();
    }
}
