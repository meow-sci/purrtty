using System.Reflection;
using System.Text.Json.Nodes;
using HarmonyLib;
using KSA.Kfs;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Patches;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tables;

namespace purrTTY.Kfs;

/// <summary>
/// The public entry of purrTTY's kFS extension (plan §4.5): installs the augmented tables, the host
/// hooks and <c>&lt;purrtty.h&gt;</c> all-or-nothing, and removes them again. Nothing throws out of
/// <see cref="Install"/> or <see cref="Uninstall"/>: every failure becomes a
/// <see cref="KfsExtensionStatus"/>, and a refused install leaves no header, no builtin names and
/// no patch of its own behind (published tables may stay: with no hooks their rows only reject).
/// Process-wide; calls are serialised.
/// </summary>
public static class KfsExtension
{
    private static readonly object Gate = new();
    private static KfsExtensionStatus _status = KfsExtensionStatus.NotInstalled;
    private static Installation? _installed;

    /// <summary>
    /// The patch registry, in apply order. Required entries make the install all-or-nothing; optional
    /// ones are applied after the header and may fail without consequence. To add a hook, add one
    /// entry here.
    /// </summary>
    /// <remarks>
    /// <c>BeforeInputEvents</c> is required in every install, the offline test runtime's included:
    /// its target exists in <c>KSA.dll</c> wherever the extension can load, and offline (never
    /// called by a game, no active pump) the postfix does nothing.
    /// </remarks>
    public static IReadOnlyList<KfsPatch> Patches { get; } =
    [
        new("ApplyRequest", "VehicleComputer.ApplyRequest", Required: true,
            ApplyRequestPatch.TargetMethod, (h, t) => ApplyRequestPatch.Apply(h, t), ApplyRequestPatch.Remove),
        new("IssueResult", "VehicleComputer ISyscallHost.IssueResult", Required: true,
            IssueResultPatch.TargetMethod, (h, t) => IssueResultPatch.Apply(h, t), IssueResultPatch.Remove),
        new("BeforeInputEvents", "KfsIntegration.BeforeInputEvents", Required: true,
            BeforeInputEventsPatch.TargetMethod, (h, t) => BeforeInputEventsPatch.Apply(h, t), BeforeInputEventsPatch.Remove),
        new("BenchApplyRequest", "HostComputer.ApplyRequest", Required: false,
            BenchApplyRequestPatch.TargetMethod, (h, t) => BenchApplyRequestPatch.Apply(h, t), BenchApplyRequestPatch.Remove),
    ];

    /// <summary>The current status, with the counters of this moment.</summary>
    public static KfsExtensionStatus Status
    {
        get
        {
            KfsExtensionStatus status;
            lock (Gate) status = _status;
            return status with { Counters = KfsExtensionCounters.Capture() };
        }
    }

    /// <summary>True while installed.</summary>
    public static bool IsActive
    {
        get { lock (Gate) return _installed is not null; }
    }

    /// <summary>
    /// <see cref="KfsExtensionStatus.Summary"/> of the current status (<c>active — 22 services</c>,
    /// <c>disabled — switched off</c>, <c>not installed</c>), computed when the status changes, so a
    /// menu can show it every frame without allocating. Any thread.
    /// </summary>
    public static string StatusSummary => Volatile.Read(ref _summary);

    private static string _summary = KfsExtensionStatus.NotInstalled.Summary;

    /// <summary>
    /// Why the last uninstall left <c>&lt;purrtty.h&gt;</c> registered (a compile may have been reading
    /// the game's header table), while it still is; null otherwise. Any thread.
    /// </summary>
    public static string? HeaderLeftRegistered => SystemHeaderRegistration.LeftInPlace;

    private static void SetStatus(KfsExtensionStatus status)
    {
        _status = status;
        Volatile.Write(ref _summary, status.Summary);
    }

