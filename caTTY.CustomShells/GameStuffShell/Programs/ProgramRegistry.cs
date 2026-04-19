namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Registry for managing program resolution.
/// </summary>
public sealed class ProgramRegistry : IProgramResolver
{
    private readonly Dictionary<string, IProgram> _programs = new();

    /// <summary>
    /// Registers a program in the registry.
    /// </summary>
    /// <param name="program">The program to register.</param>
    public void Register(IProgram program)
    {
        _programs[program.Name] = program;
    }

    /// <inheritdoc/>
    public bool TryResolve(string name, out IProgram program)
    {
        return _programs.TryGetValue(name, out program!);
    }
}
