using caTTY.CustomShells.GameStuffShell.Execution;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Lists all craft names, one per line.
/// </summary>
public sealed class CraftsProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "crafts";

    public async Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        if (context.GameApi == null)
        {
            await context.Streams.Stderr.WriteAsync("crafts: game API not available\n", cancellationToken);
            return 1;
        }

        var craftNames = context.GameApi.GetCraftNames();

        foreach (var name in craftNames)
        {
            await context.Streams.Stdout.WriteAsync($"{name}\n", cancellationToken);
        }

        return 0;
    }
}
