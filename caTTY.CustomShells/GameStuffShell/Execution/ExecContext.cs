namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Execution context shared across all program executions in a shell session.
/// </summary>
public sealed class ExecContext
{
    /// <summary>
    /// Gets the program resolver.
    /// </summary>
    public IProgramResolver ProgramResolver { get; }

    /// <summary>
    /// Gets the game API (may be null for testing).
    /// </summary>
    public IGameStuffApi? GameApi { get; }

    /// <summary>
    /// Gets the environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// Gets the terminal width.
    /// </summary>
    public int TerminalWidth { get; }

    /// <summary>
    /// Gets the terminal height.
    /// </summary>
    public int TerminalHeight { get; }

    /// <summary>
    /// Gets the callback for writing output to the terminal.
    /// </summary>
    public Action<string, bool> TerminalOutputCallback { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExecContext"/> class.
    /// </summary>
    public ExecContext(
        IProgramResolver programResolver,
        IGameStuffApi? gameApi,
        IReadOnlyDictionary<string, string> environment,
        int terminalWidth,
        int terminalHeight,
        Action<string, bool> terminalOutputCallback)
    {
        ProgramResolver = programResolver;
        GameApi = gameApi;
        Environment = environment;
        TerminalWidth = terminalWidth;
        TerminalHeight = terminalHeight;
        TerminalOutputCallback = terminalOutputCallback;
    }
}
