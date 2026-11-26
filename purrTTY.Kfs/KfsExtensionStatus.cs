using System.Globalization;
using System.Text;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Patches;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs;

/// <summary>Where the extension stands.</summary>
public enum KfsExtensionState
{
    /// <summary>Never installed in this process, or uninstalled.</summary>
    NotInstalled,
    /// <summary>Installed: tables published, required hooks applied, header registered.</summary>
    Active,
    /// <summary>An install was attempted and refused; nothing of it is left except possibly published tables.</summary>
    Disabled,
}

/// <summary>Why an install was refused (plan §4.5).</summary>
public enum KfsDisabledReason
{
    /// <summary>Not disabled.</summary>
    None,
    /// <summary>Switched off by <see cref="KfsExtensionOptions.Enabled"/>.</summary>
    Option,
    /// <summary>The catalogue self-check failed (step 2).</summary>
    Catalogue,
    /// <summary>A required patch target was not found (step 3).</summary>
    TargetNotFound,
    /// <summary>The game's table loader rejected the augmented tables (step 4).</summary>
    TablesRejected,
    /// <summary>Someone else's tables were already published (step 5).</summary>
    TablesAlreadyBuilt,
    /// <summary>The tables could not be published (step 5: the field write did not reach the game's accessor, or the fallback patch failed).</summary>
    TablesNotPublished,
    /// <summary>Applying a required patch failed (step 6).</summary>
    PatchFailed,
    /// <summary>Registering <c>&lt;purrtty.h&gt;</c> failed (step 7).</summary>
    HeaderFailed,
    /// <summary>An unexpected exception escaped a step.</summary>
    Unexpected,
    /// <summary>The game services could not resolve what they need (step 3; in the game: <c>KfsVehicleHost.AllHosts</c>).</summary>
    GameServicesUnavailable,
    /// <summary>Attaching the game services failed (step 10).</summary>
    GameServicesFailed,
}

/// <summary>How the augmented tables reached <c>KfsIntegration.Tables</c>.</summary>
public enum KfsTablesPublication
{
    /// <summary>Not published by this install.</summary>
    None,
    /// <summary><c>KfsIntegration._tables</c> was null and was assigned the augmented instance.</summary>
    Published,
    /// <summary><c>KfsIntegration._tables</c> already held purrTTY's augmented instance (a second install in one process).</summary>
    Reused,
    /// <summary>The field was not found; a postfix on <c>GameTables.LoadEmbedded</c> returns the augmented instance.</summary>
    Fallback,
}

/// <summary>
/// Live counters: the request/answer/apply counts aggregated over the bindings in
/// <see cref="PartsRegistry.Live"/> (an unbound vehicle's counts leave the sum), plus
/// process-wide counters of the bench hook and of <see cref="KfsLog"/>.
/// </summary>
public sealed record KfsExtensionCounters
{
    /// <summary>Live vehicle bindings.</summary>
    public int Bindings { get; init; }
    /// <summary>Requests accepted into a binding's queue.</summary>
    public long Accepted { get; init; }
    /// <summary>Requests rejected by the router (index out of range, rate limits, no binding).</summary>
    public long Rejected { get; init; }
    /// <summary>Getter results answered.</summary>
    public long Answered { get; init; }
    /// <summary>Commands applied to parts by the pump.</summary>
    public long Applied { get; init; }
    /// <summary>Commands whose application threw.</summary>
    public long ApplyFailures { get; init; }
    /// <summary>Queued commands discarded because their part left the vehicle before the pump applied them.</summary>
    public long Discarded { get; init; }
    /// <summary>Commands dropped because a queue was full.</summary>
    public long Dropped { get; init; }
    /// <summary>Exceptions caught inside the router.</summary>
    public long RouterFaults { get; init; }
    /// <summary>Requests the bench hook claimed (process-wide).</summary>
    public long BenchClaimed { get; init; }
    /// <summary>Of those, setters it rejected with the bench text.</summary>
    public long BenchRejected { get; init; }
    /// <summary>Failures recorded through <see cref="KfsLog.Once"/> (process-wide, every kind).</summary>
    public long LoggedFailures { get; init; }
    /// <summary>Times the <c>BeforeInputEvents</c> hook ran (process-wide; one per frame in the game, 0 offline).</summary>
    public long PumpFrames { get; init; }