    /// <summary>
    /// Installs the extension with <paramref name="harmony"/> (plan §4.5) and logs one status line.
    /// When already active, returns the current status and does nothing else.
    /// </summary>
    /// <param name="harmony">The Harmony instance that owns the hooks.</param>
    /// <param name="options">The switches (in the game, from <c>kfs.toml</c>); null for the defaults.</param>
    /// <param name="gameServices">
    /// The game-only part (in the game <see cref="Game.KsaGameServices"/>: host discovery, the
    /// per-frame pump, the dispatch-thread fallback, restoring light templates); part of the
    /// all-or-nothing install. Null in the offline test runtime, which binds its vehicles itself.
    /// </param>
    public static KfsExtensionStatus Install(Harmony harmony, KfsExtensionOptions? options = null, IKfsGameServices? gameServices = null) =>
        Install(harmony, options, InstallSeams.Default, gameServices);

    /// <summary>As <see cref="Install(Harmony, KfsExtensionOptions?, IKfsGameServices?)"/>, with parts of the sequence substituted (tests).</summary>
    internal static KfsExtensionStatus Install(Harmony harmony, KfsExtensionOptions? options, InstallSeams seams, IKfsGameServices? gameServices = null)
    {
        lock (Gate)
        {
            if (_installed is not null) return Status;

            KfsExtensionStatus status;
            try
            {
                status = harmony is null || seams is null
                    ? KfsExtensionStatus.Disabled(KfsDisabledReason.Unexpected, "no Harmony instance")
                    : TryInstall(harmony, options ?? new KfsExtensionOptions(), seams, gameServices);
            }
            catch (Exception e)
            {
                // TryInstall undoes its own work before returning a refusal; reaching here means a
                // bug in that bookkeeping. Report it rather than throw into the mod loader.
                status = KfsExtensionStatus.Disabled(KfsDisabledReason.Unexpected, $"install failed ({e.GetType().Name}: {e.Message})");
            }

            SetStatus(status);
            try { (seams?.Log ?? KfsLog.Info)(status.ToString()); }
            catch { /* logging never fails an install */ }
            return Status;
        }
    }

    /// <summary>
    /// Stops the game services (pump, fallback; private light templates restored), removes the
    /// hooks, <c>&lt;purrtty.h&gt;</c> and the builtin names, closes the registry and unbinds every
    /// vehicle. The published tables stay for the life of the process; a later
    /// <see cref="Install(Harmony, KfsExtensionOptions?, IKfsGameServices?)"/> reuses them. Does nothing when not installed.
    /// </summary>
    public static void Uninstall()
    {
        lock (Gate)
        {
            Installation? installed = _installed;
            _installed = null;
            SetStatus(KfsExtensionStatus.NotInstalled);
            if (installed is null) return;

            installed.Undo();
            try
            {
                PartsRegistry.Fallback = null;
                PartsRegistry.Close();   // no bind after this point, not even from a dispatch thread mid-call
            }
            catch (Exception e)
            {
                KfsLog.Once("uninstall bindings", e);
            }
        }
    }

    // ------------------------------------------------------------------ the sequence

