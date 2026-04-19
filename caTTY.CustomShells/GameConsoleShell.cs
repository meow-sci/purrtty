using System.Text;
using Brutal.ImGuiApi.Abstractions;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using KSA;

namespace caTTY.CustomShells;

/// <summary>
///     Custom shell implementation that provides a command-line interface to the KSA game console.
///     This shell executes commands through KSA's TerminalInterface and displays results with appropriate formatting.
///
///     Inherits from BaseLineBufferedShell which provides:
///     - Line buffering with command history
///     - Input processing (backspace, enter, arrow keys, Ctrl+L)
///     - Channel-based output pump pattern
///     - Escape sequence state machine
/// </summary>
public class GameConsoleShell : BaseLineBufferedShell
{
    // Harmony coordination - static fields for patch to access
    private static GameConsoleShell? _activeInstance;
    private static readonly object _activeLock = new();

    // Track if we're currently executing a command (prevents recursion)
    private bool _isExecutingCommand;

    // Prompt configuration
    private string _promptValue = "ksa> ";

    /// <inheritdoc />
    public override CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create(
        name: "Game Console",
        description: "KSA game console interface - execute game commands directly",
        version: new Version(1, 0, 0),
        author: "caTTY",
        supportedFeatures: new[] { "colors", "clear-screen", "command-execution" }
    );

    /// <summary>
    ///     Loads the prompt string from the saved configuration.
    /// </summary>
    private void LoadPromptFromConfiguration()
    {
        try
        {
            var config = ThemeConfiguration.Load();
            _promptValue = config.GameShellPrompt;
        }
        catch (Exception)
        {
            // If loading fails, keep the default prompt
            _promptValue = "ksa> ";
        }
    }

    /// <inheritdoc />
    protected override async Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
    {
        // Verify TerminalInterface is available
        if (Program.TerminalInterface == null)
        {
            throw new InvalidOperationException(
                "KSA TerminalInterface is not available. The game may not be fully initialized yet.");
        }

        // Load prompt from configuration
        LoadPromptFromConfiguration();

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task OnStoppingAsync(CancellationToken cancellationToken)
    {
        // Send goodbye message through the channel
        QueueOutput("\r\n\x1b[1;33mGame Console shell terminated.\x1b[0m\r\n");

        // Raise termination event
        RaiseTerminated(0, "User requested shutdown");

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public override void SendInitialOutput()
    {
        // Load prompt from configuration before sending
        LoadPromptFromConfiguration();

        // Send banner and prompt
        // This happens AFTER the shell is fully initialized and wired to the terminal
        var banner = "\x1b[1;36m" +  // Cyan bold
                     "=================================================\r\n" +
                     "  KSA Game Console Shell\r\n" +
                     "  Type 'help' for available commands\r\n" +
                     "  Press Ctrl+L to clear screen\r\n" +
                     "=================================================\x1b[0m\r\n";
        QueueOutput(banner);
        QueueOutput(_promptValue);
    }

    /// <inheritdoc />
    protected override string GetPrompt()
    {
        return _promptValue;
    }

    /// <inheritdoc />
    protected override void ExecuteCommandLine(string commandLine)
    {
        // Check if it's a built-in command first
        if (TryHandleBuiltinCommand(commandLine))
        {
            SendPrompt();
            return;
        }

        try
        {
            // Set this instance as active so Harmony patch can capture output
            lock (_activeLock)
            {
                _activeInstance = this;
                _isExecutingCommand = true;
            }

            try
            {
                // Execute the command via KSA's TerminalInterface
                // Output will be captured by our Harmony patch on ConsoleWindow.Print()
                bool success = Program.TerminalInterface.Execute(commandLine);

                // Send prompt
                SendPrompt();
            }
            finally
            {
                // Clear active instance
                lock (_activeLock)
                {
                    _isExecutingCommand = false;
                    _activeInstance = null;
                }
            }
        }
        catch (Exception ex)
        {
            // Handle any exceptions during command execution - send as stderr
            SendError($"\x1b[31mError executing command: {ex.Message}\x1b[0m\r\n");
            SendPrompt();
        }
    }

    /// <inheritdoc />
    protected override void HandleClearScreen()
    {
        // ESC[2J = Clear entire screen (but preserves scrollback)
        // ESC[H = Move cursor to home position (0,0)
        SendOutput("\x1b[2J\x1b[H");
    }

    /// <summary>
    ///     Tries to handle a built-in shell command.
    /// </summary>
    /// <param name="command">The command to check</param>
    /// <returns>True if the command was handled as a built-in command, false otherwise</returns>
    private bool TryHandleBuiltinCommand(string command)
    {
        switch (command.Trim().ToLowerInvariant())
        {
            case "clear":
                ClearScreenAndScrollback();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///     Clears the entire display including the scrollback buffer and moves cursor to home.
    ///     Used by the "clear" command to completely wipe all terminal history.
    /// </summary>
    private void ClearScreenAndScrollback()
    {
        // ESC[3J = Clear entire screen and scrollback buffer (xterm extension, mode 3)
        SendOutput("\x1b[3J");
    }

    /// <inheritdoc />
    public override void RequestCancellation()
    {
        // For now, we don't support command cancellation
        // In the future, this could interrupt long-running game commands
        SendOutput("\r\n\x1b[33m^C\x1b[0m\r\n");
        SendPrompt();
    }

    /// <summary>
    ///     Internal method called by Harmony patch to handle captured console output.
    /// </summary>
    public static void OnConsolePrint(string output, uint color, ConsoleLineType lineType)
    {
        lock (_activeLock)
        {
            if (_activeInstance == null || !_activeInstance._isExecutingCommand)
            {
                return; // Not currently executing in our shell
            }

            try
            {
                // Forward to the active shell instance
                // Determine if this is an error based on color (red = error)
                bool isError = color == ConsoleWindow.ErrorColor || color == ConsoleWindow.CriticalColor;

                if (isError)
                {
                    // Send errors as stderr with red formatting
                    string formattedError = $"\x1b[31m{output}\x1b[0m\r\n";
                    _activeInstance.SendError(formattedError);
                }
                else
                {
                    // Send normal output as stdout
                    string formattedOutput = $"{output}\r\n";
                    _activeInstance.SendOutput(formattedOutput);
                }
            }
            catch (Exception)
            {
                // Silently handle errors to avoid disrupting the game console
            }
        }
    }
}
