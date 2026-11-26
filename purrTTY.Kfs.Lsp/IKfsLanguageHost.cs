using KSA.Kfs.Host;
using KSA.Kfs.Host.Services;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Lsp;

/// <summary>
/// What a build is for (plan §6.1): the processor part, the vehicle layout and a name for messages.
/// Immutable: a host replaces the whole snapshot, so any thread may read it.
/// </summary>
/// <param name="Part">The part the program is built for (costs, limits, core class).</param>
/// <param name="Layout">The I/O layout (<c>&lt;vehicle.h&gt;</c>, message sizes, the command block).</param>
/// <param name="DisplayName">A name for logs and messages (<c>bench</c>, <c>vehicle 12</c>, <c>sim (default)</c>).</param>
public sealed record BuildTarget(PartDescription Part, IoLayout Layout, string DisplayName)
{
    /// <summary>
    /// True when the target is a vehicle's computer (in game: the workbench targets a vehicle), so
    /// <c>kfs.load</c> loads with <c>--vehicle</c> as the stock <c>Workbench.LoadIntoTarget</c> does.
    /// </summary>
    public bool IsVehicle { get; init; }

    /// <summary><c>DisplayName (part, layout 0x…)</c>.</summary>
    public override string ToString() => $"{DisplayName} ({Part.Name}, layout 0x{Layout.Fingerprint:X8})";
}

/// <summary>What a <c>kfs</c> verb printed (plan §6.7).</summary>
/// <param name="Ok">True when the verb ran.</param>
/// <param name="Output">Everything it printed while it ran.</param>
/// <param name="Error">Why it did not run, else null.</param>
public sealed record CommandResult(bool Ok, string Output, string? Error = null)
{
    /// <summary>A verb that could not run.</summary>
    public static CommandResult Refused(string why) => new(false, "", why);
}

/// <summary>
/// The program running on the target, for live data (plan §6.7; used from task C4 on). Every member
/// is safe to call from any thread: an in-game host reads the computer's published snapshot and its
/// locked log and trace stores.
/// </summary>
public interface ILiveProgram
{
    /// <summary>The target's name (<c>bench</c>, <c>vehicle 12</c>).</summary>
    string Name { get; }

    /// <summary>The last published machine snapshot (state, tick, overruns, tasks, trap).</summary>
    MachineSnapshot Snapshot { get; }

    /// <summary>The image currently loaded, or null (to tell whether a build is the one running).</summary>
    byte[]? LoadedImage { get; }

    /// <summary>The program log's version: it changes whenever a line is added.</summary>
    long LogVersion { get; }

    /// <summary>Copies the last <paramref name="count"/> log lines into <paramref name="into"/>; returns the log version they belong to.</summary>
    long CopyLogTail(int count, List<LogLine> into);

    /// <summary>The names of the program's <c>trace()</c> channels.</summary>
    IReadOnlyList<string> TraceChannels { get; }

    /// <summary>Copies the last <paramref name="count"/> samples of a trace channel into <paramref name="into"/>.</summary>
    void CopyTrace(string channel, int count, List<TraceSample> into);
}

/// <summary>
/// Everything the language server needs from where it runs (plan §6.1). Two hosts implement it:
/// <c>InGameLanguageHost</c> (in <c>InGame/</c>, driven each frame by the mod) and
/// <c>StandaloneLanguageHost</c> in <c>purr-kfs</c>. Every member may be called from any thread.
/// </summary>
public interface IKfsLanguageHost
{
    /// <summary>The augmented tables every build uses (in game <c>KfsIntegration.Tables</c>).</summary>
    GameTables Tables { get; }

    /// <summary>The kFS workspace folder (absolute); documents are built when they lie inside it.</summary>
    string WorkspaceRoot { get; }

    /// <summary>Part + layout + display name of the current build target: an immutable snapshot, safe from any thread.</summary>
    BuildTarget CurrentTarget { get; }

    /// <summary>Raised (on any thread) when <see cref="CurrentTarget"/> changes; the server re-diagnoses every open project.</summary>
    event Action? TargetChanged;

    /// <summary>True when <see cref="RunVerb"/> can act on a vehicle.</summary>
    bool CanControlVehicle { get; }

    /// <summary>Runs <c>kfs &lt;verb&gt;</c> where the game allows it (the main thread, UI phase) and returns what it printed.</summary>
    Task<CommandResult> RunVerb(string verb, CancellationToken ct);

    /// <summary>The running program for live data; null when there is none (always, outside the game).</summary>
    ILiveProgram? Live { get; }

    /// <summary>
    /// The secret a client must send as <c>initializationOptions.token</c> in <c>initialize</c>
    /// (plan §7.6, R12); null: no check (the stdio transport, whose peer started the process).
    /// </summary>
    string? Token { get; }
}
