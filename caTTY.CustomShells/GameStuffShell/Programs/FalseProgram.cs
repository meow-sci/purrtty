using caTTY.CustomShells.GameStuffShell.Execution;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Implements the 'false' command that always returns exit code 1.
/// </summary>
public sealed class FalseProgram : IProgram
{
    /// <inheritdoc/>
    public string Name => "false";

    /// <inheritdoc/>
    public Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        return Task.FromResult(1);
    }
}