    /// <summary>The counters as they are now.</summary>
    public static KfsExtensionCounters Capture()
    {
        var c = new KfsExtensionCounters();
        long accepted = 0, rejected = 0, answered = 0, applied = 0, failures = 0, discarded = 0, dropped = 0, faults = 0;
        IReadOnlyList<VehicleBinding> live = PartsRegistry.Live;
        foreach (VehicleBinding b in live)
        {
            accepted += b.Accepted;
            rejected += b.Rejected;
            answered += b.Answered;
            applied += b.Applied;
            failures += b.ApplyFailures;
            discarded += b.Discarded;
            dropped += b.Dropped;
            faults += b.RouterFaults;
        }
        return c with
        {
            Bindings = live.Count,
            Accepted = accepted,
            Rejected = rejected,
            Answered = answered,
            Applied = applied,
            ApplyFailures = failures,
            Discarded = discarded,
            Dropped = dropped,
            RouterFaults = faults,
            BenchClaimed = BenchApplyRequestPatch.Claimed,
            BenchRejected = BenchApplyRequestPatch.Rejected,
            LoggedFailures = KfsLog.Total,
            PumpFrames = BeforeInputEventsPatch.Calls,
        };
    }
}

/// <summary>
/// What an install did (plan §4.15): active or the reason it was refused, which hooks applied,
/// how the tables were published, the services and their id range, and live counters. Immutable:
/// <see cref="KfsExtension.Status"/> returns a fresh one with the counters of that moment.
/// </summary>
public sealed record KfsExtensionStatus
{
    /// <summary>The prefix of every status line.</summary>
    public const string LinePrefix = "purrTTY kFS extension: ";

    /// <summary>Active, disabled or not installed.</summary>
    public KfsExtensionState State { get; init; }

    /// <summary>Why the install was refused; <see cref="KfsDisabledReason.None"/> unless <see cref="State"/> is <see cref="KfsExtensionState.Disabled"/>.</summary>
    public KfsDisabledReason Reason { get; init; }

    /// <summary>The disabled reason in words (<c>catalogue</c>, <c>target … not found</c>, <c>tables rejected (…)</c>, …); empty otherwise.</summary>
    public string Message { get; init; } = "";

    /// <summary>The required patches applied, by name, in registry order.</summary>
    public IReadOnlyList<string> RequiredPatches { get; init; } = [];

    /// <summary>The optional patches applied, by name.</summary>
    public IReadOnlyList<string> OptionalPatches { get; init; } = [];

    /// <summary>Optional patches not applied, as <c>name: why</c> (switched off, target missing, apply failed).</summary>
    public IReadOnlyList<string> OptionalPatchesSkipped { get; init; } = [];

    /// <summary>How the augmented tables were published.</summary>
    public KfsTablesPublication Tables { get; init; }

    /// <summary>The number of services in the catalogue.</summary>
    public int ServiceCount { get; init; }

    /// <summary>The lowest service id of the catalogue.</summary>
    public ushort FirstId { get; init; }

    /// <summary>The highest service id of the catalogue.</summary>
    public ushort LastId { get; init; }

    /// <summary>The counters when this status was taken.</summary>
    public KfsExtensionCounters Counters { get; init; } = new();

    /// <summary>The name of the game services attached (<c>game back end</c>), or null when installed without (the offline test runtime).</summary>
    public string? GameServices { get; init; }

    /// <summary>
    /// Why the stock kFS editor's build state cannot be read (a game member the probe looks up by name
    /// was not found), or null when it can be or no game services are installed. While set, every
    /// uninstall leaves <c>&lt;purrtty.h&gt;</c> registered (the safe answer; the next install takes it over).
    /// </summary>
    public string? EditorProbe { get; init; }

    /// <summary>The <c>purr</c> console command (install step 9); <see cref="PurrCommandState.None"/> without a console host.</summary>
    public PurrCommandState ConsoleCommand { get; init; }

    /// <summary>Why registering the <c>purr</c> command failed (<see cref="PurrCommandState.Failed"/>); empty otherwise.</summary>
    public string ConsoleMessage { get; init; } = "";

    /// <summary>True when the extension is installed and working.</summary>
    public bool IsActive => State == KfsExtensionState.Active;

    /// <summary>Nothing installed yet.</summary>
    public static KfsExtensionStatus NotInstalled { get; } = new();

    /// <summary>A refused install.</summary>
    public static KfsExtensionStatus Disabled(KfsDisabledReason reason, string message) =>
        new() { State = KfsExtensionState.Disabled, Reason = reason, Message = message };

