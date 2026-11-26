using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// What a new run of a program gets (plan §4.2): fresh lists and fresh rate limits on every start,
/// reset and stop → start, not only on an image load; rescans coalesced to one rebuild per pump;
/// <c>x_deploy_toggle</c> from rest and mid-travel.
/// </summary>
[TestFixture]
public sealed class RunLifecycleTests : RuntimeFixture
{
    // ------------------------------------------------------------------ rate limits per run (review B1)

    [Test]
    public void Describe_In_The_First_Tick_Works_On_Every_Run()
    {
        string program = Kc.Ticked("if (k == 0u) { x_parts_describe(X_KIND_LIGHT); }");
        using SimVehicle v = Vehicle(lights: 1);

        v.LoadKc(program).Run(0.3, 0.1);
        v.LoadKc(program).Run(0.3, 0.1);   // reloaded and started: k restarts at 0
        v.Reset().Run(0.3, 0.1);           // kfs reset: a new run without a load
        v.Stop().Start().Run(0.3, 0.1);    // kfs stop, kfs start

        Assert.That(v.LogText.Count(t => t.StartsWith("parts (generation ", StringComparison.Ordinal)), Is.EqualTo(4), v.Dump);
        Assert.That(v.LogText, Has.None.Contains("rejected"), v.Dump);
        Assert.That(v.Binding.Rejected, Is.Zero);
    }

    [Test]
    public void Pump_Reports_Are_Not_Lost_On_A_New_Run()
    {
        // Each run: a rescan in tick 0 (reported by the pump) and a command whose part leaves
        // before the pump applies it (reported as discarded).
        string program = Kc.Ticked("if (k == 0u) { x_parts_rescan(); x_light_set_on(1u, 1u); }");
        using SimVehicle v = Vehicle(lights: 4);
        for (int run = 0; run < 3; run++)
        {
            if (run == 0) v.LoadKc(program, start: true);
            else v.Reset();
            v.Frame(0.1);                                   // pump (new run), dispatch: tick 0 queues both
            v.Parts.Detach(v.Parts.Light(1));               // light 1 of this run's lists leaves
            v.Run(0.3, 0.1);                                // pump applies the rescan, discards the setter
        }

        Assert.That(v.LogText.Count(t => t.StartsWith("parts rescanned: ", StringComparison.Ordinal)), Is.EqualTo(3), v.Dump);
        Assert.That(v.LogText.Count(t => t == "x_light_set_on: light 1 left the vehicle before the command was applied"), Is.EqualTo(3), v.Dump);
        Assert.That(v.Binding.Discarded, Is.EqualTo(3));
    }

    // ------------------------------------------------------------------ fresh lists per run (review B2)

    [Test]
    public void Every_Run_Starts_From_Fresh_Lists()
    {
        string program = Kc.Ticked("""
                if (k == 0u) {
                    kfs_logf("run gen=%u lights=%u present0=%u id0=%u dirty=%u", x_parts_info(X_PARTS_GENERATION), x_light_count(),
                             x_light_geti(0u, X_LIGHT_PRESENT), x_light_geti(0u, X_LIGHT_PART_ID), x_parts_info(X_PARTS_DIRTY));
                }
            """);
        using SimVehicle v = Vehicle(lights: 4);
        v.LoadKc(program).Run(0.3, 0.1);

        v.Parts.Detach(v.Parts.Light(0));        // staged away during the first run: a gap
        v.Run(0.2, 0.1);
        Assert.That(v.Parts.Sets.Lights.Length, Is.EqualTo(4), "within a run the lists stay frozen");
        v.Reset().Run(0.3, 0.1);                  // kfs reset, no reload

        v.Parts.Detach(v.Parts.Light(0));
        v.Stop().Run(0.2, 0.1);                   // stopped: no ticks
        v.Start().Run(0.3, 0.1);                  // kfs start after kfs stop

        v.Parts.Detach(v.Parts.Light(0));
        v.Start().Run(0.3, 0.1);                  // kfs start while running

        var runs = v.LogText.Where(t => t.StartsWith("run ", StringComparison.Ordinal)).ToList();
        Assert.That(runs, Is.EqualTo(new[]
        {
            "run gen=1 lights=4 present0=1 id0=100 dirty=0",
            "run gen=2 lights=3 present0=1 id0=101 dirty=0",
            "run gen=3 lights=2 present0=1 id0=102 dirty=0",
            "run gen=4 lights=1 present0=1 id0=103 dirty=0",
        }), v.Dump);
    }