    private static KfsExtensionStatus TryInstall(Harmony harmony, KfsExtensionOptions options, InstallSeams seams, IKfsGameServices? gameServices)
    {
        ServiceCatalog catalog = seams.Catalog;

        // 1. Switched off.
        if (!options.Enabled) return KfsExtensionStatus.Disabled(KfsDisabledReason.Option, "switched off");

        // 2. Catalogue self-check, including "absent from the stock table".
        (ushort[] stockIds, string[] stockEnumKeys) = StockKeys(seams.OpenStock);
        IReadOnlyList<CatalogProblem> problems = CatalogSelfCheck.Run(catalog, stockIds, stockEnumKeys);
        if (problems.Count > 0)
        {
            string more = problems.Count > 1 ? $" (+{problems.Count - 1} more)" : "";
            return KfsExtensionStatus.Disabled(KfsDisabledReason.Catalogue, $"catalogue: {problems[0]}{more}");
        }

        // 3. Every required target, before anything is changed.
        var required = new List<(KfsPatch Patch, MethodBase Target)>();
        foreach (KfsPatch patch in Patches.Where(p => p.Required))
        {
            MethodBase? target = Resolve(seams, patch);
            if (target is null) return KfsExtensionStatus.Disabled(KfsDisabledReason.TargetNotFound, $"target {patch.Target} not found");
            required.Add((patch, target));
        }

        //    ... and every capability of the game services, equally required.
        if (gameServices is not null)
        {
            string? why;
            try
            {
                why = gameServices.Prepare();
            }
            catch (Exception e)
            {
                why = $"{e.GetType().Name}: {e.Message}";
            }
            if (why is not null) return KfsExtensionStatus.Disabled(KfsDisabledReason.GameServicesUnavailable, $"{GameServicesName(gameServices)} unavailable ({why})");
        }

        // 4. Dry run: the game's own loader over the augmented rows.
        GameTables built;
        try
        {
            built = seams.BuildTables(seams.OpenStock, catalog);
        }
        catch (Exception e)
        {
            return KfsExtensionStatus.Disabled(KfsDisabledReason.TablesRejected, $"tables rejected ({e.Message})");
        }

        // From here on every step changes process-wide state. Any exception, expected or not,
        // undoes everything this install did (published tables excepted: with no hooks their rows
        // only reject, and a later install reuses them) before the refusal is reported.
        var installation = new Installation(harmony);
        try
        {
            KfsExtensionStatus status = InstallFrom(harmony, options, seams, catalog, required, built, installation, gameServices);
            if (!status.IsActive) installation.Undo();
            return status;
        }
        catch (Exception e)
        {
            installation.Undo();
            return KfsExtensionStatus.Disabled(KfsDisabledReason.Unexpected,
                $"install failed ({e.GetType().Name}: {e.Message}); everything it changed was undone");
        }
    }

