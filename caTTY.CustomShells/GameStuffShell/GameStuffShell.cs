using System.Text;
using caTTY.Core.Terminal;
using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Lexing;
using caTTY.CustomShells.GameStuffShell.Parsing;
using caTTY.CustomShells.GameStuffShell.Programs;

namespace caTTY.CustomShells.GameStuffShell;

public sealed class GameStuffShell : BaseLineBufferedShell
{
    private readonly CustomShellMetadata _metadata = CustomShellMetadata.Create(
        name: "Game Stuff",
        description: "Game Stuff shell - bash-like command interpreter",
        version: new Version(1, 0, 0),
        author: "caTTY",
        supportedFeatures: new[] { "line-editing", "history", "clear-screen" }
    );

    private string _promptValue = "gstuff> ";
    private readonly Executor _executor = new();
    private ExecContext? _execContext;

    public override CustomShellMetadata Metadata => _metadata;

    protected override Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
    {
        // Create program registry and register built-in programs
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new SleepProgram());
        registry.Register(new CraftsProgram());
        registry.Register(new FollowProgram());
        registry.Register(new XargsProgram());
        registry.Register(new ShProgram());

        _execContext = new ExecContext(
            registry,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (isError)
                {
                    SendError(text);
                }
                else
                {
                    SendOutput(text);
                }
            });

        return Task.CompletedTask;
    }

    protected override Task OnStoppingAsync(CancellationToken cancellationToken)
    {
        QueueOutput("\r\n\x1b[1;33mGame Stuff shell terminated.\x1b[0m\r\n");
        RaiseTerminated(0, "User requested shutdown");
        return Task.CompletedTask;
    }

    public override void SendInitialOutput()
    {
        var banner = new StringBuilder()
            .Append("\x1b[1;36m")
            .Append("=================================================\r\n")
            .Append("  Game Stuff Shell v1.0.0\r\n")
            .Append("  Type commands below to get started\r\n")
            .Append("  Press Ctrl+L to clear screen\r\n")
            .Append("=================================================\x1b[0m\r\n")
            .ToString();

        QueueOutput(banner);
        QueueOutput(_promptValue);
    }

    protected override string GetPrompt()
    {
        return _promptValue;
    }

    protected override void ExecuteCommandLine(string commandLine)
    {
        // Ignore empty commands
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            SendPrompt();
            return;
        }

        // Run execution asynchronously and block on result
        // (This is acceptable since ExecuteCommandLine is called from WriteInputAsync which is sync-over-async)
        try
        {
            var task = ExecuteCommandLineAsync(commandLine, CancellationToken.None);
            task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            SendError($"\x1b[31mUnexpected error: {ex.Message}\x1b[0m\r\n");
        }

        SendPrompt();
    }

    private async Task ExecuteCommandLineAsync(string commandLine, CancellationToken cancellationToken)
    {
        if (_execContext is null)
        {
            SendError("\x1b[31mShell not initialized\x1b[0m\r\n");
            return;
        }

        // Lex
        var lexer = new Lexer(commandLine);
        IReadOnlyList<Token> tokens;
        try
        {
            tokens = lexer.Lex();
        }
        catch (Exception ex)
        {
            SendError($"\x1b[31mLexer error: {ex.Message}\x1b[0m\r\n");
            return;
        }

        // Parse
        var parser = new Parser(tokens);
        ListNode ast;
        try
        {
            ast = parser.ParseList();
        }
        catch (ParserException ex)
        {
            SendError($"\x1b[31mParse error: {ex.Message}\x1b[0m\r\n");
            return;
        }
        catch (Exception ex)
        {
            SendError($"\x1b[31mParser error: {ex.Message}\x1b[0m\r\n");
            return;
        }

        // Execute
        try
        {
            var exitCode = await _executor.ExecuteListAsync(ast, _execContext, cancellationToken);
            // Exit code is available but not currently used
            // Could be stored for $? variable in the future
        }
        catch (Exception ex)
        {
            SendError($"\x1b[31mExecution error: {ex.Message}\x1b[0m\r\n");
        }
    }

    protected override void HandleClearScreen()
    {
        SendOutput("\x1b[2J\x1b[H");
    }

    public override void RequestCancellation()
    {
        SendOutput("\r\n\x1b[33m^C\x1b[0m\r\n");
        SendPrompt();
    }
}
