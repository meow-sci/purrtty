using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Lsp;

/// <summary>
/// A plain <see cref="IKfsLanguageHost"/>: fixed tables and workspace, a target the owner replaces
/// with <see cref="SetTarget"/> (which raises <see cref="TargetChanged"/>), no vehicle and no live
/// program. <c>StandaloneLanguageHost</c> builds on it; tests use it directly.
/// </summary>
public class KfsLanguageHost : IKfsLanguageHost
{
    private BuildTarget _target;

    /// <summary>A host over <paramref name="tables"/> and <paramref name="workspaceRoot"/> (made absolute).</summary>
    public KfsLanguageHost(GameTables tables, string workspaceRoot, BuildTarget target, string? token = null)
    {
        Tables = tables ?? throw new ArgumentNullException(nameof(tables));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        WorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        Token = token;
    }

    /// <summary>A host for the tables' bench part and bench layout.</summary>
    public static KfsLanguageHost ForBench(GameTables tables, string workspaceRoot, string? token = null) =>
        new(tables, workspaceRoot, new BuildTarget(tables.BenchPart, tables.BenchLayout, "bench"), token);

    /// <inheritdoc/>
    public GameTables Tables { get; }

    /// <inheritdoc/>
    public string WorkspaceRoot { get; }

    /// <inheritdoc/>
    public BuildTarget CurrentTarget => Volatile.Read(ref _target);

    /// <inheritdoc/>
    public event Action? TargetChanged;

    /// <summary>How many handlers <see cref="TargetChanged"/> has (tests: a closed server leaves none behind).</summary>
    internal int TargetChangedHandlers => TargetChanged?.GetInvocationList().Length ?? 0;

    /// <inheritdoc/>
    public virtual bool CanControlVehicle => false;

    /// <inheritdoc/>
    public virtual ILiveProgram? Live => null;

    /// <inheritdoc/>
    public string? Token { get; }

    /// <summary>Replaces the target and raises <see cref="TargetChanged"/> (handlers' exceptions are swallowed).</summary>
    public void SetTarget(BuildTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Volatile.Write(ref _target, target);
        Action? handlers = TargetChanged;
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch { /* one subscriber must not stop the others */ }
        }
    }

    /// <inheritdoc/>
    public virtual Task<CommandResult> RunVerb(string verb, CancellationToken ct) =>
        Task.FromResult(CommandResult.Refused("this language server has no game to run kfs commands in"));
}