    /// <summary>Steps 5–10. A refusal returned from here is undone by the caller; so is an exception.</summary>
    private static KfsExtensionStatus InstallFrom(Harmony harmony, KfsExtensionOptions options, InstallSeams seams, ServiceCatalog catalog,
        List<(KfsPatch Patch, MethodBase Target)> required, GameTables built, Installation installation, IKfsGameServices? gameServices)
    {
        // 5. Publish.
        KfsTablesPublication publication;
        TablesPublishResult published = TablesPublisher.Publish(built, seams.FindTablesField);
        switch (published.Outcome)
        {
            case TablesPublishOutcome.Published:
                publication = KfsTablesPublication.Published;
                break;
            case TablesPublishOutcome.Reused:
                // Ours only when built from exactly this catalogue: the origin carries a hash of
                // every emitted row and enumeration (TableAugmenter.OriginFor). Same ids and names
                // with a widened selector, another cell layout, kind, cost class or enumeration
                // list would otherwise be "reused", and the new header compile against old rows.
                string expected = TableAugmenter.OriginFor(catalog);
                if (published.Tables!.Origin != expected)
                {
                    return KfsExtensionStatus.Disabled(KfsDisabledReason.TablesAlreadyBuilt,
                        $"tables already built (purrTTY tables of another catalogue: origin {published.Tables.Origin}, this build is {expected})");
                }
                publication = KfsTablesPublication.Reused;
                break;
            case TablesPublishOutcome.Foreign:
                return KfsExtensionStatus.Disabled(KfsDisabledReason.TablesAlreadyBuilt,
                    $"tables already built (origin {published.Tables?.Origin})");
            case TablesPublishOutcome.NotVisible:
                return KfsExtensionStatus.Disabled(KfsDisabledReason.TablesNotPublished,
                    "tables not published (KfsIntegration.Tables did not return the assigned instance)");
            default:
                // Field not found: the LoadEmbedded postfix of spike 03. "Already built" cannot be
                // read from the field, so it is checked through the accessor afterwards.
                MethodBase? loadEmbedded = Resolve(seams, LoadEmbeddedFallback);
                if (loadEmbedded is null)
                {
                    return KfsExtensionStatus.Disabled(KfsDisabledReason.TablesNotPublished,
                        $"tables not published (field KfsIntegration.{TablesPublisher.FieldName} and target {LoadEmbeddedFallback.Target} not found)");
                }
                try
                {
                    LoadEmbeddedFallbackPatch.Apply(harmony, loadEmbedded, built);
                }
                catch (Exception e)
                {
                    installation.TryRemove(LoadEmbeddedFallback, loadEmbedded);   // Harmony may have recorded it before failing
                    return KfsExtensionStatus.Disabled(KfsDisabledReason.TablesNotPublished,
                        $"tables not published (fallback patch on {LoadEmbeddedFallback.Target} failed: {e.Message})");
                }
                installation.Applied.Add((LoadEmbeddedFallback, loadEmbedded));
                // The game's accessor is LazyInitializer.EnsureInitialized(ref _tables, LoadEmbedded):
                // it hands out the postfix's instance only if nobody built the tables before us.
                // Anything else is a half-install (header and hooks against the stock tables).
                GameTables visible = KfsIntegration.Tables;
                if (!ReferenceEquals(visible, built))
                {
                    return KfsExtensionStatus.Disabled(KfsDisabledReason.TablesAlreadyBuilt,
                        $"tables already built (KfsIntegration.Tables kept origin {visible.Origin}; the {LoadEmbeddedFallback.Target} fallback was removed)");
                }
                publication = KfsTablesPublication.Fallback;
                break;
        }
        seams.Step?.Invoke("published");

        // 6. Required patches, one by one; the first failure undoes the others.
        foreach (var (patch, target) in required)
        {
            try
            {
                patch.Apply(harmony, target);
            }
            catch (Exception e)
            {
                installation.TryRemove(patch, target);   // Harmony may have recorded it before failing
                return KfsExtensionStatus.Disabled(KfsDisabledReason.PatchFailed, $"patch {patch.Name} on {patch.Target} failed ({e.Message})");
            }
            installation.Applied.Add((patch, target));
            seams.Step?.Invoke("required " + patch.Name);
        }

        // 7. The header and the builtin names.
        try
        {
            installation.Header = seams.RegisterHeader(CatalogEmitter.Header(catalog), CatalogEmitter.BuiltinNames(catalog));
        }
        catch (Exception e)
        {
            return KfsExtensionStatus.Disabled(KfsDisabledReason.HeaderFailed, $"header not registered ({e.Message})");
        }
        seams.Step?.Invoke("header");

        // 8. Optional patches: a failure is logged and ignored.
        IReadOnlyCollection<string> disabled = options.DisabledOptionalPatches ?? [];
        var optional = new List<string>();
        var skipped = new List<string>();
        foreach (KfsPatch patch in Patches.Where(p => !p.Required))
        {
            if (disabled.Contains(patch.Name))
            {
                skipped.Add($"{patch.Name}: switched off");
                continue;
            }
            MethodBase? target = Resolve(seams, patch);
            if (target is null)
            {
                skipped.Add($"{patch.Name}: target {patch.Target} not found");
                continue;
            }
            try
            {
                patch.Apply(harmony, target);
                installation.Applied.Add((patch, target));
                optional.Add(patch.Name);
            }
            catch (Exception e)
            {
                installation.TryRemove(patch, target);
                KfsLog.Once($"optional patch {patch.Name}", e);
                skipped.Add($"{patch.Name}: {e.Message}");
            }
        }
        seams.Step?.Invoke("optional");

        // 9. The `purr` console command (plan §4.13), when the game services bring a console. A
        //    failure to register is logged and reported in the status, like an optional patch: it
        //    never undoes the install, and the host stays attached so the purrTTY menu still runs
        //    the verbs. A later failure undoes it with the rest (the installation unregisters).
        PurrCommandState consoleState = PurrCommandState.None;
        string consoleMessage = "";
        IKfsConsoleHost? console = gameServices is null ? null : ConsoleOf(gameServices);
        if (console is not null)
        {
            consoleState = PurrConsole.Attach(console, out consoleMessage);
            installation.Console = console;
            installation.ConsoleState = consoleState;
            seams.Step?.Invoke("console");
        }

        // 10. The game services, as the last step that can fail: from here on the pump binds the
        //     game's hosts and the fallback binds from dispatch threads (both refuse while the
        //     registry is still closed, i.e. until the next line but one). A failure undoes the
        //     install; so does any later exception (the installation detaches them).
        if (gameServices is not null)
        {
            installation.GameServices = gameServices;
            try
            {
                gameServices.Attach();
            }
            catch (Exception e)
            {
                return KfsExtensionStatus.Disabled(KfsDisabledReason.GameServicesFailed,
                    $"{GameServicesName(gameServices)} not attached ({e.GetType().Name}: {e.Message})");
            }
            seams.Step?.Invoke("game services");
        }

        var status = new KfsExtensionStatus
        {
            State = KfsExtensionState.Active,
            RequiredPatches = required.Select(r => r.Patch.Name).ToArray(),
            OptionalPatches = optional,
            OptionalPatchesSkipped = skipped,
            Tables = publication,
            ServiceCount = catalog.Services.Count,
            FirstId = catalog.Services.Count == 0 ? (ushort)0 : catalog.Services.Min(s => s.Id),
            LastId = catalog.Services.Count == 0 ? (ushort)0 : catalog.Services.Max(s => s.Id),
            GameServices = gameServices is null ? null : GameServicesName(gameServices),
            EditorProbe = gameServices is null ? null : StockEditorProbe.Problem,
            ConsoleCommand = consoleState,
            ConsoleMessage = consoleMessage,
        };
        // Last, after everything that can fail: bindings may be created from now on.
        PartsRegistry.Open();
        _installed = installation;
        return status;
    }

