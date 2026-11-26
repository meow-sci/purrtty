namespace purrTTY.Kfs.Testing;

/// <summary>The computer's state (the game's <c>ComputerState</c>, in a type tests may use anywhere).</summary>
public enum SimComputerState
{
    /// <summary>No image loaded.</summary>
    NoSoftware,
    /// <summary>Loaded, not started (or stopped).</summary>
    Standby,
    /// <summary>Running its init tasks.</summary>
    Boot,
    /// <summary>Running.</summary>
    Running,
    /// <summary>Started, with nothing left to run.</summary>
    Idle,
    /// <summary>Halted: a trap, <c>kfs_halt()</c> or the host. Ticks are still taken, but no code runs and commands are held.</summary>
    Halted,
}

/// <summary>A trap, located with the program's debug information.</summary>
/// <param name="Code">The trap code's name (<c>DIV_ZERO</c>, <c>BOUNDS</c>, <c>NULL_ACCESS</c> …).</param>
/// <param name="Pc">The program counter.</param>
/// <param name="Task">The task index.</param>
/// <param name="Function">The function, or <c>?</c>.</param>
/// <param name="File">The source file, or null without line information.</param>
/// <param name="Line">The 1-based line, or 0.</param>
/// <param name="UserCode">The user code of a <c>USER</c> trap.</param>
public sealed record SimTrap(string Code, uint Pc, int Task, string Function, string? File, int Line, byte UserCode)
{
    /// <summary><c>DIV_ZERO in run (main.c:12)</c>.</summary>
    public override string ToString() => $"{Code} in {Function}" + (File is null ? "" : $" ({File}:{Line})");
}

/// <summary>A kC or kASM build failed; <see cref="Exception.Message"/> holds every diagnostic, one per line.</summary>
public sealed class KfsBuildException : InvalidOperationException
{
    /// <summary>A failed build of <paramref name="language"/> (kC or kASM).</summary>
    public KfsBuildException(string language, BuildResult build)
        : base($"{language} build failed:\n{build?.Text}")
    {
        ArgumentNullException.ThrowIfNull(build);
        Build = build;
    }

    /// <summary>The failed build.</summary>
    public BuildResult Build { get; }

    /// <summary>The compiler's or assembler's diagnostics.</summary>
    public IReadOnlyList<BuildDiagnostic> Diagnostics => Build.Diagnostics;
}
