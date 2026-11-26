using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.Live;

namespace purrTTY.Kfs.Lsp.InGame;

/// <summary>
/// The game as the in-game language host sees it: the thin layer that touches the game
/// (<c>purrTTY.GameMod/Kfs/KsaKfsGameSide.cs</c>). Every member is called on the main thread, in
/// the UI phase (<c>TerminalMod.OnAfterUi</c>), and must be cheap when nothing changed.
/// </summary>
public interface IKfsGameSide
{
    /// <summary>The kFS workspace folder (absolute). Read once per frame.</summary>
    string WorkspaceRoot { get; }

    /// <summary>Refreshes <paramref name="target"/> from the workbench's target. Once per frame.</summary>
    void RefreshTarget(TargetSnapshot target);

    /// <summary>The live program of the workbench's target (the same instance while the target computer is the same), or null. Once per frame.</summary>
    HostComputerLive? RefreshLive();

    /// <summary>Runs one <c>kfs</c> argument line as the console does.</summary>
    void ExecuteKfs(string line);

    /// <summary>Runs one <c>purr</c> argument line as the console does.</summary>
    void ExecutePurr(string line);
}

/// <summary>Settings of an <see cref="InGameLanguageHost"/>.</summary>
public sealed class InGameHostOptions
{
    /// <summary>How long a command waits for the main thread to come round (default 10 s).</summary>
    public TimeSpan VerbTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How many commands may wait for the main thread (default 8); one more is refused at once ("the game is busy").</summary>
    public int MaxPendingVerbs { get; init; } = MainThreadQueue.DefaultMaxPending;

    /// <summary>How long one frame keeps starting queued commands (default 4 ms; at least one command runs per frame; zero: exactly one).</summary>
    public TimeSpan VerbBudgetPerFrame { get; init; } = MainThreadQueue.DefaultBudget;

    /// <summary>Where the host logs (once per kind of failure).</summary>
    public LspLogSink Log { get; init; } = LspLog.ModLog;
}

/// <summary>
/// The <see cref="IKfsLanguageHost"/> inside the game (plan §6.1, §6.7). Its game access goes through
/// <see cref="IKfsGameSide"/>; everything else is here and tested offline:
/// <list type="bullet">
/// <item><see cref="CurrentTarget"/> — a <see cref="TargetSnapshot"/> refreshed by <see cref="Frame"/>.</item>
/// <item><see cref="RunVerb"/> / <see cref="RunPurr"/> — queued on a <see cref="MainThreadQueue"/>
/// drained by <see cref="Frame"/> — at least one command per frame, more only within
/// <see cref="InGameHostOptions.VerbBudgetPerFrame"/>, at most <see cref="InGameHostOptions.MaxPendingVerbs"/>
/// waiting ("the game is busy" beyond); a <c>kfs build &lt;file&gt;</c> or <c>kfs manual &lt;code&gt;</c>
/// identical to the last one still waiting joins it (<see cref="CoalesceKey"/>); the line runs inside a capture of the <see cref="Tee"/> (what it
/// printed is the result's output, also what a deferred reply printed during the call; the target is
/// refreshed right after it, before the result is returned); a bounded
/// wait (<see cref="InGameHostOptions.VerbTimeout"/>); an exception in the verb becomes a refusal,
/// never reaching the game loop; <see cref="Shutdown"/> fails what is pending.</item>
/// <item><see cref="WorkspaceRoot"/> — refreshed by <see cref="Frame"/>; a change raises
/// <see cref="WorkspaceChanged"/> (the service rewrites the server info file). Connections keep
/// the root they started with.</item>
/// <item><see cref="Live"/> / <see cref="LiveSource"/> — the workbench target's computer.</item>
/// </list>
/// <see cref="Token"/> is null: the service's listener wraps the host with its per-start token.
/// </summary>
public sealed class InGameLanguageHost : IKfsLanguageHost
{
    private readonly IKfsGameSide _game;
    private readonly InGameHostOptions _options;
    private readonly TargetSnapshot _target;
    private readonly HashSet<string> _failures = new(StringComparer.Ordinal);
    private string _root;
    private string _rawRoot;
    private HostComputerLive? _live;

    /// <summary>A host over <paramref name="tables"/> reaching the game through <paramref name="game"/>; reads the workspace root once (main thread).</summary>
    public InGameLanguageHost(GameTables tables, IKfsGameSide game, InGameHostOptions? options = null)
    {
        Tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _options = options ?? new InGameHostOptions();
        _target = new TargetSnapshot(new BuildTarget(tables.BenchPart, tables.BenchLayout, "bench"));
        _rawRoot = game.WorkspaceRoot;
        _root = Normalize(_rawRoot);
        Queue = new MainThreadQueue(_options.MaxPendingVerbs, _options.VerbBudgetPerFrame);
    }

    /// <inheritdoc/>
    public GameTables Tables { get; }

    /// <inheritdoc/>
    public string WorkspaceRoot => Volatile.Read(ref _root);

    /// <inheritdoc/>
    public BuildTarget CurrentTarget => _target.Current;

    /// <summary>The target holder (tests and <c>purr lsp</c>).</summary>
    public TargetSnapshot Target => _target;

    /// <inheritdoc/>
    public event Action? TargetChanged
    {
        add => _target.Changed += value;
        remove => _target.Changed -= value;
    }

    /// <summary>Raised on the main thread with the new root when the workspace folder changed (<c>kfs ws</c>).</summary>
    public event Action<string>? WorkspaceChanged;

    /// <inheritdoc/>
    public bool CanControlVehicle => true;

    /// <inheritdoc/>
    public ILiveProgram? Live => Volatile.Read(ref _live);