    /// <summary>The fallback's descriptor (not in <see cref="Patches"/>: applied only when the field is not found).</summary>
    internal static KfsPatch LoadEmbeddedFallback { get; } = new("LoadEmbeddedFallback", "GameTables.LoadEmbedded", Required: false,
        LoadEmbeddedFallbackPatch.TargetMethod,
        static (_, _) => throw new InvalidOperationException("applied with its tables by the installer"),
        LoadEmbeddedFallbackPatch.Remove);

    private static IKfsConsoleHost? ConsoleOf(IKfsGameServices services)
    {
        try
        {
            return services.Console;
        }
        catch (Exception e)
        {
            KfsLog.Once("game services console", e);
            return null;
        }
    }

    private static string GameServicesName(IKfsGameServices services)
    {
        try
        {
            return string.IsNullOrWhiteSpace(services.Name) ? "game services" : services.Name;
        }
        catch
        {
            return "game services";
        }
    }

    private static MethodBase? Resolve(InstallSeams seams, KfsPatch patch)
    {
        try
        {
            return (seams.TargetResolvers is { } resolvers && resolvers.TryGetValue(patch.Name, out var resolve) ? resolve : patch.ResolveTarget)();
        }
        catch
        {
            return null;   // a resolver that throws found nothing
        }
    }

    /// <summary>The stock service ids and enumeration keys, read from the stock JSON (no table build).</summary>
    private static (ushort[] Ids, string[] EnumKeys) StockKeys(Func<string, Stream?> open)
    {
        JsonNode? services = ReadJson(open, TableAugmenter.ServicesFile);
        JsonNode? enums = ReadJson(open, TableAugmenter.EnumsFile);
        ushort[] ids = (services?["services"] as JsonArray ?? new JsonArray())
            .Select(r => r?["id"] is JsonValue v && v.TryGetValue(out int id) ? id : -1)
            .Where(id => id is >= 0 and <= ushort.MaxValue)
            .Select(id => (ushort)id)
            .ToArray();
        string[] keys = (enums?["enums"] as JsonObject)?.Select(kv => kv.Key).ToArray() ?? [];
        return (ids, keys);
    }

    private static JsonNode? ReadJson(Func<string, Stream?> open, string file)
    {
        try
        {
            using Stream? stream = open(file);
            return stream is null ? null : JsonNode.Parse(stream);
        }
        catch
        {
            return null;   // unreadable: the dry run (step 4) reports it with the loader's message
        }
    }

