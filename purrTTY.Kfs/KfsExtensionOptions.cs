namespace purrTTY.Kfs;

/// <summary>Switches for <see cref="KfsExtension.Install"/> (in the game, read from the kFS config file).</summary>
public sealed record KfsExtensionOptions
{
    /// <summary>When false, <see cref="KfsExtension.Install"/> installs nothing and reports <see cref="KfsDisabledReason.Option"/>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Names of optional patches (<see cref="KfsPatch.Required"/> false) not to apply, e.g.
    /// <c>BenchApplyRequest</c>. Names of required patches are ignored: they cannot be switched off
    /// individually. Case-sensitive.
    /// </summary>
    public IReadOnlyCollection<string> DisabledOptionalPatches { get; init; } = [];
}
