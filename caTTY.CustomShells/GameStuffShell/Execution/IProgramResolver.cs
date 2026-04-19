namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Interface for resolving program names to executable programs.
/// </summary>
public interface IProgramResolver
{
    /// <summary>
    /// Tries to resolve a program by name.
    /// </summary>
    /// <param name="name">The program name.</param>
    /// <param name="program">The resolved program, if found.</param>
    /// <returns>True if the program was found; otherwise, false.</returns>
    bool TryResolve(string name, out IProgram program);
}

/// <summary>
/// Interface for an executable program.
/// </summary>
public interface IProgram
{
    /// <summary>
    /// Gets the name of the program.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Runs the program asynchronously.
    /// </summary>
    /// <param name="context">The program execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exit code (0 for success, non-zero for failure).</returns>
    Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken);
}
