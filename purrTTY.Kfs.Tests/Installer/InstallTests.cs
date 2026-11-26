using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSA.Kfs;
using KSA.Kfs.Host;
using KSA.Kfs.Vm.Tables;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Game;
using purrTTY.Kfs.Patches;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tables;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Installer;

/// <summary>
/// The installer (<see cref="KfsExtension"/>, plan §4.5): all-or-nothing install, table publication
/// in each of its cases, uninstall, idempotence, the optional bench hook switched off. These tests
/// change process-wide state (Harmony patches, <c>SystemHeaders</c>, <c>KfsIntegration._tables</c>),
/// so they live outside <c>purrTTY.Kfs.Tests.Runtime</c> and its shared runtime, and every one of
/// them leaves the process clean: the tear-down uninstalls, resets <c>_tables</c> and asserts that
/// no header, no builtin name and no patch of purrTTY's is left.
/// </summary>
[TestFixture]
public sealed class InstallTests : GameFixture
{
    private Harmony _harmony = null!;
    private readonly List<string> _log = [];

    [SetUp]
    public void StartClean()
    {
        Assert.That(KfsExtension.IsActive, Is.False, "a previous test left the extension installed");
        Assert.That(KfsRuntime.Current, Is.Null, "a previous test left a KfsRuntime running");
        ResetTables();
        _harmony = new Harmony("purrtty.kfs.tests.install." + Guid.NewGuid().ToString("N"));
        _log.Clear();
    }

    [TearDown]
    public void LeaveClean()
    {
        KfsRuntime.Current?.Dispose();
        KfsExtension.Uninstall();
        ResetTables();

        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(SystemHeaderScope.IsClean(), Is.True, "<purrtty.h> or a builtin name was left in SystemHeaders");
        Assert.That(TablesAreNull(), Is.True, "KfsIntegration._tables was not reset");
        Assert.That(PatchedBy(_harmony.Id), Is.Empty, "patches owned by the test's Harmony id were left");
        Assert.That(OurPatchesAnywhere(), Is.Empty, "purrTTY patches were left");
        Assert.That(PartsRegistry.Live, Is.Empty, "bindings were left");
        Assert.That(PurrConsole.Host, Is.Null, "a console host was left attached");
        Assert.That(PurrConsole.HasLingeringCommand, Is.False, "a purr command was left in a console");
    }

    // ------------------------------------------------------------------ install / report

