using System.Text;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Abstractions;
using purrTTY.Core.Terminal;
using KSA;

namespace purrTTY.CustomShells;

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

    // True while a command is executing via TerminalInterface — the capture
    // gate for the Harmony console-print patch (output printed outside this
    // window is not ours and must not be captured).
    private bool _isExecutingCommand;

    /// <summary>
    ///     Lock-free fast gate for the Harmony console-capture postfix. The new
    ///     Brutal <c>ConsoleWindow.Print</c> sink hands us a <c>ReadOnlySpan&lt;char&gt;</c>
    ///     (it no longer allocates a string), so the postfix only materializes a
    ///     string while a command is actually executing in this shell. The
    ///     authoritative, locked check lives in <see cref="OnConsolePrint"/>.
    /// </summary>
    public static bool IsCapturing => _activeInstance is { _isExecutingCommand: true };

    // Prompt configuration
    private string _promptValue = "ksa> ";

    /// <inheritdoc />
    public override CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create(
        name: "Game Console",
        description: "KSA game console interface - execute game commands directly",
        version: new Version(1, 0, 0),
        author: "purrTTY",
        supportedFeatures: new[] { "colors", "clear-screen", "command-execution" }
    );

    /// <inheritdoc />
    protected override Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
    {
        // Verify the game console plumbing is available. Program.ConsoleWindow
        // must be null-checked first: Program.TerminalInterface dereferences it
        // (`=> ConsoleWindow.Terminal`), so probing TerminalInterface alone
        // throws the very NRE this guard exists to convert into a clear error.
        if (Program.ConsoleWindow is null || Program.TerminalInterface is null)
        {
            throw new InvalidOperationException(
                "KSA TerminalInterface is not available. The game may not be fully initialized yet.");
        }

        ApplyPromptFromOptions(options);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Applies the configured prompt from the launch options' environment
    ///     (stamped by the launcher from the user's configuration; see
    ///     <see cref="WellKnownShellEnvironment.GameShellPrompt"/>) — the shell
    ///     layer has no dependency on the display layer's configuration types.
    ///     Absent or empty keeps the default prompt.
    /// </summary>
    protected void ApplyPromptFromOptions(CustomShellStartOptions options)
    {
        if (options.EnvironmentVariables.TryGetValue(WellKnownShellEnvironment.GameShellPrompt, out var prompt)
            && !string.IsNullOrEmpty(prompt))
        {
            _promptValue = prompt;
        }
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
                // Execute the command via KSA's TerminalInterface. Output —
                // including Execute's own error text — is captured by the
                // Harmony patch on ConsoleWindow.Print(), so the bool result
                // carries no extra information.
                Program.TerminalInterface.Execute(commandLine);

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
        // Game commands execute synchronously, so there is nothing in-flight to
        // interrupt — but the visually cancelled input must also be discarded
        // (otherwise the next Enter would execute the line the ^C dismissed).
        CancelCurrentLine();
        SendOutput("\r\n\x1b[33m^C\x1b[0m\r\n");
        SendPrompt();
    }

    /// <summary>
    ///     Internal method called by Harmony patch to handle captured console output.
    /// </summary>
    /// <remarks>
    ///     Known limitations (inherent to capturing the game's <c>Print</c> sink):
    ///     each captured <c>Print</c> call is terminated with <c>\r\n</c>, so a
    ///     single logical line emitted as multiple colored segments shows spurious
    ///     line breaks. Capture is bounded to the synchronous extent of the
    ///     command's <c>Execute</c> (see <see cref="IsCapturing"/>): output a
    ///     command produces asynchronously after it returns is lost, and unrelated
    ///     cross-thread prints during <c>Execute</c> are mis-attributed to it.
    /// </remarks>
    public static void OnConsolePrint(string output, ImColor8 color)
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
                // Determine if this is an error based on color (red = error).
                // ImColor8 has no == operator, so compare packed uint values. The
                // Brutal API is version-sensitive across the KSA builds we target:
                // ConsoleWindow.ErrorColor/CriticalColor are ImColor8 on newer
                // builds but a raw uint on older ones. ToUint() is overloaded for
                // both, so overload resolution picks the right path per build with
                // no conditional compilation.
                uint c = color.AsUint();
                bool isError = c == ToUint(ConsoleWindow.ErrorColor) || c == ToUint(ConsoleWindow.CriticalColor);

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

    // Cross-version color normalization. Newer Brutal builds expose console
    // colors as ImColor8; older ones use a raw uint. Both overloads exist so
    // the compiler binds whichever matches the installed KSA assemblies.
    private static uint ToUint(ImColor8 color) => color.AsUint();
    private static uint ToUint(uint color) => color;
}