    [Test]
    public void A_Stopped_Computer_Keeps_Its_Lists()
    {
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(Kc.Ticked("if (k == 0u) { kfs_logf(\"n %u\", x_light_count()); }")).Run(0.2, 0.1);
        int generation = v.Parts.Sets.Generation;
        v.Parts.AddLight();
        v.Stop().Run(0.5, 0.1);

        Assert.That(v.Parts.Sets.Generation, Is.EqualTo(generation), "no run, no rebuild");
        Assert.That(v.Parts.Sets.Lights.Length, Is.EqualTo(2));
    }

    // ------------------------------------------------------------------ rescans coalesced (review C1)

    [Test]
    public void Rescans_Of_One_Tick_Rebuild_Once()
    {
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    for (uint32_t i = 0u; i < 40u; i++) { x_parts_rescan(); }
                } else if (k == 3u) {
                    kfs_logf("gen %u", x_parts_info(X_PARTS_GENERATION));
                }
            """));
        v.Run(0.2, 0.1);                          // ticks 0 and 1: the lists of the run, then 40 rescans queued
        int builds = v.Parts.Builds, generation = v.Parts.Sets.Generation;
        Assert.That(v.Binding.PendingCount, Is.EqualTo(40));

        v.Run(0.3, 0.1);

        Assert.That(v.Parts.Builds - builds, Is.EqualTo(1), "one rebuild for the whole batch");
        Assert.That(v.Parts.Sets.Generation, Is.EqualTo(generation + 1));
        Assert.That(v.LogText, Has.Member($"gen {generation + 1}"), v.Dump);
        Assert.That(v.LogText.Count(t => t.StartsWith("parts rescanned: ", StringComparison.Ordinal)), Is.EqualTo(1), v.Dump);
        Assert.That(v.Binding.Applied, Is.EqualTo(40), "every rescan is satisfied by the one rebuild");
    }

    [Test]
    public void Rescans_Coalesce_Per_Drain_Not_Across_Drains()
    {
        using SimVehicle v = Vehicle(lights: 1);
        int generation = v.Parts.Sets.Generation;
        for (int i = 0; i < 100; i++) v.Binding.Enqueue(new PartCommand(Svc.PartsRescan, null));
        Assert.That(BindingPump.DrainAndApply(v.Binding), Is.EqualTo(100));
        Assert.That(v.Parts.Sets.Generation, Is.EqualTo(generation + 1));

        v.Binding.Enqueue(new PartCommand(Svc.PartsRescan, null));
        BindingPump.DrainAndApply(v.Binding);
        Assert.That(v.Parts.Sets.Generation, Is.EqualTo(generation + 2), "the next drain rebuilds again");
    }

    // ------------------------------------------------------------------ x_deploy_toggle

    [Test]
    public void Deploy_Toggle_From_Rest_And_Mid_Travel()
    {
        // 2 s deployable; a command of tick k is applied by the pump of frame k + 1, which then steps
        // 0.1 s. Tick 1: toggle (retracted → deploying). Tick 23 (deployed since tick 21): toggle
        // back. Tick 33, halfway through the retraction: toggle again (→ deploying from 1 s).
        using SimVehicle v = Vehicle(deployables: 1);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u || k == 23u || k == 33u) { x_deploy_toggle(0u); }
                s = x_deploy_geti(0u, X_DEPLOY_STATE);
                if (s != last) {
                    kfs_logf("toggle k=%u state=%u pos=%.2f target=%.2f", k, s, x_deploy_getf(0u, X_DEPLOY_POSITION), x_deploy_getf(0u, X_DEPLOY_TARGET));
                    last = s;
                }
            """, "uint32_t s;\nuint32_t last = 99u;\n"));
        v.Run(5.0, 0.1);

        Assert.That(v.LogText.Where(t => t.StartsWith("toggle k=", StringComparison.Ordinal)), Is.EqualTo(new[]
        {
            "toggle k=0 state=2 pos=0.00 target=0.00",    // X_STATE_RETRACTED
            "toggle k=2 state=3 pos=0.05 target=1.00",    // X_STATE_DEPLOYING
            "toggle k=21 state=1 pos=1.00 target=1.00",   // X_STATE_DEPLOYED
            "toggle k=24 state=4 pos=0.95 target=0.00",   // X_STATE_RETRACTING, from deployed
            "toggle k=34 state=3 pos=0.55 target=1.00",   // X_STATE_DEPLOYING again, from mid-travel
            "toggle k=43 state=1 pos=1.00 target=1.00",
        }), v.Dump);
        Assert.That(v.Parts.Applied.Select(a => a.Command.Service), Is.EqualTo(new[] { Svc.DeployToggle, Svc.DeployToggle, Svc.DeployToggle }));
        Assert.That(v.Parts.Deployable(0).GoalS, Is.EqualTo(2.0));
    }
}