    [Test]
    public void Install_Activates_And_Reports()
    {
        KfsExtensionStatus status = Install();

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Active), status.Describe());
        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.None));
        Assert.That(status.IsActive && KfsExtension.IsActive && KfsExtension.Status.IsActive, Is.True);
        Assert.That(status.RequiredPatches, Is.EqualTo(new[] { "ApplyRequest", "IssueResult", "BeforeInputEvents" }));
        Assert.That(status.GameServices, Is.Null, "installed without game services, as the test runtime is");
        Assert.That(status.OptionalPatches, Is.EqualTo(new[] { "BenchApplyRequest" }));
        Assert.That(status.OptionalPatchesSkipped, Is.Empty);
        Assert.That(status.Tables, Is.EqualTo(KfsTablesPublication.Published));
        Assert.That(status.ServiceCount, Is.EqualTo(XCatalog.Services.Count));
        Assert.That((status.FirstId, status.LastId), Is.EqualTo((XCatalog.Services.Min(s => s.Id), XCatalog.Services.Max(s => s.Id))));

        // Header and builtins registered.
        Assert.That(HeaderText(), Is.EqualTo(CatalogEmitter.Header(XCatalog.Catalog)));
        Assert.That(MissingBuiltins(), Is.Empty);

        // KfsIntegration.Tables is the augmented instance, assigned through the field.
        AssertAugmentedTablesPublished();

        // Exactly one log line, of the documented shape.
        Assert.That(_log, Has.Count.EqualTo(1));
        Assert.That(_log[0], Is.EqualTo(status.ToString()));
        Assert.That(_log[0], Is.EqualTo(
            $"purrTTY kFS extension: active — {XCatalog.Services.Count} services 0x{status.FirstId:X4}–0x{status.LastId:X4}, 3 hooks (+1 optional), tables published"));
        Assert.That(_log[0], Does.Match(@"^purrTTY kFS extension: active — \d+ services 0x[0-9A-F]{4}–0x[0-9A-F]{4}, \d+ hooks \(\+\d+ optional\), tables \w+"));
        Assert.That(status.Describe().Split('\n'), Has.Length.GreaterThan(3));
    }

    [Test]
    public void Patches_Apply_To_Distinct_Targets()
    {
        var targets = KfsExtension.Patches.Select(p => (p.Name, Target: p.ResolveTarget())).ToList();
        targets.Add((KfsExtension.LoadEmbeddedFallback.Name, KfsExtension.LoadEmbeddedFallback.ResolveTarget()));
        Assert.That(targets.Where(t => t.Target is null).Select(t => t.Name), Is.Empty, "every registered patch resolves");
        Assert.That(targets.Select(t => t.Target).Distinct().Count(), Is.EqualTo(targets.Count), "targets are pairwise distinct");

        Assert.That(Install().IsActive, Is.True);
        foreach (KfsPatch patch in KfsExtension.Patches)
        {
            Assert.That(PostfixOwners(patch.ResolveTarget()!), Is.EqualTo(new[] { _harmony.Id }), $"{patch.Name} applied once, by the install's Harmony");
        }
        Assert.That(PostfixOwners(KfsExtension.LoadEmbeddedFallback.ResolveTarget()!), Is.Empty, "the fallback is not needed when the field exists");

        KfsExtension.Uninstall();
        foreach (KfsPatch patch in KfsExtension.Patches)
        {
            Assert.That(PostfixOwners(patch.ResolveTarget()!), Is.Empty, $"{patch.Name} removed by uninstall");
        }
    }

    [Test]
    public void Install_Is_Idempotent()
    {
        KfsExtensionStatus first = Install();
        Assert.That(first.IsActive, Is.True, first.Describe());

        var other = new Harmony(_harmony.Id + ".second");
        KfsExtensionStatus second = KfsExtension.Install(other, null, Seams());

        Assert.That(second with { Counters = first.Counters }, Is.EqualTo(first), "the same status, nothing redone");
        Assert.That(_log, Has.Count.EqualTo(1), "the second call logs nothing");
        foreach (KfsPatch patch in KfsExtension.Patches)
        {
            Assert.That(PostfixOwners(patch.ResolveTarget()!), Is.EqualTo(new[] { _harmony.Id }), patch.Name);
        }
        Assert.That(PatchedBy(other.Id), Is.Empty);
        Assert.That(MissingBuiltins(), Is.Empty);
    }

    [Test]
    public void Uninstall_Restores()
    {
        var runtime = KfsRuntime.Start();
        string runtimeId = runtime.HarmonyId;
        using SimVehicle vehicle = runtime.CreateVehicle(SimVehicleSpec.With(lights: 1, name: "uninstall"));
        Assert.That(PartsRegistry.Live, Has.Member(vehicle.Binding));
        Assert.That(PatchedBy(runtimeId), Is.Not.Empty);

        KfsExtension.Uninstall();

        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(KfsExtension.Status.State, Is.EqualTo(KfsExtensionState.NotInstalled));
        Assert.That(SystemHeaderScope.IsClean(), Is.True, "header and builtins removed");
        Assert.That(PatchedBy(runtimeId), Is.Empty, "every hook removed");
        Assert.That(PartsRegistry.Live, Is.Empty, "every binding removed");
        Assert.That(vehicle.Binding.IsBound, Is.False);
        Assert.That(IsCurrentTables(runtime), Is.True, "the published tables stay");

        // A second install in the same process works and reuses them.
        KfsExtensionStatus again = Install();
        Assert.That(again.IsActive, Is.True, again.Describe());
        Assert.That(again.Tables, Is.EqualTo(KfsTablesPublication.Reused));
        Assert.That(IsCurrentTables(runtime), Is.True);
    }

    [Test]
    public void Install_Reuses_Its_Own_Tables()
    {
        Assert.That(Install().Tables, Is.EqualTo(KfsTablesPublication.Published));
        object first = CurrentTables();
        KfsExtension.Uninstall();
        Assert.That(CurrentTables(), Is.SameAs(first), "uninstall leaves the tables published");

        KfsExtensionStatus again = Install();
        Assert.That(again.IsActive, Is.True, again.Describe());
        Assert.That(again.Tables, Is.EqualTo(KfsTablesPublication.Reused));
        Assert.That(CurrentTables(), Is.SameAs(first));
        Assert.That(_log, Has.Count.EqualTo(2));
        Assert.That(_log[1], Does.EndWith(", tables reused"));
        AssertAugmentedTablesPublished();
    }

    // ------------------------------------------------------------------ refusals

    [TestCase("catalogue")]
    [TestCase("target")]
    [TestCase("foreign-tables")]
    [TestCase("apply-fails")]
    public void Install_Is_All_Or_Nothing(string failure)
    {
        InstallSeams seams = Seams();
        KfsDisabledReason reason;
        string message;
        switch (failure)
        {
            case "catalogue":
                // A service moved onto a stock id: outside the block and in the stock table.
                seams = seams with { Catalog = XCatalog.Catalog.WithService(Svc.PartsInfo, s => s with { Id = 1 }) };
                (reason, message) = (KfsDisabledReason.Catalogue, "catalogue: ");
                break;
            case "target":
                seams = seams with { TargetResolvers = new Dictionary<string, Func<MethodBase?>> { ["IssueResult"] = () => null } };
                (reason, message) = (KfsDisabledReason.TargetNotFound, "target VehicleComputer ISyscallHost.IssueResult not found");
                break;
            case "foreign-tables":
                SeedForeignTables();
                (reason, message) = (KfsDisabledReason.TablesAlreadyBuilt, "tables already built (origin test:foreign-stock)");
                break;
            default:
                // The second required patch cannot be applied (an abstract method has no body):
                // the first, already applied, must be removed again.
                seams = seams with
                {
                    TargetResolvers = new Dictionary<string, Func<MethodBase?>> { ["IssueResult"] = () => typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose)) },
                };
                (reason, message) = (KfsDisabledReason.PatchFailed, "patch IssueResult on VehicleComputer ISyscallHost.IssueResult failed (");
                break;
        }

        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, seams);

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled), status.Describe());
        Assert.That(status.Reason, Is.EqualTo(reason), status.Describe());
        Assert.That(status.Message, Does.StartWith(message));
        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(KfsExtension.Status.State, Is.EqualTo(KfsExtensionState.Disabled));
        Assert.That(SystemHeaderScope.IsClean(), Is.True, "no header, no builtins");
        Assert.That(PatchedBy(_harmony.Id), Is.Empty, "no patches");
        Assert.That(status.RequiredPatches, Is.Empty);
        Assert.That(_log, Is.EqualTo(new[] { "purrTTY kFS extension: disabled — " + status.Message }));

        if (failure == "foreign-tables")
        {
            Assert.That(CurrentTablesOrigin(), Is.EqualTo("test:foreign-stock"), "someone else's tables are left alone");
        }
        else if (failure != "apply-fails")
        {
            Assert.That(TablesAreNull(), Is.True, "refused before publishing: nothing published");
        }
    }

    [Test]
    public void Tables_Rejected_Disables()
    {
        // A domain naming no enumeration: the game's loader refuses the whole table.
        ServiceCatalog rejected = XCatalog.Catalog.WithService(Svc.LightGeti,
            s => s with { Params = [s.Params[0], s.Params[1] with { Domain = "enum:x_nope" }] });
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams() with { BuildTables = (open, _) => TableAugmenter.Build(open, rejected) });

        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.TablesRejected), status.Describe());
        Assert.That(status.Message, Does.StartWith("tables rejected (").And.Contain("enum:x_nope"));
        Assert.That(SystemHeaderScope.IsClean(), Is.True);
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
        Assert.That(TablesAreNull(), Is.True, "nothing published");
        Assert.That(StockLoaderOrigin(), Is.EqualTo("embedded:KSA"), "the game's own loader is untouched");
    }

    [Test]
    public void Fallback_Postfix_Publishes_Tables()
    {
        ResetTables();
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams() with { FindTablesField = () => null });

        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(status.Tables, Is.EqualTo(KfsTablesPublication.Fallback));
        Assert.That(_log.Single(), Does.EndWith(", tables published through the LoadEmbedded fallback"));
        // The installer verified the publication through the game's accessor, whose lazy
        // initialiser ran LoadEmbedded (postfixed) and cached the result: our instance.
        Assert.That(ReferenceEquals(KfsIntegrationTables.Read(), LoadEmbeddedFallbackPatch.Tables), Is.True, "cached by the game's own initialiser");
        MethodBase loadEmbedded = KfsExtension.LoadEmbeddedFallback.ResolveTarget()!;
        Assert.That(PostfixOwners(loadEmbedded), Is.EqualTo(new[] { _harmony.Id }));

        // The game's accessor runs its loader, and the postfix hands out the augmented instance.
        AssertFallbackTables();
        AssertAugmentedTablesPublished();

        KfsExtension.Uninstall();
        Assert.That(PostfixOwners(loadEmbedded), Is.Empty, "the fallback postfix is removed with the others");
        Assert.That(StockLoaderOrigin(), Is.EqualTo("embedded:KSA"));
    }

    [Test]
    public void Disabled_By_Option()
    {
        KfsExtensionStatus status = Install(new KfsExtensionOptions { Enabled = false });

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled));
        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.Option));
        Assert.That(_log, Is.EqualTo(new[] { "purrTTY kFS extension: disabled — switched off" }));
        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(SystemHeaderScope.IsClean(), Is.True);
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
        Assert.That(TablesAreNull(), Is.True);
    }

    // ------------------------------------------------------------------ undo after a side effect (review C2)

    [Test]
    public void Null_Option_Collections_Are_Empty()
    {
        KfsExtensionStatus status = Install(new KfsExtensionOptions { DisabledOptionalPatches = null! });

        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(status.OptionalPatches, Is.EqualTo(new[] { "BenchApplyRequest" }));
        AssertEachPatchOnce();
    }

    /// <summary>An exception after the first side effect (published tables) undoes every hook and the header; a later install starts clean.</summary>
    [TestCase("published")]
    [TestCase("required ApplyRequest")]
    [TestCase("required IssueResult")]
    [TestCase("required BeforeInputEvents")]
    [TestCase("header")]
    [TestCase("optional")]
    public void Unexpected_Failure_At_A_Step_Undoes_The_Install(string step)
    {
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null,
            Seams() with { Step = s => { if (s == step) throw new InvalidOperationException("injected at " + s); } });

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled), status.Describe());
        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.Unexpected));
        Assert.That(status.Message, Does.Contain("injected at " + step));
        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(SystemHeaderScope.IsClean(), Is.True, "no header, no builtins");
        Assert.That(PatchedBy(_harmony.Id), Is.Empty, "no hook left");
        Assert.That(OurPatchesAnywhere(), Is.Empty);

        _log.Clear();
        KfsExtensionStatus again = Install();
        Assert.That(again.IsActive, Is.True, again.Describe());
        AssertEachPatchOnce();
        Assert.That(again.Tables, Is.EqualTo(KfsTablesPublication.Reused), "the tables published before the failure stay and are reused");
    }

    [Test]
    public void Header_Failure_Disables()
    {
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null,
            Seams() with { RegisterHeader = (_, _) => throw new InvalidOperationException("headers are read-only") });

        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.HeaderFailed), status.Describe());
        Assert.That(status.Message, Is.EqualTo("header not registered (headers are read-only)"));
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
        Assert.That(SystemHeaderScope.IsClean(), Is.True);
    }

    [Test]
    public void Optional_Patch_Failure_Is_Logged_And_Ignored()
    {
        long logged = KfsLog.Count("optional patch BenchApplyRequest");
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams() with
        {
            TargetResolvers = new Dictionary<string, Func<MethodBase?>> { ["BenchApplyRequest"] = AbstractMethod },
        });

        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(status.OptionalPatches, Is.Empty);
        Assert.That(status.OptionalPatchesSkipped, Has.Count.EqualTo(1));
        Assert.That(status.OptionalPatchesSkipped[0], Does.StartWith("BenchApplyRequest: "));
        Assert.That(KfsLog.Count("optional patch BenchApplyRequest"), Is.EqualTo(logged + 1));
        Assert.That(PostfixOwners(BenchApplyRequestPatch.TargetMethod()!), Is.Empty);
        Assert.That(_log.Single(), Does.Contain("3 hooks (+0 optional)"));
    }

    // ------------------------------------------------------------------ publication refusals

    [Test]
    public void Tables_Not_Visible_Disables()
    {
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams() with { FindTablesField = DecoyTablesField.Find });

        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.TablesNotPublished), status.Describe());
        Assert.That(status.Message, Is.EqualTo("tables not published (KfsIntegration.Tables did not return the assigned instance)"));
        Assert.That(DecoyTablesField.Value, Is.Null, "the assignment was undone");
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
        Assert.That(SystemHeaderScope.IsClean(), Is.True);
    }

    [Test]
    public void Fallback_Apply_Failure_Disables()
    {
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams() with
        {
            FindTablesField = () => null,
            TargetResolvers = new Dictionary<string, Func<MethodBase?>> { ["LoadEmbeddedFallback"] = AbstractMethod },
        });

        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.TablesNotPublished), status.Describe());
        Assert.That(status.Message, Does.StartWith("tables not published (fallback patch on GameTables.LoadEmbedded failed: "));
        Assert.That(LoadEmbeddedFallbackPatch.Tables, Is.Null);
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
        Assert.That(StockLoaderOrigin(), Is.EqualTo("embedded:KSA"));
    }

    [Test]
    public void Fallback_Without_A_Target_Disables()
    {
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams() with
        {
            FindTablesField = () => null,
            TargetResolvers = new Dictionary<string, Func<MethodBase?>> { ["LoadEmbeddedFallback"] = () => null },
        });

        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.TablesNotPublished), status.Describe());
        Assert.That(status.Message, Is.EqualTo("tables not published (field KfsIntegration._tables and target GameTables.LoadEmbedded not found)"));
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
    }

    /// <summary>Review C4: when the game's accessor does not hand out the fallback's tables (someone built them first), the postfix is removed and the install refused.</summary>
    [Test]
    public void Fallback_Refuses_When_The_Game_Keeps_Other_Tables()
    {
        SeedForeignTables();
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams() with { FindTablesField = () => null });

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled), status.Describe());
        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.TablesAlreadyBuilt));
        Assert.That(status.Message, Is.EqualTo("tables already built (KfsIntegration.Tables kept origin test:foreign-stock; the GameTables.LoadEmbedded fallback was removed)"));
        Assert.That(PostfixOwners(KfsExtension.LoadEmbeddedFallback.ResolveTarget()!), Is.Empty, "the fallback postfix is removed");
        Assert.That(LoadEmbeddedFallbackPatch.Tables, Is.Null);
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
        Assert.That(SystemHeaderScope.IsClean(), Is.True);
        Assert.That(CurrentTablesOrigin(), Is.EqualTo("test:foreign-stock"));
        Assert.That(StockLoaderOrigin(), Is.EqualTo("embedded:KSA"));
    }

    // ------------------------------------------------------------------ "already ours" (review C3)

    /// <summary>
    /// Tables a different build of purrTTY published in this process (same ids and names, anything
    /// else changed): never reused, or the new header would compile against the old rows.
    /// </summary>
    [TestCase("selector-range")]
    [TestCase("arg-types")]
    [TestCase("result-cells")]
    [TestCase("kind")]
    [TestCase("cost-class")]
    [TestCase("enum-list")]
    [TestCase("summary")]
    public void Install_Refuses_purrTTY_Tables_Of_Another_Catalogue(string difference)
    {
        ServiceCatalog other = OtherCatalogue(difference);
        Assert.That(other, Is.Not.EqualTo(XCatalog.Catalog));
        Assert.That(other.Services.Select(s => (s.Id, s.Name)), Is.EqualTo(XCatalog.Services.Select(s => (s.Id, s.Name))), "same ids and names");

        foreach (string origin in new[] { "embedded:KSA+purrtty", TableAugmenter.OriginFor(other) })
        {
            KfsIntegrationTables.Seed(TableAugmenter.Build(TableAugmenter.OpenStock, other, origin));
            KfsExtensionStatus status = Install();

            Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled), $"{difference}, origin {origin}: {status.Describe()}");
            Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.TablesAlreadyBuilt));
            Assert.That(status.Message, Does.StartWith("tables already built (").And.Contain(origin));
            Assert.That(PatchedBy(_harmony.Id), Is.Empty);
            Assert.That(SystemHeaderScope.IsClean(), Is.True);
            ResetTables();
        }
    }

    private static ServiceCatalog OtherCatalogue(string difference)
    {
        ServiceCatalog real = XCatalog.Catalog;
        ServiceDef ChangeParam(ServiceDef s, int i, Func<ParamDef, ParamDef> change) =>
            s with { Params = s.Params.Select((p, j) => j == i ? change(p) : p).ToArray() };
        return difference switch
        {
            "selector-range" => real.WithService(Svc.LightGeti, s => ChangeParam(s, 1, p => p with { Domain = "enum:x_light_prop[0..8]" })),
            "arg-types" => real.WithService(Svc.LightSetIntensity, s => ChangeParam(s, 1, p => p with { CType = "uint32_t", CellTypes = ["u32"], Domain = "any" })),
            "result-cells" => real.WithService(Svc.LightGetf, s => s with { ResultCType = "uint32_t", ResultCells = ["u32"] }),
            "kind" => real.WithService(Svc.LightCount, s => s with { Kind = ServiceKinds.Request, ResultCType = "void", ResultCells = [] }),
            "cost-class" => real.WithService(Svc.LightCount, s => s with { CostClass = CostClasses.Heavy }),
            "enum-list" => real with
            {
                Enums = real.Enums.Select(e => ReferenceEquals(e, XCatalog.LightPropEnum)
                    ? e with { Values = e.Values.Select((v, i) => i == 8 ? "extra" : v).ToArray() }
                    : e).ToArray(),
            },
            "summary" => real.WithService(Svc.LightCount, s => s with { Summary = "Number of lights." }),
            _ => throw new ArgumentOutOfRangeException(nameof(difference)),
        };
    }

    [Test]
    public void Origin_Stamps_The_Catalogue()
    {
        string origin = TableAugmenter.OriginFor(XCatalog.Catalog);
        Assert.That(origin, Does.Match("^embedded:KSA\\+purrtty;catalog=[0-9a-f]{16}$"));
        Assert.That(TableAugmenter.OriginFor(XCatalog.Catalog with { Services = XCatalog.Services.ToArray() }), Is.EqualTo(origin), "content, not identity");
        Assert.That(TableAugmenter.OriginFor(OtherCatalogue("summary")), Is.Not.EqualTo(origin));
        Assert.That(TableAugmenter.IsAugmented(origin) && TableAugmenter.IsAugmented("embedded:KSA+purrtty"), Is.True);
        Assert.That(TableAugmenter.IsAugmented("embedded:KSA"), Is.False);
    }

    // ------------------------------------------------------------------ bindings across installs (review C7)

    [Test]
    public void Install_Cycles_Leave_ImageLoaded_Alone()
    {
        Assert.That(Install().IsActive, Is.True);
        var fake = new FakeVehicle(KfsIntegration.Tables, 1, "cycles");
        int calls = 0;
        Action original = () => calls++;
        fake.Computer.ImageLoaded = original;

        for (int cycle = 0; cycle < 4; cycle++)
        {
            if (cycle > 0) Assert.That(Install().IsActive, Is.True);
            VehicleBinding binding = PartsRegistry.Bind(fake.Computer, new SimVehicleParts(lights: 1));
            Assert.That(PartsRegistry.Find(fake.Computer), Is.SameAs(binding));
            KfsExtension.Uninstall();
            Assert.That(binding.IsBound, Is.False);
            Assert.That(PartsRegistry.Live, Is.Empty);
            Assert.That(ReferenceEquals(fake.Computer.ImageLoaded, original), Is.True, $"cycle {cycle}: the game's delegate is never wrapped");
        }
        Assert.That(fake.Computer.ImageLoaded!.GetInvocationList(), Has.Length.EqualTo(1));
    }

    [Test]
    public void No_Bind_After_Uninstall()
    {
        Assert.That(Install().IsActive, Is.True);
        var fake = new FakeVehicle(KfsIntegration.Tables, 1, "late");
        PartsRegistry.Fallback = c => PartsRegistry.Bind(c, new SimVehicleParts(lights: 1));
        var readBeforeUninstall = PartsRegistry.Fallback;   // what a dispatch thread may hold

        KfsExtension.Uninstall();

        Assert.That(PartsRegistry.Fallback, Is.Null);
        var refused = Assert.Throws<InvalidOperationException>(() => readBeforeUninstall!(fake.Computer));
        Assert.That(refused!.Message, Does.Contain("not installed"));
        Assert.That(PartsRegistry.Live, Is.Empty, "nothing bound that no pump would ever prune");
        Assert.That(PartsRegistry.Find(fake.Computer), Is.Null);
    }

    // ------------------------------------------------------------------ game services (task A4)

    [Test]
    public void Pump_Hook_Is_Inert_Without_Game_Services()
    {
        Assert.That(Install().IsActive, Is.True);
        long calls = BeforeInputEventsPatch.Calls;

        Assert.DoesNotThrow(RunGameFrameHook);

        Assert.That(BeforeInputEventsPatch.Calls, Is.EqualTo(calls + 1), "the postfix runs offline too");
        Assert.That(MainThreadPump.Active, Is.Null, "but has no pump to run");
        Assert.That(KfsExtension.Status.Counters.PumpFrames, Is.EqualTo(calls + 1));
    }

    [Test]
    public void Game_Services_Install_Offline_And_The_Hook_Runs_Their_Pump()
    {
        var services = new KsaGameServices();
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams(), services);

        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(status.GameServices, Is.EqualTo("game back end"));
        Assert.That(_log.Single(), Does.Contain(", 3 hooks (+1 optional), tables published, game back end").And.EndWith("game back end"));
        Assert.That(status.Describe(), Does.Contain("back end: game back end"));
        MainThreadPump pump = services.Pump ?? throw new AssertionException("no pump attached");
        Assert.That(MainThreadPump.Active, Is.SameAs(pump));
        Assert.That(PartsRegistry.Fallback, Is.SameAs(pump.BindFromDispatchDelegate));
        Assert.That(services.Directory!.Count, Is.Zero, "no game hosts offline");

        long frames = pump.Frames, calls = BeforeInputEventsPatch.Calls;
        RunGameFrameHook();
        RunGameFrameHook();
        Assert.That(pump.Frames, Is.EqualTo(frames + 2), "the game's per-frame hook runs the pump");
        Assert.That(BeforeInputEventsPatch.Calls, Is.EqualTo(calls + 2));
        Assert.That((pump.Bound, pump.Pruned, pump.Failures), Is.EqualTo((0L, 0L, 0L)));
        Assert.That(PartsRegistry.Live, Is.Empty);
        Assert.That(FindsNoHostFor("stranger"), Is.True, "the fallback finds no host for a computer the game does not know");

        KfsExtension.Uninstall();
        Assert.That(MainThreadPump.Active, Is.Null, "uninstall stops the pump");
        Assert.That(PartsRegistry.Fallback, Is.Null, "and clears the fallback");
        Assert.That(services.Pump, Is.Null);
        RunGameFrameHook();
        Assert.That(BeforeInputEventsPatch.Calls, Is.EqualTo(calls + 2), "the hook is removed");
        Assert.That(pump.BindFromDispatch(NewComputer("late")), Is.Null, "a dispatch thread still holding the old fallback binds nothing and throws nothing");
    }

    [Test]
    public void Missing_AllHosts_Disables_And_Leaves_Nothing()
    {
        var services = new KsaGameServices(() => null);
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams(), services);

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled), status.Describe());
        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.GameServicesUnavailable));
        Assert.That(status.Message, Is.EqualTo("game back end unavailable (field KfsVehicleHost.AllHosts not found)"));
        Assert.That(_log, Is.EqualTo(new[] { "purrTTY kFS extension: disabled — game back end unavailable (field KfsVehicleHost.AllHosts not found)" }));
        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(TablesAreNull(), Is.True, "refused with the patch targets, before anything was published");
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
        Assert.That(SystemHeaderScope.IsClean(), Is.True);
        Assert.That(MainThreadPump.Active, Is.Null);
        Assert.That(PartsRegistry.Fallback, Is.Null);

        Assert.That(new KsaGameServices(() => typeof(KfsExtension).GetField("Gate", BindingFlags.Static | BindingFlags.NonPublic)).Prepare(),
            Does.Contain("not a static List<KfsVehicleHost>"), "a field of another type is refused too");
        Assert.That(new KsaGameServices().Prepare(), Is.Null, "this build has the field");
    }

    [TestCase("attach")]
    [TestCase("after attach")]
    public void Game_Services_Failure_Undoes_The_Install(string where)
    {
        var services = new RecordingServices { ThrowOnAttach = where == "attach" };
        InstallSeams seams = Seams() with
        {
            Step = s => { if (where == "after attach" && s == "game services") throw new InvalidOperationException("injected at " + s); },
        };

        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, seams, services);

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled), status.Describe());
        Assert.That(status.Reason, Is.EqualTo(where == "attach" ? KfsDisabledReason.GameServicesFailed : KfsDisabledReason.Unexpected));
        Assert.That(status.Message, Does.StartWith(where == "attach" ? "recording services not attached (InvalidOperationException: injected attach failure)" : "install failed ("));
        Assert.That((services.Prepares, services.Attaches, services.Detaches), Is.EqualTo((1, 1, 1)), "prepared, attached, then detached by the undo");
        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(PatchedBy(_harmony.Id), Is.Empty, "no hook left");
        Assert.That(SystemHeaderScope.IsClean(), Is.True);

        // A later install starts clean, and its uninstall detaches its services exactly once.
        _log.Clear();
        var next = new RecordingServices();
        KfsExtensionStatus again = KfsExtension.Install(_harmony, null, Seams(), next);
        Assert.That(again.IsActive, Is.True, again.Describe());
        Assert.That(_log.Single(), Does.EndWith(", recording services"));
        KfsExtension.Uninstall();
        KfsExtension.Uninstall();
        Assert.That((next.Prepares, next.Attaches, next.Detaches), Is.EqualTo((1, 1, 1)));
    }

    [Test]
    public void Game_Services_Prepare_Failure_Refuses_Before_Any_Change()
    {
        var services = new RecordingServices { PrepareReason = "nothing to discover" };
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams(), services);

        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.GameServicesUnavailable), status.Describe());
        Assert.That(status.Message, Is.EqualTo("recording services unavailable (nothing to discover)"));
        Assert.That((services.Prepares, services.Attaches, services.Detaches), Is.EqualTo((1, 0, 0)));
        Assert.That(TablesAreNull(), Is.True);
        Assert.That(PatchedBy(_harmony.Id), Is.Empty);
    }

    /// <summary>Game services that only count their calls (no game types).</summary>
    private sealed class RecordingServices : IKfsGameServices
    {
        public bool ThrowOnAttach { get; init; }
        public string? PrepareReason { get; init; }
        public int Prepares { get; private set; }
        public int Attaches { get; private set; }
        public int Detaches { get; private set; }

        public string Name => "recording services";

        public string? Prepare()
        {
            Prepares++;
            return PrepareReason;
        }

        public void Attach()
        {
            Attaches++;
            if (ThrowOnAttach) throw new InvalidOperationException("injected attach failure");
        }

        public void Detach() => Detaches++;
    }

    // ------------------------------------------------------------------ the purr console command (step 9)

    [Test]
    public void Purr_Command_Is_Registered_Once_And_Removed_At_Uninstall()
    {
        var console = new SimConsoleHost();
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console));

        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(status.ConsoleCommand, Is.EqualTo(PurrCommandState.Registered));
        Assert.That(_log.Single(), Does.EndWith(", console services, purr command"));
        Assert.That(status.Describe(), Does.Contain("  console:  purr registered\n"));
        Assert.That(console.Registered, Has.Count.EqualTo(1));
        Assert.That(console.Registered[0], Is.SameAs(PurrConsole.Command));
        AssertTerminalAction(console.Registered[0]);
        Assert.That(PurrConsole.Host, Is.SameAs(console));

        console.Type("status");   // what the console does with `purr status`
        Assert.That(console.Lines, Is.EqualTo(KfsExtension.Status.Describe().Split('\n').Select(ConsoleText.ToAscii)), "in ASCII, for the console's font");

        KfsExtension.Uninstall();
        Assert.That((console.UnregisterCalls, console.Registered.Count), Is.EqualTo((1, 0)), "removed at uninstall");
        Assert.That(PurrConsole.Host, Is.Null);

        // A re-install registers it again — once.
        _log.Clear();
        Assert.That(KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console)).ConsoleCommand, Is.EqualTo(PurrCommandState.Registered));
        Assert.That((console.RegisterCalls, console.Registered.Count), Is.EqualTo((2, 1)));
        KfsExtension.Uninstall();
        Assert.That(console.Registered, Is.Empty);
    }

    [Test]
    public void Purr_Command_That_Cannot_Be_Removed_Stays_Inert_And_Is_Not_Registered_Twice()
    {
        var console = new SimConsoleHost { CanUnregister = false };
        Assert.That(KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console)).ConsoleCommand, Is.EqualTo(PurrCommandState.Registered));
        KfsExtension.Uninstall();
        Assert.That((console.UnregisterCalls, console.Registered.Count), Is.EqualTo((1, 1)), "this console keeps the command");
        Assert.That(PurrConsole.HasLingeringCommand, Is.True);

        // The command still answers, through the console's own output, and does nothing.
        console.Lines.Clear();
        console.Type("parts");
        console.Type("help");
        Assert.That(console.Lines[0], Is.EqualTo("purr parts: the purrTTY kFS extension is not active (not installed); purr status says more"));
        Assert.That(console.Lines[1], Is.EqualTo("purr: the purrTTY kFS extension is not active (not installed); verbs (purr <verb> [arguments]):"));

        // A re-install recognises the console: no second registration.
        _log.Clear();
        KfsExtensionStatus again = KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console));
        Assert.That(again.ConsoleCommand, Is.EqualTo(PurrCommandState.Kept), again.Describe());
        Assert.That((console.RegisterCalls, console.Registered.Count), Is.EqualTo((1, 1)));
        Assert.That(_log.Single(), Does.EndWith(", purr command"));
        Assert.That(again.Describe(), Does.Contain("purr kept from an earlier install"));
        console.Lines.Clear();
        console.Type("help");
        Assert.That(console.Lines[0], Is.EqualTo("purr: the purrTTY kFS extension is active; verbs (purr <verb> [arguments]):"), "the kept command reaches the new install");

        // Once the console can remove it, the next uninstall does.
        console.CanUnregister = true;
        KfsExtension.Uninstall();
        Assert.That(console.Registered, Is.Empty);
        Assert.That(PurrConsole.HasLingeringCommand, Is.False);
    }

    [Test]
    public void Purr_Command_Registration_Failure_Leaves_The_Install_Active()
    {
        var console = new SimConsoleHost { RegisterFailure = new InvalidOperationException("the game console does not exist yet") };
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console));

        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(status.ConsoleCommand, Is.EqualTo(PurrCommandState.Failed));
        Assert.That(status.ConsoleMessage, Is.EqualTo("InvalidOperationException: the game console does not exist yet"));
        Assert.That(_log.Single(), Does.EndWith(", console services, no purr command (InvalidOperationException: the game console does not exist yet)"));
        Assert.That(status.Describe(), Does.Contain("  console:  purr not registered (InvalidOperationException: the game console does not exist yet); the purrTTY menu still runs it\n"));
        Assert.That(status.Describe().Split('\n')[0], Is.EqualTo(status.ToString()), "the status line stays one line");
        Assert.That(console.Registered, Is.Empty);

        // The host stays attached: the menu's direct calls still work.
        PurrConsole.Execute("help");
        Assert.That(console.Lines[0], Is.EqualTo("purr: the purrTTY kFS extension is active; verbs (purr <verb> [arguments]):"));

        KfsExtension.Uninstall();
        Assert.That(console.UnregisterCalls, Is.Zero, "nothing to remove");
        Assert.That(PurrConsole.Host, Is.Null);
    }

    [Test]
    public void Purr_Command_Is_Removed_When_A_Later_Step_Undoes_The_Install()
    {
        var console = new SimConsoleHost();
        InstallSeams seams = Seams() with
        {
            Step = s => { if (s == "game services") throw new InvalidOperationException("injected after the console"); },
        };
        KfsExtensionStatus status = KfsExtension.Install(_harmony, null, seams, new ConsoleServices(console));

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Disabled), status.Describe());
        Assert.That((console.RegisterCalls, console.UnregisterCalls, console.Registered.Count), Is.EqualTo((1, 1, 0)), "registered, then removed by the undo");
        Assert.That(PurrConsole.Host, Is.Null);
    }

    [Test]
    public void Purr_Command_Stands_While_The_Extension_Is_Disabled()
    {
        var console = new SimConsoleHost();
        using IDisposable verb = PurrConsole.Verbs.Add("standing-test", "a verb that works without the extension", c => c.Print("standing ok"));
        try
        {
            // The mod registers purr at load, before (and whatever) the install decides.
            Assert.That(PurrConsole.Register(console, out string message), Is.EqualTo(PurrCommandState.Registered), message);
            Assert.That((console.RegisterCalls, console.Registered.Count), Is.EqualTo((1, 1)));
            Assert.That(console.Registered[0], Is.SameAs(PurrConsole.Command));
            Assert.That(PurrConsole.HasStandingCommand, Is.True);

            KfsExtensionStatus disabled = KfsExtension.Install(_harmony, new KfsExtensionOptions { Enabled = false }, Seams(), new ConsoleServices(console));
            Assert.That(disabled.State, Is.EqualTo(KfsExtensionState.Disabled));
            Assert.That(PurrConsole.Host, Is.Null);

            console.Type("parts");
            console.Type("examples");
            console.Type("standing-test");
            console.Type("help");
            Assert.That(console.Lines[0], Is.EqualTo("purr parts: the purrTTY kFS extension is not active (disabled - switched off); purr status says more"));
            Assert.That(console.Lines[1], Is.EqualTo("purr examples: the purrTTY kFS extension is not active (disabled - switched off); purr status says more"));
            Assert.That(console.Lines[2], Is.EqualTo("standing ok"), "verbs that do not need the extension work");
            Assert.That(console.Lines[3], Is.EqualTo("purr: the purrTTY kFS extension is not active (disabled - switched off); verbs (purr <verb> [arguments]):"));

            // An install on the same console uses the standing registration; its uninstall leaves it.
            KfsExtensionStatus active = KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console));
            Assert.That(active.ConsoleCommand, Is.EqualTo(PurrCommandState.Standing), active.Describe());
            Assert.That(active.Describe(), Does.Contain("purr registered by purrTTY for as long as the mod is loaded"));
            Assert.That(active.ToString(), Does.EndWith(", console services, purr command"));
            Assert.That(console.RegisterCalls, Is.EqualTo(1), "not registered twice");
            console.Lines.Clear();
            console.Type("help");
            Assert.That(console.Lines[0], Is.EqualTo("purr: the purrTTY kFS extension is active; verbs (purr <verb> [arguments]):"));
            KfsExtension.Uninstall();
            Assert.That((console.UnregisterCalls, console.Registered.Count), Is.EqualTo((0, 1)), "the uninstall leaves it");

            // The mod unloads: removed.
            PurrConsole.Unregister();
            Assert.That((console.UnregisterCalls, console.Registered.Count, PurrConsole.HasStandingCommand), Is.EqualTo((1, 0, false)));
        }
        finally
        {
            PurrConsole.Unregister();
        }
    }

    [Test]
    public void Purr_Command_Registered_By_An_Install_Becomes_The_Standing_One()
    {
        var console = new SimConsoleHost();
        try
        {
            Assert.That(KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console)).ConsoleCommand, Is.EqualTo(PurrCommandState.Registered));
            Assert.That(PurrConsole.Register(console, out _), Is.EqualTo(PurrCommandState.Registered));
            Assert.That(console.RegisterCalls, Is.EqualTo(1), "the console already held it");
            Assert.That(PurrConsole.Register(console, out _), Is.EqualTo(PurrCommandState.Standing), "a second Register is a no-op");

            KfsExtension.Uninstall();
            Assert.That((console.UnregisterCalls, console.Registered.Count), Is.EqualTo((0, 1)), "standing now: the uninstall leaves it");
            console.Type("status");
            Assert.That(console.Lines[0], Is.EqualTo(KfsExtensionStatus.NotInstalled.ToString()), "and it still answers through the console");

            PurrConsole.Unregister();
            Assert.That(console.Registered, Is.Empty);
        }
        finally
        {
            PurrConsole.Unregister();
        }
    }

    [Test]
    public void Purr_Standing_Registration_Registers_When_The_Install_Could_Not()
    {
        // The install's registration failed (the console was not there yet) but its host stays
        // attached; the mod's standing registration later must still register the command.
        var console = new SimConsoleHost { RegisterFailure = new InvalidOperationException("the game console does not exist yet") };
        try
        {
            Assert.That(KfsExtension.Install(_harmony, null, Seams(), new ConsoleServices(console)).ConsoleCommand, Is.EqualTo(PurrCommandState.Failed));
            Assert.That(PurrConsole.Host, Is.SameAs(console), "attached although not registered");
            console.RegisterFailure = null;

            Assert.That(PurrConsole.Register(console, out string message), Is.EqualTo(PurrCommandState.Registered), message);
            Assert.That((console.RegisterCalls, console.Registered.Count), Is.EqualTo((2, 1)), "registered by the standing registration");
            console.Type("help");
            Assert.That(console.Lines[0], Is.EqualTo("purr: the purrTTY kFS extension is active; verbs (purr <verb> [arguments]):"));

            KfsExtension.Uninstall();
            Assert.That(console.Registered.Count, Is.EqualTo(1), "the standing one stays after the uninstall");
            PurrConsole.Unregister();
            Assert.That(console.Registered, Is.Empty);
        }
        finally
        {
            KfsExtension.Uninstall();
            PurrConsole.Unregister();
        }
    }

    [Test]
    public void Purr_Standing_Registration_Failure_Is_Reported()
    {
        var console = new SimConsoleHost { RegisterFailure = new InvalidOperationException("the game console does not exist yet") };
        Assert.That(PurrConsole.Register(console, out string message), Is.EqualTo(PurrCommandState.Failed));
        Assert.That(message, Is.EqualTo("InvalidOperationException: the game console does not exist yet"));
        Assert.That(PurrConsole.HasStandingCommand, Is.False);
        PurrConsole.Unregister();
        Assert.That(console.UnregisterCalls, Is.Zero);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertTerminalAction(Action<string> command)
    {
        var action = command.Method.GetCustomAttribute<Brutal.ImGuiApi.Abstractions.TerminalActionAttribute>();
        Assert.That(action, Is.Not.Null, "the console finds the command by its [TerminalAction]");
        Assert.That((action!.CommandName, action.ParsingMode), Is.EqualTo(("purr", Brutal.ImGuiApi.Abstractions.ArgParseMode.Unparsed)));
        Assert.That(action.Description, Is.EqualTo(PurrConsole.Description));
        Assert.That(command.Method.GetParameters().Select(p => (p.ParameterType, p.HasDefaultValue)), Is.EqualTo(new[] { (typeof(string), true) }),
            "Unparsed needs exactly one string parameter (Brutal's Command.CreateArgDefinitions)");
    }

    /// <summary>Game services with only a console (no game types).</summary>
    private sealed class ConsoleServices(IKfsConsoleHost console) : IKfsGameServices
    {
        public string Name => "console services";
        public string? Prepare() => null;
        public void Attach() { }
        public void Detach() { }
        public IKfsConsoleHost? Console { get; } = console;
    }

    // ------------------------------------------------------------------ the bench without its hook

    [Test]
    public void Bench_Without_Optional_Patch()
    {
        using var runtime = KfsRuntime.Start(new KfsRuntimeOptions
        {
            Extension = new KfsExtensionOptions { DisabledOptionalPatches = ["BenchApplyRequest"] },
        });
        KfsExtensionStatus status = runtime.Extension;
        Assert.That(status.IsActive, Is.True, status.Describe());
        Assert.That(status.OptionalPatches, Is.Empty);
        Assert.That(status.OptionalPatchesSkipped, Is.EqualTo(new[] { "BenchApplyRequest: switched off" }));
        Assert.That(PostfixOwners(BenchApplyRequestPatch.TargetMethod()!), Is.Empty);

        long claimed = BenchApplyRequestPatch.Claimed;
        var log = BenchProgram.Run(runtime);
        string dump = string.Join("\n", log);

        // The stock host: every call, getters included, rejected "no vehicle to act on".
        Assert.That(log, Has.Member("bench count=0 present=0"), dump);
        Assert.That(log, Has.Member("rejected x_light_set_on: no vehicle to act on"), dump);
        Assert.That(log, Has.Member("rejected x_light_count: no vehicle to act on"), dump);
        Assert.That(log, Has.Member("rejected x_light_geti: no vehicle to act on"), dump);
        Assert.That(log, Has.Member("bench rejected=3"), dump);
        Assert.That(log.Where(l => l.Contains(BenchApplyRequestPatch.Reason, StringComparison.Ordinal)), Is.Empty, dump);
        Assert.That(BenchApplyRequestPatch.Claimed, Is.EqualTo(claimed));
    }

    // ------------------------------------------------------------------ the runtime's lifetime

    /// <summary>
    /// The part of <c>Runtime_Misuse_Is_Explicit</c> (Runtime namespace) that needs a runtime of
    /// its own: a disposed runtime and its vehicles refuse to work, and the process is free for
    /// the next one.
    /// </summary>
    [Test]
    public void Disposed_Runtime_Is_Explicit()
    {
        var runtime = KfsRuntime.Start();
        Assert.That(KfsRuntime.Current, Is.SameAs(runtime));
        var second = Assert.Throws<InvalidOperationException>(() => KfsRuntime.Start());
        Assert.That(second!.Message, Does.Contain("already active in this process"));

        SimVehicle vehicle = runtime.Vehicle(lights: 1).LoadKc("#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nvoid run(void) { }\n");
        runtime.Dispose();
        Assert.DoesNotThrow(runtime.Dispose, "Dispose is idempotent");
        Assert.That((runtime.IsDisposed, KfsRuntime.Current), Is.EqualTo((true, (KfsRuntime?)null)));

        Assert.Throws<ObjectDisposedException>(() => runtime.Vehicle());
        Assert.Throws<ObjectDisposedException>(() => runtime.CreateVehicle());
        Assert.Throws<ObjectDisposedException>(() => runtime.CreateBench());
        Assert.Throws<ObjectDisposedException>(() => BuildWithDisposed(runtime, vehicle));
        Assert.Throws<ObjectDisposedException>(() => vehicle.Frame(), "the runtime disposed its vehicles");

        using var next = KfsRuntime.Start();
        Assert.That(KfsRuntime.Current, Is.SameAs(next));
    }

    private static void BuildWithDisposed(KfsRuntime runtime, SimVehicle vehicle) => runtime.BuildKc("void f(void) { }\n", vehicle.Layout);

    // ------------------------------------------------------------------ helpers (game types allowed in bodies and signatures)

    private InstallSeams Seams() => InstallSeams.Default with { Log = _log.Add };

    /// <summary>What <c>Program.PrepareFrame</c> calls once per frame in the game.</summary>
    private static void RunGameFrameHook() => KfsIntegration.BeforeInputEvents();

    private static VehicleComputer NewComputer(string name) => new FakeVehicle(KfsIntegration.Tables, 1, name).Computer;

    private static bool FindsNoHostFor(string name)
    {
        VehicleComputer computer = NewComputer(name);
        return PartsRegistry.Find(computer) is null && !PartsRegistry.TryGet(computer, out _);
    }

    /// <summary>A method Harmony cannot patch (no body).</summary>
    private static MethodBase? AbstractMethod() => typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose));

    /// <summary>Every registered patch is applied exactly once, by the test's Harmony.</summary>
    private void AssertEachPatchOnce()
    {
        foreach (KfsPatch patch in KfsExtension.Patches)
            Assert.That(PostfixOwners(patch.ResolveTarget()!), Is.EqualTo(new[] { _harmony.Id }), patch.Name);
    }

    private KfsExtensionStatus Install(KfsExtensionOptions? options = null) => KfsExtension.Install(_harmony, options, Seams());

    private static void ResetTables() => KfsIntegrationTables.Reset();

    private static bool TablesAreNull() => KfsIntegrationTables.IsNull;

    private static void SeedForeignTables() => KfsIntegrationTables.SeedStock();

    private static object CurrentTables() => KfsIntegrationTables.Read() ?? throw new AssertionException("KfsIntegration._tables is null");

    private static string? CurrentTablesOrigin() => KfsIntegrationTables.Read()?.Origin;

    private static bool IsCurrentTables(KfsRuntime runtime) => ReferenceEquals(KfsIntegrationTables.Read(), runtime.Tables);

    private static string StockLoaderOrigin() => GameTables.LoadEmbedded().Origin;

    private static string? HeaderText() =>
        KSA.Kfs.KC.SystemHeaders.Files.TryGetValue(XCatalog.HeaderName, out var text) ? text : null;

    private static List<string> MissingBuiltins() =>
        XCatalog.Services.Select(s => s.Name).Where(n => !KSA.Kfs.KC.SystemHeaders.Builtins.Contains(n)).ToList();

    /// <summary>The augmented tables are what <c>KfsIntegration.Tables</c> returns, and the field holds them.</summary>
    private static void AssertAugmentedTablesPublished()
    {
        GameTables tables = KfsIntegration.Tables;
        GameTables stock = KfsTables.Stock();
        Assert.That(tables.Origin, Is.EqualTo(TableAugmenter.OriginFor(XCatalog.Catalog)));
        Assert.That(tables.Services, Has.Count.EqualTo(stock.Services.Count + XCatalog.Services.Count));
        Assert.That(XCatalog.Services.Where(s => !tables.Services.ContainsKey(s.Id)).Select(s => s.Name), Is.Empty);
        Assert.That(ReferenceEquals(KfsIntegrationTables.Read(), tables), Is.True, "the field holds the instance the accessor returns");
        Assert.That(ReferenceEquals(KfsIntegration.Tables, tables), Is.True, "the same instance on every read");
    }

    private static void AssertFallbackTables()
    {
        GameTables tables = KfsIntegration.Tables;
        Assert.That(tables.Origin, Is.EqualTo(TableAugmenter.OriginFor(XCatalog.Catalog)));
        Assert.That(ReferenceEquals(tables, LoadEmbeddedFallbackPatch.Tables), Is.True, "the instance the installer built");
        Assert.That(ReferenceEquals(GameTables.LoadEmbedded(), tables), Is.True, "every LoadEmbedded call returns it");
    }

    private static string[] PostfixOwners(MethodBase target) =>
        Harmony.GetPatchInfo(target)?.Postfixes.Select(p => p.owner).ToArray() ?? [];

    private static List<string> PatchedBy(string owner) =>
        Harmony.GetAllPatchedMethods()
            .Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains(owner) == true)
            .Select(Describe).ToList();

    private static List<string> OurPatchesAnywhere()
    {
        Assembly ours = typeof(KfsExtension).Assembly;
        var found = new List<string>();
        foreach (MethodBase method in Harmony.GetAllPatchedMethods())
        {
            HarmonyLib.Patches? info = Harmony.GetPatchInfo(method);
            if (info is null) continue;
            foreach (Patch patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
            {
                if (patch.PatchMethod.DeclaringType?.Assembly == ours) found.Add($"{Describe(method)} by {patch.owner}");
            }
        }
        return found;
    }

    private static string Describe(MethodBase m) => $"{m.DeclaringType?.Name}.{m.Name}";
}
