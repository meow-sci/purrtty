using KSA.Kfs.Host;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// The frozen part lists (plan §4.2): parts that leave, parts that arrive, the dirty flag, rescans
/// in order with the commands around them, rebuilds for a new image's run, and the describe table.
/// </summary>
[TestFixture]
public sealed class PartListTests : RuntimeFixture
{
    [Test]
    public void Detach_Makes_Slot_Inert()
    {
        using SimVehicle v = Vehicle(lights: 2, deployables: 1);
        v.LoadKc(Kc.Ticked("""
                if (k == 2u) {
                    kfs_logf("gone %u %u %.3f %u %u %u %u", x_light_geti(1u, X_LIGHT_PRESENT), x_light_geti(1u, X_LIGHT_ON),
                             x_light_getf(1u, X_LIGHT_INTENSITY), x_deploy_geti(0u, X_DEPLOY_PRESENT),
                             x_deploy_geti(0u, X_DEPLOY_STATE), x_parts_info(X_PARTS_DIRTY), x_light_count());
                    kfs_logf("kept %u %.3f", x_light_geti(0u, X_LIGHT_PRESENT), x_light_getf(0u, X_LIGHT_INTENSITY));
                    x_light_set_on(1u, 1u);
                    x_deploy_set(0u, 1u);
                }
            """));
        v.Frame(0.1);                       // the run's first frame: its lists are frozen, tick 0 runs
        v.Parts.Detach(v.Parts.Light(1));
        v.Parts.Detach(v.Parts.Deployable(0));
        v.Run(0.4, 0.1);

        // The slot stays (indices do not shift), reads 0 (X_STATE_NONE for the state) and refuses writes.
        Assert.That(v.LogText, Has.Member("gone 0 0 0.000 0 0 1 2"), v.Dump);
        Assert.That(v.LogText, Has.Member("kept 1 2.000"), v.Dump);
        Assert.That(v.LogText, Has.Member("rejected x_light_set_on: light 1 is no longer on this vehicle"), v.Dump);
        Assert.That(v.LogText, Has.Member("rejected x_deploy_set: deployable 0 is no longer on this vehicle"), v.Dump);
        Assert.That(v.Parts.Applied, Is.Empty);
        Assert.That(v.Parts.Sets.Lights.Length, Is.EqualTo(2));
    }

    [Test]
    public void Detach_Between_Accept_And_Apply_Discards_The_Command()
    {
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(Kc.Ticked("if (k == 1u) { x_light_set_on(0u, 1u); }"));
        v.Frame(0.1);
        v.Frame(0.1);                       // tick 1: accepted and queued
        Assert.That(v.Binding.PendingCount, Is.EqualTo(1));
        v.Parts.Detach(v.Parts.Light(0));   // the part leaves before the pump runs
        v.Frame(0.1);                       // pump: discarded and reported; the report is written at this dispatch
        v.Frame(0.1);

        Assert.That((v.Binding.Discarded, v.Binding.Applied), Is.EqualTo((1L, 0L)));
        Assert.That(v.Parts.AllLights[0].On, Is.False);
        Assert.That(v.LogText, Has.Member("x_light_set_on: light 0 left the vehicle before the command was applied"), v.Dump);
    }

