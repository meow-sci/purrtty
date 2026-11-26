using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Patches;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// Every service of the catalogue compiled by the game's kC compiler (or kASM assembler), run by
/// the game's VM on a <c>FakeVehicle</c>, delivered by the two host hooks to the router and applied
/// to simulated parts — plus the rejections at both levels and the bench.
/// </summary>
[TestFixture]
public sealed class ServiceTests : RuntimeFixture
{
    private sealed record ServiceCase(string Body, Action<SimVehicle> Check);

    /// <summary>The vehicle every service case runs on.</summary>
    private static SimVehicleSpec EveryServiceSpec() => new()
    {
        Name = "every",
        Lights =
        [
            new SimLightSpec { Name = "Spot A", Key = "spot_a" },
            new SimLightSpec { Name = "Spot B", Key = "spot_b", Intensity = 4f },
            new SimLightSpec { Name = "Flood", Key = "flood", Type = LightType.Point, Intensity = 6f },
        ],
        Deployables =
        [
            new SimAnimationSpec { Name = "Solar A", DurationS = 2.0, Links = AnimSlot.LinkSolarPanel },
            new SimAnimationSpec { Name = "Solar B", DurationS = 4.0 },
        ],
        Actuators = [new SimAnimationSpec { Name = "Bay", DurationS = 1.5 }],
    };