    // ------------------------------------------------------------------ state

    /// <summary>What a successful (or in-progress) install changed, so it can be undone exactly.</summary>
    private sealed class Installation(Harmony harmony)
    {
        public Harmony Harmony { get; } = harmony;
        public List<(KfsPatch Patch, MethodBase Target)> Applied { get; } = [];
        public SystemHeaderRegistration? Header { get; set; }
        public IKfsGameServices? GameServices { get; set; }
        public IKfsConsoleHost? Console { get; set; }
        public PurrCommandState ConsoleState { get; set; }

        /// <summary>Removes the console command, detaches the game services, then removes every applied patch (newest first) and the header. Never throws; idempotent.</summary>
        public void Undo()
        {
            IKfsConsoleHost? console = Console;
            Console = null;
            if (console is not null) PurrConsole.Detach(console, ConsoleState);   // never throws

            IKfsGameServices? services = GameServices;
            GameServices = null;
            // Asked before the services detach: whether anything may read SystemHeaders off the main thread now.
            string? inUse = Header is null ? null : SystemHeaderReaders.InUse(services is null ? null : services.SystemHeadersInUse);
            if (services is not null)
            {
                try { services.Detach(); }
                catch (Exception e) { KfsLog.Once("detach game services", e); }
            }
            for (int i = Applied.Count - 1; i >= 0; i--) TryRemove(Applied[i].Patch, Applied[i].Target);
            Applied.Clear();
            try
            {
                if (Header is { } header && !header.Release(inUse))
                    KfsLog.Info($"{KfsExtensionStatus.LinePrefix}<purrtty.h> left registered: {inUse} (changing the game's header table under a running compile could corrupt it; the next install takes it over)");
            }
            catch (Exception e) { KfsLog.Once("remove header", e); }
            Header = null;
        }

        public void TryRemove(KfsPatch patch, MethodBase target)
        {
            try { patch.Remove(Harmony, target); }
            catch (Exception e) { KfsLog.Once($"remove patch {patch.Name}", e); }
        }
    }
}

/// <summary>Substitutable parts of the install sequence (tests only; <see cref="Default"/> is the real one).</summary>
internal sealed record InstallSeams
{
    /// <summary>The real sequence.</summary>
    public static InstallSeams Default { get; } = new();

    /// <summary>The catalogue to check, build and register.</summary>
    public ServiceCatalog Catalog { get; init; } = XCatalog.Catalog;

    /// <summary>Opens a stock table file.</summary>
    public Func<string, Stream?> OpenStock { get; init; } = TableAugmenter.OpenStock;

    /// <summary>Builds the augmented tables (step 4).</summary>
    public Func<Func<string, Stream?>, ServiceCatalog, GameTables> BuildTables { get; init; } =
        static (open, catalog) => TableAugmenter.Build(open, catalog);

    /// <summary>Target resolvers replacing those of the registry entries with these names.</summary>
    public IReadOnlyDictionary<string, Func<MethodBase?>> TargetResolvers { get; init; } = new Dictionary<string, Func<MethodBase?>>();

    /// <summary>Finds <c>KfsIntegration._tables</c> (step 5).</summary>
    public Func<FieldInfo?> FindTablesField { get; init; } = TablesPublisher.FindField;

    /// <summary>Registers the header and the builtin names (step 7).</summary>
    public Func<string, IReadOnlyList<string>, SystemHeaderRegistration> RegisterHeader { get; init; } =
        static (text, builtins) => SystemHeaderRegistration.Install(text, builtins);

    /// <summary>Called after each step that changed something (<c>published</c>, <c>required &lt;name&gt;</c>, <c>header</c>, <c>optional</c>); may throw to inject a failure there.</summary>
    public Action<string>? Step { get; init; }

    /// <summary>Receives the status line.</summary>
    public Action<string> Log { get; init; } = KfsLog.Info;
}
