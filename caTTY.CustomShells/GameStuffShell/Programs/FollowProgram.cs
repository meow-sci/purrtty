using caTTY.CustomShells.GameStuffShell.Execution;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Follows (focuses camera on) a craft by name.
/// </summary>
public sealed class FollowProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "follow";

    public async Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        if (context.GameApi == null)
        {
            await context.Streams.Stderr.WriteAsync("follow: game API not available\n", cancellationToken);
            return 1;
        }

        if (context.Argv.Count < 2)
        {
            await context.Streams.Stderr.WriteAsync("follow: missing craft name\n", cancellationToken);
            return 2; // Usage error
        }

        var craftName = context.Argv[1];

        if (context.GameApi.TryFollowCraft(craftName, out var error))
        {
            return 0; // Success
        }
        else
        {
            await context.Streams.Stderr.WriteAsync($"follow: {error}\n", cancellationToken);
            return 1; // Runtime error
        }
    }
}