    /// <summary>
    /// One case per service: the statements run in tick 1 (<c>k == 1u</c>) and, after a
    /// <c>|</c>, in tick 3; the check runs after half a second at 10 Hz.
    /// </summary>
    private static readonly Dictionary<string, ServiceCase> Cases = new(StringComparer.Ordinal)
    {
        ["x_parts_info"] = new(
            """kfs_logf("r %u %u %u %u %u", x_parts_info(X_PARTS_GENERATION), x_parts_info(X_PARTS_DIRTY), x_parts_info(X_PARTS_LIGHTS), x_parts_info(X_PARTS_DEPLOYABLES), x_parts_info(X_PARTS_ACTUATORS));""",
            v => Logged(v, "r 1 0 3 2 1")),
        ["x_parts_rescan"] = new(
            """x_parts_rescan(); | kfs_logf("r %u", x_parts_info(X_PARTS_GENERATION));""",
            v =>
            {
                Assert.That(v.Parts.Sets.Generation, Is.EqualTo(2));
                Logged(v, "r 2");
                Logged(v, "parts rescanned: 3 lights, 2 deployables, 1 actuator (generation 2)");
            }),
        ["x_parts_describe"] = new(
            "x_parts_describe(X_KIND_LIGHT);",
            v =>
            {
                Logged(v, "parts (generation 1): 3 lights, 2 deployables, 1 actuator");
                Logged(v, "light 0: Spot A (part 100) spot, switch group 0, off");
                Logged(v, "light 1: Spot B (part 101) spot, switch group 1, off");
                Logged(v, "light 2: Flood (part 102) point, switch group 2, off");
                Assert.That(v.LogText, Has.None.StartsWith("deployable "), "X_KIND_LIGHT lists lights only");
            }),
        ["x_light_count"] = new("""kfs_logf("r %u", x_light_count());""", v => Logged(v, "r 3")),
        ["x_light_geti"] = new(
            """kfs_logf("r %u %u %u", x_light_geti(2u, X_LIGHT_PART_ID), x_light_geti(2u, X_LIGHT_TYPE), x_light_geti(1u, X_LIGHT_HAS_SWITCH));""",
            v => Logged(v, "r 102 1 1")),
        ["x_light_getf"] = new(
            """kfs_logf("r %.3f %.3f", x_light_getf(1u, X_LIGHT_STOCK_INTENSITY), x_light_getf(0u, X_LIGHT_OUTER_RAD));""",
            v => Logged(v, "r 4.000 0.600")),
        ["x_light_set_on"] = new(
            "x_light_set_on(1u, 1u);",
            v =>
            {
                var c = OnlyCommand(v, Svc.LightSetOn);
                Assert.That((c.Slot?.Index, c.U), Is.EqualTo(((int?)1, 1u)));
                Assert.That(v.Parts.Lights.Select(l => l.On), Is.EqualTo(new[] { false, true, false }));
            }),
        ["x_light_set_color"] = new(
            "c.x = 0.25f; c.y = 0.5f; c.z = 0.75f; x_light_set_color(0u, c);",
            v =>
            {
                var c = OnlyCommand(v, Svc.LightSetColor);
                Assert.That((c.Slot?.Index, c.A, c.B, c.C), Is.EqualTo(((int?)0, 0.25f, 0.5f, 0.75f)));
                SimLight l = v.Parts.Light(0);
                Assert.That((l.R, l.G, l.B, l.Overridden), Is.EqualTo((0.25f, 0.5f, 0.75f, true)));
            }),
        ["x_light_set_cone"] = new(
            "x_light_set_cone(0u, 0.1f, 0.4f);",
            v =>
            {
                var c = OnlyCommand(v, Svc.LightSetCone);
                Assert.That((c.A, c.B), Is.EqualTo((0.1f, 0.4f)));
                Assert.That((v.Parts.Light(0).InnerRad, v.Parts.Light(0).OuterRad), Is.EqualTo((0.1f, 0.4f)));
            }),
        ["x_light_set_intensity"] = new(
            "x_light_set_intensity(2u, 7.5f);",
            v =>
            {
                Assert.That(OnlyCommand(v, Svc.LightSetIntensity).A, Is.EqualTo(7.5f));
                Assert.That(v.Parts.Light(2).Intensity, Is.EqualTo(7.5f));
                Assert.That(v.Parts.Light(0).Intensity, Is.EqualTo(2f), "other lights untouched");
            }),
        ["x_light_set_range"] = new(
            "x_light_set_range(1u, 12.5f);",
            v =>
            {
                Assert.That(OnlyCommand(v, Svc.LightSetRange).A, Is.EqualTo(12.5f));
                Assert.That(v.Parts.Light(1).RangeM, Is.EqualTo(12.5f));
            }),
        ["x_light_reset"] = new(
            "x_light_set_intensity(0u, 9.0f); x_light_reset(0u);",
            v =>
            {
                Assert.That(v.Parts.Applied.Select(a => a.Command.Service), Is.EqualTo(new[] { Svc.LightSetIntensity, Svc.LightReset }));
                Assert.That((v.Parts.Light(0).Intensity, v.Parts.Light(0).Overridden), Is.EqualTo((2f, false)));
            }),
        ["x_lights_set_all"] = new(
            "x_lights_set_all(1u);",
            v =>
            {
                Assert.That(OnlyCommand(v, Svc.LightsSetAll).U, Is.EqualTo(1u));
                Assert.That(v.Parts.Lights.Select(l => l.On), Is.All.True);
            }),
        ["x_deploy_count"] = new("""kfs_logf("r %u", x_deploy_count());""", v => Logged(v, "r 2")),
        ["x_deploy_geti"] = new(
            """kfs_logf("r %u %u %u %u", x_deploy_geti(1u, X_DEPLOY_PART_ID), x_deploy_geti(0u, X_DEPLOY_STATE), x_deploy_geti(0u, X_DEPLOY_LINKS), x_deploy_geti(0u, X_DEPLOY_PRESENT));""",
            v => Logged(v, "r 201 2 1 1")),   // X_STATE_RETRACTED
        ["x_deploy_getf"] = new(
            """kfs_logf("r %.3f %.3f", x_deploy_getf(1u, X_DEPLOY_DURATION_S), x_deploy_getf(1u, X_DEPLOY_TARGET));""",
            v => Logged(v, "r 4.000 0.000")),
        ["x_deploy_set"] = new(
            "x_deploy_set(1u, 1u);",
            v =>
            {
                Assert.That(OnlyCommand(v, Svc.DeploySet).Slot?.Index, Is.EqualTo(1));
                Assert.That(v.Parts.Deployable(1).GoalS, Is.EqualTo(4.0));
                Assert.That(v.Parts.Deployable(0).GoalS, Is.EqualTo(0.0));
            }),
        ["x_deploy_toggle"] = new(
            "x_deploy_toggle(0u);",
            v =>
            {
                Assert.That(OnlyCommand(v, Svc.DeployToggle).Slot?.Index, Is.EqualTo(0));
                Assert.That(v.Parts.Deployable(0).GoalS, Is.EqualTo(2.0));
            }),
        ["x_actuator_count"] = new("""kfs_logf("r %u", x_actuator_count());""", v => Logged(v, "r 1")),
        ["x_actuator_geti"] = new(
            """kfs_logf("r %u %u", x_actuator_geti(0u, X_ACTUATOR_PART_ID), x_actuator_geti(0u, X_ACTUATOR_MOVING));""",
            v => Logged(v, "r 300 0")),
        ["x_actuator_getf"] = new(
            """kfs_logf("r %.3f", x_actuator_getf(0u, X_ACTUATOR_DURATION_S));""",
            v => Logged(v, "r 1.500")),
        ["x_actuator_set"] = new(
            "x_actuator_set(0u, 0.25f);",
            v =>
            {
                Assert.That(OnlyCommand(v, Svc.ActuatorSet).A, Is.EqualTo(0.25f));
                Assert.That(v.Parts.Actuator(0).GoalS, Is.EqualTo(0.375));
            }),
    };

