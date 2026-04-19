using caTTY.CustomShells.GameStuffShell.Execution;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Implements the 'exit' command that returns a specified exit code.
/// Usage: exit [N]
/// Returns exit code N, or 0 if N is omitted.
/// </summary>
public sealed class ExitProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "exit";

    /// <inheritdoc/>
    public Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        var argv = context.Argv;

        // Default exit code is 0
        if (argv.Count < 2)
        {
            return Task.FromResult(0);
        }

        // Parse exit code
        if (!int.TryParse(argv[1], out var exitCode))
        {
            // Like bash, non-numeric argument results in exit code 2
            context.Streams.Stderr.WriteAsync($"exit: {argv[1]}: numeric argument required\n", cancellationToken)
                .GetAwaiter().GetResult();
            return Task.FromResult(2);
        }

        return Task.FromResult(exitCode);
    }
}
