using caTTY.CustomShells.GameStuffShell.Execution;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Implements the 'echo' command that writes arguments to stdout.
/// </summary>
public sealed class EchoProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "echo";

    /// <inheritdoc/>
    public async Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        var argv = context.Argv;
        var suppressNewline = false;
        var startIndex = 1;

        // Check for -n flag
        if (argv.Count > 1 && argv[1] == "-n")
        {
            suppressNewline = true;
            startIndex = 2;
        }

        // Join remaining arguments with spaces
        var output = string.Join(" ", argv.Skip(startIndex));

        // Add newline unless suppressed
        if (!suppressNewline)
        {
            output += "\n";
        }

        await context.Streams.Stdout.WriteAsync(output, cancellationToken);
        return 0;
    }
}