    [Test]
    public void Dirty_And_Rescan_Renumber()
    {
        using SimVehicle v = Vehicle(lights: 3);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    kfs_logf("before gen=%u dirty=%u count=%u id0=%u id1=%u", x_parts_info(X_PARTS_GENERATION),
                             x_parts_info(X_PARTS_DIRTY), x_light_count(), x_light_geti(0u, X_LIGHT_PART_ID), x_light_geti(1u, X_LIGHT_PART_ID));
                } else if (k == 2u) {
                    x_parts_rescan();
                } else if (k == 4u) {
                    kfs_logf("after gen=%u dirty=%u count=%u ids=%u,%u,%u,%u", x_parts_info(X_PARTS_GENERATION),
                             x_parts_info(X_PARTS_DIRTY), x_light_count(), x_light_geti(0u, X_LIGHT_PART_ID),
                             x_light_geti(1u, X_LIGHT_PART_ID), x_light_geti(2u, X_LIGHT_PART_ID), x_light_geti(3u, X_LIGHT_PART_ID));
                }
            """));
        v.Frame(0.1);                                                    // the run's lists are frozen; tick 0
        int generation = v.Parts.Sets.Generation;
        v.Parts.Detach(v.Parts.Light(0));                                // staged away
        v.Parts.AddLight(new SimLightSpec { Name = "Docked 1", PartId = 103 });   // docked on
        v.Parts.AddLight(new SimLightSpec { Name = "Docked 2", PartId = 104 });
        Assert.That(v.Parts.Dirty, Is.True);
        v.Run(0.5, 0.1);

        Assert.That(generation, Is.EqualTo(1));
        Assert.That(v.LogText, Has.Member("before gen=1 dirty=1 count=3 id0=0 id1=101"), v.Dump);
        Assert.That(v.LogText, Has.Member("parts rescanned: 4 lights, 0 deployables, 0 actuators (generation 2)"), v.Dump);
        Assert.That(v.LogText, Has.Member("after gen=2 dirty=0 count=4 ids=101,102,103,104"), v.Dump);
        Assert.That(v.Parts.Dirty, Is.False);
    }

    [Test]
    public void Rescan_Applies_In_Order()
    {
        var spec = new SimVehicleSpec { Lights = [new SimLightSpec { Name = "A" }, new SimLightSpec { Name = "B" }, new SimLightSpec { Name = "C" }] };
        using SimVehicle v = Vehicle(spec);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    x_light_set_intensity(1u, 7.0f);   /* B, before the rescan */
                    x_parts_rescan();                  /* A has left: B becomes 0, C becomes 1 */
                    x_light_set_range(1u, 5.0f);       /* still checked against the lists of this tick: B */
                } else if (k == 3u) {
                    kfs_logf("order %.3f %.3f %.3f %u %u", x_light_getf(0u, X_LIGHT_INTENSITY), x_light_getf(1u, X_LIGHT_INTENSITY),
                             x_light_getf(0u, X_LIGHT_RANGE_M), x_light_count(), x_parts_info(X_PARTS_GENERATION));
                }
            """));
        v.Frame(0.1);                       // the run's lists are frozen; tick 0
        SimLight a = v.Parts.Light(0), b = v.Parts.Light(1), c = v.Parts.Light(2);
        v.Parts.Detach(a);
        v.Run(0.4, 0.1);

        // Every command reaches the part it named when accepted, whatever the rescan between.
        Assert.That(v.Parts.Applied.Select(x => x.Command.Service), Is.EqualTo(new[] { Svc.LightSetIntensity, Svc.LightSetRange }));
        Assert.That((b.Intensity, b.RangeM), Is.EqualTo((7f, 5f)), v.Dump);
        Assert.That((c.Intensity, c.RangeM), Is.EqualTo((2f, 30f)));
        Assert.That(v.LogText, Has.Member("order 7.000 2.000 5.000 2 2"), v.Dump);
    }

    [Test]
    public void New_Image_Run_Rebuilds_Sets()
    {
        string program = Kc.Ticked("""
                if (k == 0u) { kfs_logf("load gen=%u lights=%u dirty=%u", x_parts_info(X_PARTS_GENERATION), x_light_count(), x_parts_info(X_PARTS_DIRTY)); }
            """);
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(program).Run(0.2, 0.1);
        Assert.That(v.LogText, Has.Member("load gen=1 lights=2 dirty=0"), v.Dump);
        var firstSlots = v.Parts.Sets.Lights;

        v.Parts.AddLight();
        Assert.That((v.Parts.Dirty, v.Parts.Sets.Lights.Length), Is.EqualTo((true, 2)), "no rebuild until the next run or rescan");

        v.LoadKc(program).Run(0.2, 0.1);
        Assert.That(v.LogText, Has.Member("load gen=2 lights=3 dirty=0"), v.Dump);
        Assert.That(v.Parts.Sets.Generation, Is.EqualTo(2));
        Assert.That(v.Parts.Sets.Lights[0], Is.Not.SameAs(firstSlots[0]), "fresh slots for the new image");
    }

    /// <summary>
    /// Review C7: binding is once per computer, and binding never wraps the game's
    /// <c>ImageLoaded</c> delegate (the host owns it; new runs are detected without it), so
    /// repeated Bind → Unbind cycles cannot grow a chain holding dead bindings.
    /// </summary>
    [Test]
    public void Bind_Is_Once_And_Leaves_ImageLoaded_Alone()
    {
        var fake = new FakeVehicle(Runtime.Tables, 1, "chain");
        int previousCalls = 0;
        Action previous = () => previousCalls++;
        fake.Computer.ImageLoaded = previous;
        var parts = new SimVehicleParts(lights: 1);
        for (int cycle = 0; cycle < 5; cycle++)
        {
            VehicleBinding binding = PartsRegistry.Bind(fake.Computer, parts);
            Assert.That(PartsRegistry.Bind(fake.Computer, new SimVehicleParts(lights: 5)), Is.SameAs(binding), "a second bind returns the first binding");
            Assert.That(PartsRegistry.Find(fake.Computer), Is.SameAs(binding));
            Assert.That(PartsRegistry.Live.Count(b => ReferenceEquals(b.Computer, fake.Computer)), Is.EqualTo(1));
            Assert.That(ReferenceEquals(fake.Computer.ImageLoaded, previous), Is.True, $"cycle {cycle}: ImageLoaded untouched");
            Assert.That(PartsRegistry.Unbind(fake.Computer), Is.True);
            Assert.That(binding.IsBound, Is.False);
            Assert.That(PartsRegistry.Unbind(fake.Computer), Is.False);
        }
        Assert.That(fake.Computer.ImageLoaded!.GetInvocationList(), Has.Length.EqualTo(1));
        Assert.That(PartsRegistry.Find(fake.Computer), Is.Null);

        BuildResult build = Runtime.BuildKc(Kc.Ticked(""), fake.Layout);
        Assert.That(build.Ok, Is.True, build.Text);
        Assert.That(fake.Computer.Load(build.Image!, fake.Layout).Ok, Is.True);
        Assert.That(previousCalls, Is.EqualTo(1), "the game's delegate still runs, once");
    }

    [Test]
    public void Describe_Writes_The_Table_And_Is_Rate_Limited()
    {
        var spec = new SimVehicleSpec
        {
            Lights = [new SimLightSpec { Name = "Spot A" }, new SimLightSpec { Name = "Cabin", Type = LightType.Point, Interior = true }],
            Deployables = [new SimAnimationSpec { Name = "Solar", DurationS = 2.0, Links = AnimSlot.LinkSolarPanel }],
            Actuators = [new SimAnimationSpec { Name = "Bay", DurationS = 1.0, InitialFraction = 0.5 }],
        };
        using SimVehicle v = Vehicle(spec);
        v.LoadKc(Kc.Ticked("""
                if (k == 0u) { x_parts_describe(X_KIND_ALL); }
                if (k == 1u) { x_parts_describe(X_KIND_LIGHT); }
                if (k == 10u) { x_parts_describe(X_KIND_ACTUATOR); }
            """));
        v.Run(1.2, 0.1);

        var table = v.LogText.Where(t => t.StartsWith("parts (", StringComparison.Ordinal) || t.Contains(" (part ", StringComparison.Ordinal)).ToList();
        Assert.That(table, Is.EqualTo(new[]
        {
            "parts (generation 1): 2 lights, 1 deployable, 1 actuator",
            "light 0: Spot A (part 100) spot, switch group 0, off",
            "light 1: Cabin (part 101) point, no switch",
            "deployable 0: Solar (part 200) retracted, 2 s, drives a solar panel",
            "actuator 0: Bay (part 300) at 0.50 of 1 s",
            // tick 1 is in the same second as tick 0: rejected; tick 10 starts the next second
            "parts (generation 1): 2 lights, 1 deployable, 1 actuator",
            "actuator 0: Bay (part 300) at 0.50 of 1 s",
        }), v.Dump);
        Assert.That(v.LogText, Has.Member("rejected x_parts_describe: at most one table per second"), v.Dump);
        Assert.That(v.Binding.Rejected, Is.EqualTo(1));
    }
}