    private static IEnumerable<string> ServiceNames() => XCatalog.Services.Select(s => s.Name);

    [Test]
    public void Every_Service_Has_A_Case() =>
        Assert.That(Cases.Keys, Is.EquivalentTo(XCatalog.Services.Select(s => s.Name)));

    [TestCaseSource(nameof(ServiceNames))]
    public void Every_Service_Is_Callable(string name)
    {
        ServiceCase test = Cases[name];
        string[] parts = test.Body.Split('|');
        string body = $"    if (k == 1u) {{ {parts[0]} }}\n" + (parts.Length > 1 ? $"    if (k == 3u) {{ {parts[1]} }}\n" : "");

        using SimVehicle v = Runtime.CreateVehicle(EveryServiceSpec());
        v.LoadKc(Kc.Ticked(body, "vec3 c;\n")).Run(0.5, 0.1);

        Assert.That(v.LogText, Has.None.Contains("rejected"), v.Dump);
        Assert.That((v.Binding.RouterFaults, v.Binding.ApplyFailures, v.Binding.Rejected), Is.EqualTo((0L, 0L, 0L)), v.Dump);
        ServiceDef def = XCatalog.Catalog.ByName(name)!;
        if (def.HasResult) Assert.That(v.Binding.Answered, Is.GreaterThan(0), "answered by the IssueResult hook");
        else Assert.That(v.Binding.Accepted, Is.GreaterThan(0), "accepted by the ApplyRequest hook");
        test.Check(v);
    }

    [Test]
    public void Kasm_Symbols_Resolve()
    {
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKasm("""
            .include <kfs.kinc>
            .include "purrtty.kinc"
            .tick_hz 10
            .global done, 4
            .global count, 4

            .task main rate_hz=10
            .func main
                LDG   done
                JNZ   out
                SYSCALL SVC_X_LIGHT_COUNT       ; pops 0, pushes 1
                STG   count
                PUSH8 1                         ; u32 light (deepest)
                PUSH8 1                         ; u32 on
                SYSCALL SVC_X_LIGHT_SET_ON      ; pops 2
                PUSH8 X_KIND_LIGHT
                SYSCALL SVC_X_PARTS_DESCRIBE
                PUSH8 1
                STG   done
            out:
                RET   0
            .endfunc

            """).Run(0.3, 0.1);

        Assert.That(v.Parts.Lights.Select(l => l.On), Is.EqualTo(new[] { false, true }), v.Dump);
        Assert.That(OnlyCommand(v, Svc.LightSetOn).Slot?.Index, Is.EqualTo(1));
        Assert.That(v.Binding.Answered, Is.EqualTo(1), "SVC_X_LIGHT_COUNT answered once");
        Logged(v, "light 1: Light 1 (part 101) spot, switch group 1, off");
    }

