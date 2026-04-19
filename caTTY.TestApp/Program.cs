namespace caTTY.TestApp;

/// <summary>
///     Standalone BRUTAL ImGui test application for caTTY terminal emulator.
///     This application provides a complete terminal emulator using the same ImGui tech stack as the game mod.
/// </summary>
public class Program
{
    /// <summary>
    ///     Main entry point for the test application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    public static async Task Main(string[] args)
    {
        Console.WriteLine("caTTY BRUTAL ImGui Test Application");
        Console.WriteLine("===================================");
        Console.WriteLine();
        Console.WriteLine("Initializing terminal emulator and BRUTAL ImGui context...");

        try
        {
            var app = new TerminalTestApp();
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application failed: {ex.Message}");
            Console.WriteLine("Ensure KSA is installed and graphics drivers are available.");
            Console.WriteLine("The application must be run from the project directory (caTTY.TestApp/).");
            Environment.Exit(1);
        }
    }
}
