using caTTY.CustomShells.GameStuffShell.Execution;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Implements the 'true' command that always returns exit code 0.
/// </summary>
public sealed class TrueProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "true";

    /// <inheritdoc/>
    public Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(0);
    }
}
