namespace purrTTY.Kfs.Testing;

/// <summary>Options of <see cref="KfsRuntime.Start"/>.</summary>
public sealed record KfsRuntimeOptions
{
    /// <summary>Passed to <see cref="KfsExtension.Install"/> (e.g. to switch the optional bench hook off).</summary>
    public KfsExtensionOptions? Extension { get; init; }
}
