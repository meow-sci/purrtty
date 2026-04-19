using caTTY.CustomShells.GameStuffShell.Execution;
using System.Text;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Reads stdin, splits on whitespace, and invokes a target program once per token.
/// </summary>
public sealed class XargsProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "xargs";

    public async Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        // xargs requires at least one argument (the target program name)
        if (context.Argv.Count < 2)
        {
            await context.Streams.Stderr.WriteAsync("xargs: missing program name\n", cancellationToken);
            return 2; // Usage error
        }

        var targetProgramName = context.Argv[1];
        var fixedArgs = context.Argv.Skip(2).ToList();

        // Read all of stdin
        var stdinContent = await context.Streams.Stdin.ReadAllAsync(cancellationToken);

        // Split on whitespace
        var tokens = SplitOnWhitespace(stdinContent);

        // If stdin is empty, exit with error
        if (tokens.Count == 0)
        {
            await context.Streams.Stderr.WriteAsync("xargs: no input\n", cancellationToken);
            return 2; // Usage error
        }

        // Resolve the target program once
        if (!context.ProgramResolver.TryResolve(targetProgramName, out var targetProgram))
        {
            await context.Streams.Stderr.WriteAsync($"xargs: {targetProgramName}: command not found\n", cancellationToken);
            return 127;
        }

        // Invoke the program once per token
        int firstNonZero = 0;
        foreach (var token in tokens)
        {
            // Build argv: [targetProgramName, ...fixedArgs, token]
            var argv = new List<string> { targetProgramName };
            argv.AddRange(fixedArgs);
            argv.Add(token);

            // Create a new context for this invocation
            var invocationContext = new ProgramContext(
                argv: argv,
                streams: context.Streams, // Share streams with parent
                programResolver: context.ProgramResolver,
                gameApi: context.GameApi,
                environment: context.Environment,
                terminalWidth: context.TerminalWidth,
                terminalHeight: context.TerminalHeight);

            var exitCode = await targetProgram.RunAsync(invocationContext, cancellationToken);

            // Track first non-zero exit code
            if (exitCode != 0 && firstNonZero == 0)
            {
                firstNonZero = exitCode;
            }
        }

        return firstNonZero;
    }

    private static List<string> SplitOnWhitespace(string input)
    {
        var tokens = new List<string>();
        var currentToken = new StringBuilder();

        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (currentToken.Length > 0)
                {
                    tokens.Add(currentToken.ToString());
                    currentToken.Clear();
                }
            }
            else
            {
                currentToken.Append(ch);
            }
        }

        // Add final token if present
        if (currentToken.Length > 0)
        {
            tokens.Add(currentToken.ToString());
        }

        return tokens;
    }
}