    [Test]
    public void Domains_Reject()
    {
        var spec = new SimVehicleSpec
        {
            Lights =
            [
                new SimLightSpec { Name = "Spot" },
                new SimLightSpec { Name = "Cabin", Type = LightType.Point, Interior = true },
                new SimLightSpec { Name = "Spot 2" },
            ],
            Deployables = [new SimAnimationSpec { Name = "Solar" }],
            Actuators = [new SimAnimationSpec { Name = "Bay", DurationS = 1.0 }],
        };
        using SimVehicle v = Vehicle(spec);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    r0 = sys.rejected_requests;
                    x_light_set_on(99u, 1u);
                    x_deploy_set(7u, 1u);
                    x_actuator_set(5u, 0.5f);
                    x_light_set_on(2u, 1u);
                    x_light_set_on(1u, 1u);
                    x_light_set_intensity(2u, 1.0f);
                } else if (k == 2u) {
                    kfs_logf("host before=%u after=%u", r0, sys.rejected_requests);
                    r0 = sys.rejected_requests;
                    kfs_logf("sel %u %.3f %u %u", x_light_geti(0u, X_LIGHT_INTENSITY), x_light_getf(0u, X_LIGHT_ON),
                             x_light_geti(0u, 9u), x_light_geti(0u, 99u));
                    kfs_logf("sel2 %u %.3f", x_deploy_geti(0u, X_DEPLOY_POSITION), x_actuator_getf(0u, X_ACTUATOR_MOVING));
                    kfs_logf("sel same-tick=%u", sys.rejected_requests - r0);
                } else if (k == 3u) {
                    r0 = sys.rejected_requests;
                    nanbits = 0x7FC00000u;
                    x_actuator_set(0u, *(float *)&nanbits);
                    x_light_set_intensity(0u, -1.0f);
                    c.x = -0.5f; c.y = 0.0f; c.z = 0.0f;
                    x_light_set_color(0u, c);
                    kfs_logf("issue same-tick=%u", sys.rejected_requests - r0);
                } else if (k == 4u) {
                    kfs_logf("probe present=%u", x_light_geti(99u, X_LIGHT_PRESENT));
                }
            """, "uint32_t r0;\nuint32_t nanbits;\nvec3 c;\n"));
        v.Frame(0.1);                       // the run's lists are frozen; tick 0
        v.Parts.Detach(v.Parts.Light(2));
        v.Run(0.5, 0.1);

        // Host rejections (plan §4.9), visible to the program from the next tick.
        Logged(v, "rejected x_light_set_on: light 99 out of range (this vehicle has 3)");
        Logged(v, "rejected x_deploy_set: deployable 7 out of range (this vehicle has 1)");
        Logged(v, "rejected x_actuator_set: actuator 5 out of range (this vehicle has 1)");
        Logged(v, "rejected x_light_set_on: light 2 is no longer on this vehicle");
        Logged(v, "rejected x_light_set_on: light 1 has no switch");
        Logged(v, "rejected x_light_set_intensity: light 2 is no longer on this vehicle");
        Logged(v, "host before=0 after=6");
        Assert.That(v.Binding.Rejected, Is.EqualTo(6));

        // Issue-time rejections by the VM (argument domains), counted in the same tick, reading 0.
        Logged(v, "rejected x_light_geti argument 1 = 16: not in x_light_prop[0..7]");
        Logged(v, "rejected x_light_getf argument 1 = 2: not in x_light_prop[16..23]");
        Logged(v, "rejected x_light_geti argument 1 = 9: not in x_light_prop[0..7]");
        Logged(v, "rejected x_light_geti argument 1 = 99: not in x_light_prop[0..7]");
        Logged(v, "rejected x_deploy_geti argument 1 = 16: not in x_deploy_prop[0..5]");
        Logged(v, "rejected x_actuator_getf argument 1 = 1: not in x_actuator_prop[16..18]");
        Logged(v, "sel 0 0.000 0 0");
        Logged(v, "sel2 0 0.000");
        Logged(v, "sel same-tick=6");
        Logged(v, "rejected x_actuator_set argument 1 = nan: not finite");
        Logged(v, "rejected x_light_set_intensity argument 1 = -1: below 0");
        Assert.That(v.LogText, Has.Some.Contains("rejected x_light_set_color argument 1").And.Contains("below 0"), v.Dump);
        Logged(v, "issue same-tick=3");
        Logged(v, "probe present=0");

        // Nothing rejected reached the parts.
        Assert.That(v.Parts.Applied, Is.Empty);
        Assert.That((v.Parts.Light(0).Intensity, v.Parts.Light(0).R, v.Parts.Actuator(0).GoalS), Is.EqualTo((2f, 1f, 0.0)));
        Assert.That(v.Parts.AllLights.Select(l => l.On), Is.EqualTo(new[] { false, true, false }), "the interior light reads on, nothing was switched");
    }

    /// <summary>Review C8: the largest float the VM admits (finite, >= 0) reaches a light as the documented ceiling.</summary>
    [Test]
    public void Intensity_And_Range_Have_A_Ceiling()
    {
        using SimVehicle v = Vehicle(lights: 1);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    x_light_set_intensity(0u, 3.4028235e38f);
                    x_light_set_range(0u, 3.4028235e38f);
                } else if (k == 3u) {
                    kfs_logf("ceil %.1f %.1f", x_light_getf(0u, X_LIGHT_INTENSITY), x_light_getf(0u, X_LIGHT_RANGE_M));
                }
            """));
        v.Run(0.5, 0.1);

        Assert.That(v.LogText, Has.None.Contains("rejected"), v.Dump);
        Assert.That((v.Parts.Light(0).Intensity, v.Parts.Light(0).RangeM), Is.EqualTo((1e6f, 1e6f)), v.Dump);
        Logged(v, "ceil 1000000.0 1000000.0");
    }

    [Test]
    public void Bench_Is_Readable()
    {
        // The runtime installs the optional bench hook (KfsExtension, BenchApplyRequestPatch).
        Assert.That(Runtime.Extension.OptionalPatches, Has.Member("BenchApplyRequest"));
        long claimed = BenchApplyRequestPatch.Claimed, rejected = BenchApplyRequestPatch.Rejected;

        var log = BenchProgram.Run(Runtime);
        string dump = string.Join("\n", log);

        // Getters: claimed silently, result 0 (no syscall host). Setters: the bench text and the
        // rejected-request counter, exactly as the host's own rejection would write them.
        Assert.That(log, Has.Member("bench count=0 present=0"), dump);
        Assert.That(log, Has.Member("rejected x_light_set_on: " + BenchApplyRequestPatch.Reason), dump);
        Assert.That(log, Has.Member("bench rejected=1"), dump);
        Assert.That(log.Where(l => l.StartsWith("rejected x_light_count", StringComparison.Ordinal)
                                   || l.StartsWith("rejected x_light_geti", StringComparison.Ordinal)), Is.Empty, dump);
        Assert.That(log.Where(l => l.Contains("no vehicle to act on", StringComparison.Ordinal)), Is.Empty, dump);
        Assert.That(BenchApplyRequestPatch.Claimed - claimed, Is.EqualTo(3), "two getters and a setter claimed");
        Assert.That(BenchApplyRequestPatch.Rejected - rejected, Is.EqualTo(1), "only the setter rejected");
    }

    [Test]
    public void Bench_Patch_Never_Fires_For_A_VehicleComputer()
    {
        long calls = BenchApplyRequestPatch.Calls;

        // Mod setters and getters, and a stock request the vehicle host applies itself.
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    x_light_set_on(0u, 1u);
                    kfs_logf("n %u", x_light_count());
                    x_parts_rescan();
                    kfs_set_throttle(0.5f);
                }
            """));
        v.Run(0.5, 0.1);
        Assert.That(v.StockRequests.Select(r => r.Id), Has.Member((ushort)52), v.Dump);
        Assert.That(v.Parts.Light(0).On, Is.True, v.Dump);
        Assert.That(v.LogText, Has.Member("n 2"), v.Dump);

        Assert.That(BenchApplyRequestPatch.Calls, Is.EqualTo(calls), "the base HostComputer.ApplyRequest never ran for the vehicle");
        Assert.That(BenchApplyRequestPatch.VehicleComputerCalls, Is.Zero);

        // Positive control: the hook is live in this process and does fire for the bench.
        BenchProgram.Run(Runtime);
        Assert.That(BenchApplyRequestPatch.Calls, Is.GreaterThan(calls));
        Assert.That(BenchApplyRequestPatch.VehicleComputerCalls, Is.Zero);
    }

    // ------------------------------------------------------------------ helpers

    private static void Logged(SimVehicle v, string line) => Assert.That(v.LogText, Has.Member(line), v.Dump);

    private static PartCommand OnlyCommand(SimVehicle v, ushort service)
    {
        var commands = v.Parts.Applied.Where(a => a.Command.Service == service).Select(a => a.Command).ToList();
        Assert.That(commands, Has.Count.EqualTo(1), v.Dump);
        return commands[0];
    }
}