    /// <summary>
    /// The one log line: <c>purrTTY kFS extension: active — 22 services 0x5000–0x5033, 3 hooks (+1 optional), tables published, game back end, purr command</c>
    /// (the game services and the console command only when installed with them; a failed
    /// registration reads <c>, no purr command (reason)</c>),
    /// <c>purrTTY kFS extension: disabled — target VehicleComputer.ApplyRequest not found</c>, or
    /// <c>purrTTY kFS extension: not installed</c>.
    /// </summary>
    public override string ToString() => State switch
    {
        KfsExtensionState.Active => string.Create(CultureInfo.InvariantCulture,
            $"{LinePrefix}active — {ServiceCount} services 0x{FirstId:X4}–0x{LastId:X4}, {RequiredPatches.Count} hooks (+{OptionalPatches.Count} optional), tables {TablesWord(Tables)}{(GameServices is null ? "" : ", " + GameServices)}{ConsoleWords()}"),
        KfsExtensionState.Disabled => $"{LinePrefix}disabled — {Message}",
        _ => $"{LinePrefix}not installed",
    };

    /// <summary>
    /// A short form for the purrTTY menu and the <c>purr</c> command:
    /// <c>active — 22 services</c>, <c>disabled — switched off</c>, <c>not installed</c>.
    /// </summary>
    public string Summary => State switch
    {
        KfsExtensionState.Active => string.Create(CultureInfo.InvariantCulture, $"active — {ServiceCount} services"),
        KfsExtensionState.Disabled => $"disabled — {Message}",
        _ => "not installed",
    };

    private string ConsoleWords() => ConsoleCommand switch
    {
        PurrCommandState.Registered or PurrCommandState.Kept or PurrCommandState.Standing => ", purr command",
        PurrCommandState.Failed => $", no purr command ({ConsoleMessage})",
        _ => "",
    };

    private string ConsoleLine() => ConsoleCommand switch
    {
        PurrCommandState.Registered => "purr registered",
        PurrCommandState.Kept => "purr kept from an earlier install (that console could not remove it)",
        PurrCommandState.Standing => "purr registered by purrTTY for as long as the mod is loaded (it stays when the extension is off)",
        PurrCommandState.Failed => $"purr not registered ({ConsoleMessage}); the purrTTY menu still runs it",
        _ => "none (installed without a game console)",
    };

    /// <summary>A multi-line description (for <c>purr status</c>).</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.Append(ToString()).Append('\n');
        if (State == KfsExtensionState.Active)
        {
            text.Append("  hooks:    ").Append(Join(RequiredPatches)).Append('\n');
            text.Append("  optional: ").Append(Join(OptionalPatches)).Append('\n');
            if (OptionalPatchesSkipped.Count > 0) text.Append("  skipped:  ").Append(string.Join("; ", OptionalPatchesSkipped)).Append('\n');
            text.Append("  tables:   ").Append(TablesWord(Tables)).Append('\n');
            text.Append("  back end: ").Append(GameServices ?? "none (vehicles are bound by the caller)").Append('\n');
            text.Append("  console:  ").Append(ConsoleLine()).Append('\n');
            if (EditorProbe is not null)
                text.Append("  editor:   build state unreadable (").Append(EditorProbe).Append("); <purrtty.h> stays registered at uninstall\n");
        }
        KfsExtensionCounters c = Counters;
        text.Append(CultureInfo.InvariantCulture,
            $"  vehicles: {c.Bindings} bound; requests {c.Accepted} accepted, {c.Rejected} rejected; {c.Answered} answered\n");
        text.Append(CultureInfo.InvariantCulture,
            $"  commands: {c.Applied} applied, {c.ApplyFailures} failed, {c.Discarded} discarded, {c.Dropped} dropped; {c.RouterFaults} router faults\n");
        text.Append(CultureInfo.InvariantCulture,
            $"  bench:    {c.BenchClaimed} claimed, {c.BenchRejected} rejected; pump frames {c.PumpFrames}; logged failures {c.LoggedFailures}");
        return text.ToString();
    }

    private static string Join(IReadOnlyList<string> names) => names.Count == 0 ? "none" : string.Join(", ", names);

    private static string TablesWord(KfsTablesPublication p) => p switch
    {
        KfsTablesPublication.Published => "published",
        KfsTablesPublication.Reused => "reused",
        KfsTablesPublication.Fallback => "published through the LoadEmbedded fallback",
        _ => "not published",
    };
}
