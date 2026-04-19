using caTTY.CustomShells.GameStuffShell.Execution;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Implements the 'sleep' command that delays for a specified number of milliseconds.
/// </summary>
public sealed class SleepProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "sleep";

    /// <inheritdoc/>
    public async Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        var argv = context.Argv;

        // Check for argument
        if (argv.Count < 2)
        {
            await context.Streams.Stderr.WriteAsync("sleep: missing operand\n", cancellationToken);
            return 2;
        }

        // Parse milliseconds
        if (!int.TryParse(argv[1], out var milliseconds) || milliseconds < 0)
        {
            await context.Streams.Stderr.WriteAsync($"sleep: invalid time interval '{argv[1]}'\n", cancellationToken);
            return 2;
        }

        // Sleep with cancellation support
        try
        {
            await Task.Delay(milliseconds, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not an error for sleep
            return 0;
        }
    }
}