    /// <summary>The live program as the feeds read it (the same object as <see cref="Live"/>).</summary>
    public ILiveSource? LiveSource => Volatile.Read(ref _live);

    /// <inheritdoc/>
    public string? Token => null;

    /// <summary>The main-thread queue.</summary>
    public MainThreadQueue Queue { get; }

    /// <summary>The output tee whose captures collect a command's output; null: commands return no output.</summary>
    public OutputTee? Tee { get; set; }

    /// <summary>The live feed whose output buffer <see cref="Frame"/> flushes; null: none.</summary>
    public LiveFeed? Feed { get; set; }

    /// <inheritdoc/>
    public Task<CommandResult> RunVerb(string verb, CancellationToken ct) =>
        Run("kfs", verb, _game.ExecuteKfs, ct);

    /// <summary>Runs <c>purr &lt;line&gt;</c> on the main thread, as <see cref="RunVerb"/> runs <c>kfs</c>.</summary>
    public Task<CommandResult> RunPurr(string line, CancellationToken ct) =>
        Run("purr", line, _game.ExecutePurr, ct);

    /// <summary>
    /// Once per frame on the main thread, in the UI phase: the workspace root, the target, the live
    /// program, then the queued commands, then the buffered output. Never throws; allocates nothing
    /// when nothing changed and nothing is queued.
    /// </summary>
    public void Frame()
    {
        try
        {
            string root = _game.WorkspaceRoot;
            if (!ReferenceEquals(root, _rawRoot) && root is not null)
            {
                _rawRoot = root;   // the game's string: compared by reference next frame
                string normalized = Normalize(root);
                if (!string.Equals(normalized, _root, StringComparison.Ordinal))
                {
                    Volatile.Write(ref _root, normalized);
                    Raise(normalized);
                }
            }
        }
        catch (Exception e)
        {
            Once("workspace", e);
        }

        try { _game.RefreshTarget(_target); }
        catch (Exception e) { Once("target", e); }

        try
        {
            HostComputerLive? live = _game.RefreshLive();
            if (!ReferenceEquals(live, _live)) Volatile.Write(ref _live, live);
        }
        catch (Exception e)
        {
            Once("live", e);
        }

        Queue.Pump();
        try { Feed?.FlushOutput(); }
        catch (Exception e) { Once("output", e); }
    }

    /// <summary>Fails every queued command and refuses new ones (the service stopped, the mod unloads). Idempotent.</summary>
    public void Shutdown(string reason) => Queue.Shutdown(reason);

    /// <summary>
    /// The key under which a queued command joins an identical one still waiting, or null for none.
    /// Only <c>kfs build &lt;file&gt;</c> and <c>kfs manual &lt;code&gt;</c>: run twice in a row (nothing can
    /// run between two adjacent queued calls) the second changes nothing and prints the same, so both
    /// callers can share one run. Never <c>load</c>, <c>start</c>, <c>stop</c>, <c>reset</c> (each run
    /// changes the computer), <c>target</c>, <c>verb</c> or any <c>purr</c> line.
    /// </summary>
    public static string? CoalesceKey(string command, string line)
    {
        if (command != "kfs") return null;
        string trimmed = line.Trim();
        return trimmed.StartsWith("build ", StringComparison.Ordinal) || trimmed.StartsWith("manual ", StringComparison.Ordinal)
            ? "kfs " + trimmed
            : null;
    }

    private async Task<CommandResult> Run(string command, string line, Action<string> execute, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(line);
        try
        {
            return await Queue.Post(() => Execute(command, line, execute), _options.VerbTimeout, ct, CoalesceKey(command, line)).ConfigureAwait(false);
        }
        catch (MainThreadBusyException e)
        {
            return CommandResult.Refused($"{command} {line}: {e.Message}");
        }
        catch (MainThreadTimeoutException e)
        {
            return CommandResult.Refused($"{command} {line}: {e.Message}");
        }
        catch (MainThreadQueueClosedException e)
        {
            return CommandResult.Refused($"{command} {line}: {e.Message}");
        }
    }

    /// <summary>On the main thread: runs the line inside a capture; an exception is returned, never thrown.</summary>
    private CommandResult Execute(string command, string line, Action<string> execute)
    {
        OutputTee? tee = Tee;
        OutputTee.Capture? capture = tee?.BeginCapture();
        try
        {
            execute(line);
            return new CommandResult(true, capture?.Text ?? "");
        }
        catch (Exception e)
        {
            return new CommandResult(false, capture?.Text ?? "", $"{command} {line} failed: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            // A verb may change the workbench's target (kfs target vehicle): publish it before the
            // answer leaves, so the editor's next command (kfs.load decides --vehicle when it arrives,
            // on the connection's thread) sees it without waiting for the next frame.
            try { _game.RefreshTarget(_target); }
            catch (Exception e) { Once("target", e); }
            capture?.Dispose();
            try { Feed?.FlushOutput(); }
            catch (Exception e) { Once("output", e); }
        }
    }

    private void Raise(string root)
    {
        Action<string>? handlers = WorkspaceChanged;
        if (handlers is null) return;
        foreach (Action<string> handler in handlers.GetInvocationList().Cast<Action<string>>())
        {
            try { handler(root); }
            catch (Exception e) { Once("workspace handler", e); }
        }
    }

    private void Once(string what, Exception e)
    {
        lock (_failures)
        {
            if (!_failures.Add(what)) return;
        }
        _options.Log.Safe(LspLogLevel.Warning, $"in-game host: {what} failed ({e.GetType().Name}: {e.Message}); logged once");
    }

    private static string Normalize(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }
}
