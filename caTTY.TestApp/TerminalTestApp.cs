using caTTY.Core.Terminal;
using caTTY.Core.Tracing;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers;
using caTTY.Display.Types;
using caTTY.TestApp.Rendering;

namespace caTTY.TestApp;

/// <summary>
///     Main terminal test application that integrates terminal emulator, process manager, and ImGui controller.
///     This class manages the application lifecycle and coordinates between components.
/// </summary>
public class TerminalTestApp : IDisposable
{
    private readonly SessionManager _sessionManager;
    private ITerminalController? _controller;
    private bool _disposed;

    /// <summary>
    ///     Creates a new terminal test application with session management.
    /// </summary>
    public TerminalTestApp()
    {
        // Create session manager with persisted shell configuration
        _sessionManager = SessionManagerFactory.CreateWithPersistedConfiguration();
    }

    /// <summary>
    ///     Disposes the application and cleans up all resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _controller?.Dispose();
            _sessionManager?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    ///     Runs the terminal test application with BRUTAL ImGui rendering.
    /// </summary>
    public async Task RunAsync()
    {
        Console.WriteLine("Starting shell process...");
        // TerminalTracer.Enabled = true;

        // Display console color test if supported
        ConsoleColorTest.DisplayColorTest();
        Console.WriteLine();

        Console.WriteLine("Initializing BRUTAL ImGui context...");

        // Create TerminalController first to load persisted configuration
        // Option 1: Explicit font configuration (current approach - recommended for production)
        var fontConfig = TerminalFontConfig.CreateForTestApp();
        Console.WriteLine($"TestApp using explicit font configuration: Regular={fontConfig.RegularFontName}, Bold={fontConfig.BoldFontName}");
        _controller = new TerminalController(_sessionManager, fontConfig);

        // Option 2: Automatic detection (alternative approach - convenient for development)
        // Uncomment the following lines to use automatic detection instead:
        // Console.WriteLine("TestApp using automatic font detection");
        // _controller = new TerminalController(_sessionManager);

        // Option 3: Explicit automatic detection (alternative approach - shows detection explicitly)
        // Uncomment the following lines to use explicit automatic detection:
        // var autoConfig = FontContextDetector.DetectAndCreateConfig();
        // Console.WriteLine($"TestApp using detected font configuration: Regular={autoConfig.RegularFontName}, Bold={autoConfig.BoldFontName}");
        // _controller = new TerminalController(_sessionManager, autoConfig);

        // Create launch options for the shell process
        // EASY SHELL SWITCHING: Uncomment one of the following options to change shells
        
        // Option 1: Use persisted configuration (recommended - respects user settings)
        ProcessLaunchOptions? launchOptions = null; // Use default from persisted configuration
        
        // Option 2: Override with specific shell (for testing purposes)
        // var launchOptions = ShellConfiguration.Default();
        
        // Option 3: Simple WSL2 configurations
        // var launchOptions = ShellConfiguration.Wsl();                    // Default WSL distribution
        // var launchOptions = ShellConfiguration.Wsl("Ubuntu");           // Specific distribution
        // var launchOptions = ShellConfiguration.Wsl("Ubuntu", "/home/username"); // With working directory
        
        // Option 4: Windows shells
        // var launchOptions = ShellConfiguration.PowerShell();            // Windows PowerShell
        // var launchOptions = ShellConfiguration.PowerShellCore();        // PowerShell Core (pwsh)
        // var launchOptions = ShellConfiguration.Cmd();                   // Command Prompt
        
        // Option 5: Common pre-configured shells
        // var launchOptions = ShellConfiguration.Common.Ubuntu;           // Ubuntu WSL2
        // var launchOptions = ShellConfiguration.Common.Debian;           // Debian WSL2
        // var launchOptions = ShellConfiguration.Common.GitBash;          // Git Bash
        // var launchOptions = ShellConfiguration.Common.Msys2Bash;        // MSYS2 Bash
        
        // Option 6: Custom shell
        // var launchOptions = ShellConfiguration.Custom(@"C:\custom\shell.exe", "--arg1", "--arg2");

        // Set terminal dimensions and working directory (only if using explicit launch options)
        if (launchOptions != null)
        {
            launchOptions.InitialWidth = 80;
            launchOptions.InitialHeight = 24;
            launchOptions.WorkingDirectory = Environment.CurrentDirectory;
        }

        Console.WriteLine("Creating terminal session...");

        try
        {
            // Create a session with the configured shell (uses persisted configuration if launchOptions is null)
            Console.WriteLine($"Using launch options: {(launchOptions != null ? "explicit" : "from persisted configuration")}");
            var session = await _sessionManager.CreateSessionAsync("Terminal", launchOptions);
            Console.WriteLine($"Shell process started (PID: {session.ProcessManager.ProcessId})");
            Console.WriteLine($"Session created with title: {session.Title}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to start shell process: {ex.Message}");
            throw;
        }

        Console.WriteLine("Starting ImGui render loop...");
        Console.WriteLine("Try running colored commands like: ls --color, echo -e \"\\033[31mRed text\\033[0m\"");
        Console.WriteLine("Try running htop or other applications that change the terminal title!");
        Console.WriteLine();

        // Run the ImGui application loop with update and render
        StandaloneImGui.Run((deltaTime) => 
        {
            // Update controller (handles cursor blinking)
            _controller.Update(deltaTime);
            
            // Render the terminal
            _controller.Render();
        });
    }
}
