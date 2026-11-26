using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// The scenarios of spike 06, with exact state sequences: blinking, the light show, deploying,
/// an actuator sweep, read-back timing, shared switches, two vehicles, stock requests.
/// </summary>
[TestFixture]
public sealed class ScenarioTests : RuntimeFixture
{
    [Test]
    public void Blink_Toggles()
    {
        var spec = SimVehicleSpec.With(lights: 3) with
        {
            Lights = [new SimLightSpec { Name = "A" }, new SimLightSpec { Name = "B" }, new SimLightSpec { Name = "A2", SharesSwitchWith = 0 }],
        };
        using SimVehicle v = Vehicle(spec);
        v.LoadKc(Kc.Ticked("""
                if (k % 5u == 0u) {
                    on = on ^ 1u;
                    x_light_set_on(0u, on);
                }
            """, "uint32_t on;\n"));

        var l0 = new List<bool>();
        var l1 = new List<bool>();
        var l2 = new List<bool>();
        v.Run(3.0, 0.1, s => { l0.Add(s.Parts.Light(0).On); l1.Add(s.Parts.Light(1).On); l2.Add(s.Parts.Light(2).On); });

        // A command of tick k (frame k) is applied by the pump at the start of frame k + 1.
        const string expected = "011111000001111100000111110000";
        Assert.That(Bits(l0), Is.EqualTo(expected), v.Dump);
        Assert.That(Bits(l2), Is.EqualTo(expected), "light 2 shares light 0's switch");
        Assert.That(Bits(l1), Is.EqualTo(new string('0', 30)), "light 1 has its own switch");
    }

