namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Context passed to a program during execution.
/// </summary>
public sealed class ProgramContext
{
    /// <summary>
    /// Gets the program arguments (argv[0] is the program name).
    /// </summary>
    public IReadOnlyList<string> Argv { get; }

    /// <summary>
    /// Gets the stream set (stdin, stdout, stderr).
    /// </summary>
    public StreamSet Streams { get; }

    /// <summary>
    /// Gets the program resolver for invoking other programs (e.g., from xargs).
    /// </summary>
    public IProgramResolver ProgramResolver { get; }

    /// <summary>
    /// Gets the game API for accessing game state.
    /// </summary>
    public IGameStuffApi? GameApi { get; }

    /// <summary>
    /// Gets the environment variables.
    /// </summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    /// <summary>
    /// Gets the current terminal width.
    /// </summary>
    public int TerminalWidth { get; }

    /// <summary>
    /// Gets the current terminal height.
    /// </summary>
    public int TerminalHeight { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgramContext"/> class.
    /// </summary>
    public ProgramContext(
        IReadOnlyList<string> argv,
        StreamSet streams,
        IProgramResolver programResolver,
        IGameStuffApi? gameApi,
        IReadOnlyDictionary<string, string> environment,
        int terminalWidth,
        int terminalHeight)
    {
        Argv = argv;
        Streams = streams;
        ProgramResolver = programResolver;
        GameApi = gameApi;
        Environment = environment;
        TerminalWidth = terminalWidth;
        TerminalHeight = terminalHeight;
    }
}
