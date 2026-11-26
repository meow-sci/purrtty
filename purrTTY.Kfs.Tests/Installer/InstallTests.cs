using System.Reflection;
using HarmonyLib;
using KSA.Kfs;
using KSA.Kfs.Host;
using KSA.Kfs.Vm.Tables;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
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
    }

    // ------------------------------------------------------------------ install / report

    [Test]
    public void Install_Activates_And_Reports()
    {
        KfsExtensionStatus status = Install();

        Assert.That(status.State, Is.EqualTo(KfsExtensionState.Active), status.Describe());
        Assert.That(status.Reason, Is.EqualTo(KfsDisabledReason.None));
        Assert.That(status.IsActive && KfsExtension.IsActive && KfsExtension.Status.IsActive, Is.True);
        Assert.That(status.RequiredPatches, Is.EqualTo(new[] { "ApplyRequest", "IssueResult" }));
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
            $"purrTTY kFS extension: active — {XCatalog.Services.Count} services 0x{status.FirstId:X4}–0x{status.LastId:X4}, 2 hooks (+1 optional), tables published"));
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
        Assert.That(_log.Single(), Does.Contain("2 hooks (+0 optional)"));
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