    [Test]
    public void Light_Show_Reads_Back_And_Resets()
    {
        using SimVehicle v = Vehicle(lights: 3);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    c.x = 1.0f; c.y = 0.5f; c.z = 0.25f;
                    x_light_set_color(1u, c);
                    x_light_set_cone(1u, 0.2f, 0.6f);
                    x_light_set_intensity(1u, 5.0f);
                    x_light_set_range(1u, 25.0f);
                } else if (k == 3u) {
                    kfs_logf("show set rgb=%.3f,%.3f,%.3f cone=%.3f,%.3f int=%.3f range=%.3f ovr=%u",
                             x_light_getf(1u, X_LIGHT_COLOR_R), x_light_getf(1u, X_LIGHT_COLOR_G),
                             x_light_getf(1u, X_LIGHT_COLOR_B), x_light_getf(1u, X_LIGHT_INNER_RAD),
                             x_light_getf(1u, X_LIGHT_OUTER_RAD), x_light_getf(1u, X_LIGHT_INTENSITY),
                             x_light_getf(1u, X_LIGHT_RANGE_M), x_light_geti(1u, X_LIGHT_OVERRIDDEN));
                } else if (k == 4u) {
                    c.x = 2.0f; c.y = 0.0f; c.z = 0.0f;
                    x_light_set_color(1u, c);
                    x_light_set_cone(1u, 2.0f, 3.0f);
                } else if (k == 6u) {
                    kfs_logf("show clamp rgb=%.3f,%.3f,%.3f cone=%.7f,%.7f",
                             x_light_getf(1u, X_LIGHT_COLOR_R), x_light_getf(1u, X_LIGHT_COLOR_G),
                             x_light_getf(1u, X_LIGHT_COLOR_B), x_light_getf(1u, X_LIGHT_INNER_RAD),
                             x_light_getf(1u, X_LIGHT_OUTER_RAD));
                } else if (k == 7u) {
                    x_light_reset(1u);
                } else if (k == 9u) {
                    kfs_logf("show reset rgb=%.3f,%.3f,%.3f cone=%.3f,%.3f int=%.3f range=%.3f ovr=%u",
                             x_light_getf(1u, X_LIGHT_COLOR_R), x_light_getf(1u, X_LIGHT_COLOR_G),
                             x_light_getf(1u, X_LIGHT_COLOR_B), x_light_getf(1u, X_LIGHT_INNER_RAD),
                             x_light_getf(1u, X_LIGHT_OUTER_RAD), x_light_getf(1u, X_LIGHT_INTENSITY),
                             x_light_getf(1u, X_LIGHT_RANGE_M), x_light_geti(1u, X_LIGHT_OVERRIDDEN));
                }
            """, "vec3 c;\n"));

        (float R, float G, float B, float Inner, float Outer, float Intensity, float Range, bool Overridden)? at3 = null, at6 = null;
        v.Run(1.2, 0.1, s =>
        {
            SimLight l = s.Parts.Light(1);
            var snapshot = (l.R, l.G, l.B, l.InnerRad, l.OuterRad, l.Intensity, l.RangeM, l.Overridden);
            if (s.FrameCount == 3) at3 = snapshot;
            if (s.FrameCount == 6) at6 = snapshot;
        });

        Assert.That(at3, Is.EqualTo((1f, 0.5f, 0.25f, 0.2f, 0.6f, 5f, 25f, true)), "applied before tick 3");
        Assert.That(at6, Is.EqualTo((1f, 0f, 0f, PartMathMaxOuter, PartMathMaxOuter, 5f, 25f, true)),
            "colour clamped to [0, 1]; outer cone to 1.5697963 and inner to the outer");
        Assert.That(v.LogText, Has.Member("show set rgb=1.000,0.500,0.250 cone=0.200,0.600 int=5.000 range=25.000 ovr=1"), v.Dump);
        Assert.That(v.LogText, Has.Member("show clamp rgb=1.000,0.000,0.000 cone=1.5697963,1.5697963"), v.Dump);
        Assert.That(v.LogText, Has.Member("show reset rgb=1.000,0.900,0.800 cone=0.300,0.600 int=2.000 range=30.000 ovr=0"), v.Dump);

        SimLight light = v.Parts.Light(1);
        Assert.That((light.R, light.G, light.B, light.InnerRad, light.OuterRad, light.Intensity, light.RangeM, light.Overridden),
            Is.EqualTo((1f, 0.9f, 0.8f, 0.3f, 0.6f, 2f, 30f, false)), "reset restores the stock appearance");
        Assert.That(v.Parts.Light(0).Overridden || v.Parts.Light(2).Overridden, Is.False, "other lights untouched");
    }

    private const float PartMathMaxOuter = purrTTY.Kfs.Runtime.PartMath.MaxOuterConeRad;

    [Test]
    public void Deploy_Takes_Its_Duration()
    {
        using SimVehicle v = Vehicle(deployables: 1);
        Assert.That(v.Parts.AllAnimations[0].DurationS, Is.EqualTo(2.0));
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    x_deploy_set(0u, 1u);
                }
                s = x_deploy_geti(0u, X_DEPLOY_STATE);
                if (s != last) {
                    kfs_logf("deploy k=%u state=%u pos=%.2f", k, s, x_deploy_getf(0u, X_DEPLOY_POSITION));
                    last = s;
                }
            """, "uint32_t s;\nuint32_t last = 99u;\n"));
        v.Run(3.0, 0.1);

        Assert.That(v.LogText.Where(t => t.StartsWith("deploy k=", StringComparison.Ordinal)), Is.EqualTo(new[]
        {
            "deploy k=0 state=2 pos=0.00",    // X_STATE_RETRACTED
            "deploy k=3 state=3 pos=0.05",    // X_STATE_DEPLOYING: stepped in frame 3, after frame 2's pump
            "deploy k=22 state=1 pos=1.00",   // X_STATE_DEPLOYED
        }), v.Dump);

        // The command of tick 1 is applied at 0.2 s (frame 2's pump, after its step) and the 2 s
        // animation arrives at exactly 2.2 s.
        var applied = v.Parts.Applied.Single();
        Assert.That(applied.TimeS, Is.EqualTo(0.2).Within(1e-9));
        Assert.That(v.Parts.DeployHistory.Select(h => (h.Index, h.State)),
            Is.EqualTo(new[] { (0, DeployState.Deploying), (0, DeployState.Deployed) }));
        Assert.That(v.Parts.DeployHistory[^1].TimeS - applied.TimeS, Is.EqualTo(2.0).Within(1e-9));
    }

    [Test]
    public void Actuator_Tracks_A_Triangle_Wave()
    {
        using SimVehicle v = Vehicle(actuators: 1);
        v.LoadKc(Kc.Ticked("""
                ph = (float)(k % 40u) / 20.0f;
                if (ph < 1.0f) { cmd = ph; } else { cmd = 2.0f - ph; }
                if (k % 5u == 0u) {
                    kfs_logf("sweep k=%u cmd=%.3f pos=%.3f", k, cmd, x_actuator_getf(0u, X_ACTUATOR_POSITION));
                }
                x_actuator_set(0u, cmd);
            """, "float cmd;\nfloat ph;\n"));

        // Seen before each dispatch: the step of this frame ran with the goal the previous frame's
        // pump set, and must have reached it.
        double maxLag = 0;
        double? previousGoal = null;
        v.Run(8.0, 0.1, s =>
        {
            SimAnimation a = s.Parts.Actuator(0);
            if (previousGoal is double g) maxLag = Math.Max(maxLag, Math.Abs(a.PositionS - g));
            previousGoal = a.GoalS;
        });

        static double Triangle(int k) { double ph = k % 40 / 20.0; return ph < 1 ? ph : 2 - ph; }
        var rows = v.LogText.Select(t => Regex.Match(t, @"^sweep k=(\d+) cmd=([\d.]+) pos=([\d.]+)$")).Where(m => m.Success)
            .Select(m => (K: int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                          Pos: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture))).ToList();

        Assert.That(rows, Has.Count.EqualTo(16), v.Dump);
        Assert.That(rows[0], Is.EqualTo((0, 0.0)));
        foreach (var (k, pos) in rows.Skip(1))
            Assert.That(pos, Is.EqualTo(Triangle(k - 2)).Within(0.0015), $"position read in tick {k} is the command of tick {k - 2} (applied by frame {k - 1}'s pump, stepped in frame {k})");
        Assert.That(maxLag, Is.LessThan(1e-9), "the wave's slope (0.5/s) is below the slew rate (1/s): each goal is reached by the next step");
    }

    /// <summary>
    /// Plan §5.8's <c>Frame_Order_Matches_Game</c> (A2's read-back test): a value a program sets in
    /// tick k is applied by the next frame's pump and reads back from the first tick of the next
    /// dispatch — never in the same tick, and never in a later tick of the same dispatch.
    /// </summary>
    [TestCase(0.1)]
    [TestCase(0.05)]
    [TestCase(0.2)]
    [TestCase(0.3)]
    public void Frame_Order_Matches_Game(double frameDt)
    {
        using SimVehicle v = Vehicle(lights: 1);
        v.LoadKc(Kc.Ticked("""
                if (k == 2u) {
                    x_light_set_on(0u, 1u);
                }
                if (k >= 2u && k <= 6u) {
                    kfs_logf("rb k=%u on=%u", k, x_light_geti(0u, X_LIGHT_ON));
                }
            """));
        v.Run(1.2, frameDt);

        // The tick after the last tick of the dispatch that ran tick 2: the first of the next dispatch.
        int ran = 0, expected = -1;
        foreach (int ticks in v.TicksPerFrame)
        {
            if (ran <= 2 && 2 < ran + ticks) { expected = ran + ticks; break; }
            ran += ticks;
        }
        Assert.That(expected, Is.GreaterThan(2));

        var reads = v.LogText.Select(t => Regex.Match(t, @"^rb k=(\d+) on=(\d)$")).Where(m => m.Success)
            .ToDictionary(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m => m.Groups[2].Value == "1");
        Assert.That(reads.Keys, Is.EqualTo(new[] { 2, 3, 4, 5, 6 }), v.Dump);
        int firstOn = reads.Where(r => r.Value).Min(r => r.Key);
        Assert.That(firstOn, Is.EqualTo(expected),
            $"ticks per dispatch {string.Join(",", v.TicksPerFrame.Take(8))}: a value set in tick 2 reads back from the first tick of the next dispatch");
        if (frameDt <= 0.2) Assert.That(firstOn, Is.EqualTo(3), "at most one tick per dispatch before tick 3: tick k + 1");
        if (frameDt == 0.3) Assert.That(firstOn, Is.EqualTo(4), "tick 3 ran in the same dispatch as tick 2");
    }

    [Test]
    public void Shared_Switch_Switches_Both()
    {
        var spec = new SimVehicleSpec
        {
            Lights = [new SimLightSpec { Name = "Emitter 1" }, new SimLightSpec { Name = "Other" }, new SimLightSpec { Name = "Emitter 2", SharesSwitchWith = 0 }],
        };
        using SimVehicle v = Vehicle(spec);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    x_light_set_on(2u, 1u);
                } else if (k == 3u) {
                    kfs_logf("grp %u %u %u on %u %u %u",
                             x_light_geti(0u, X_LIGHT_SWITCH_GROUP), x_light_geti(1u, X_LIGHT_SWITCH_GROUP), x_light_geti(2u, X_LIGHT_SWITCH_GROUP),
                             x_light_geti(0u, X_LIGHT_ON), x_light_geti(1u, X_LIGHT_ON), x_light_geti(2u, X_LIGHT_ON));
                    x_light_set_on(0u, 0u);
                } else if (k == 5u) {
                    kfs_logf("off %u %u", x_light_geti(0u, X_LIGHT_ON), x_light_geti(2u, X_LIGHT_ON));
                }
            """));
        v.Run(0.7, 0.1);

        Assert.That(v.LogText, Has.Member("grp 0 1 0 on 1 0 1"), v.Dump);
        Assert.That(v.LogText, Has.Member("off 0 0"), v.Dump);
        Assert.That(v.Parts.Lights.Select(l => l.On), Is.EqualTo(new[] { false, false, false }));
    }

    [Test]
    public void Two_Vehicles_Are_Independent()
    {
        string program = Kc.Ticked("""
                if (k == 0u) {
                    c = x_light_count();
                    kfs_logf("vehicle has %u lights", c);
                    x_light_set_on(c - 1u, 1u);
                }
            """, "uint32_t c;\n");
        using SimVehicle a = Vehicle(lights: 2, name: "veh-a");
        using SimVehicle b = Vehicle(lights: 5, name: "veh-b");
        a.LoadKc(program);
        b.LoadKc(program);
        for (int frame = 0; frame < 3; frame++)
        {
            a.Frame(0.1);
            b.Frame(0.1);
        }

        Assert.That(a.LogText, Has.Member("vehicle has 2 lights"), a.Dump);
        Assert.That(b.LogText, Has.Member("vehicle has 5 lights"), b.Dump);
        Assert.That(a.Parts.Lights.Select(l => l.On), Is.EqualTo(new[] { false, true }));
        Assert.That(b.Parts.Lights.Select(l => l.On), Is.EqualTo(new[] { false, false, false, false, true }));
        Assert.That((a.Binding.Applied, b.Binding.Applied), Is.EqualTo((1L, 1L)));
    }

    [Test]
    public void Stock_Requests_Are_Recorded()
    {
        using SimVehicle v = Vehicle(lights: 1);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    kfs_stage();
                    x_light_set_on(0u, 1u);
                } else if (k == 2u) {
                    kfs_set_throttle(0.5f);
                } else if (k == 8u) {
                    kfs_logf("alive %u", k);
                }
            """));
        v.Run(1.0, 0.1);

        Assert.That(v.LogText, Has.Member("alive 8"), "the program keeps ticking after staging\n" + v.Dump());
        Assert.That(v.StockRequests.Select(r => r.Id), Is.EqualTo(new ushort[] { 32, 52 }));
        Assert.That(BitConverter.UInt32BitsToSingle(v.StockRequests[1].Args[0]), Is.EqualTo(0.5f));
        Assert.That(v.Parts.Light(0).On, Is.True, "mod services work alongside stock ones");
    }

    private static string Bits(IEnumerable<bool> values) => string.Concat(values.Select(b => b ? '1' : '0'));
}
