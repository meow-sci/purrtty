using System.Globalization;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.ExamplePrograms;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// <c>Examples_Run</c> (plan §4.16): every shipped example, built as its own workspace folder
/// exactly as the game's Workbench builds one (a kC folder project with every file of the folder,
/// or a pure kASM project with its generated <c>purrtty.kinc</c>), with no error and no warning,
/// then run on a simulated vehicle to its documented behaviour.
/// </summary>
[TestFixture]
public sealed class ExampleProgramTests : RuntimeFixture
{
    private const double Tick = 0.1;

    [Test]
    public void Catalogue_Is_One_Folder_Per_Example()
    {
        Assert.That(ExampleCatalog.All.Select(e => e.Folder), Is.EqualTo(new[]
        {
            "x_blink", "x_light_show", "x_deploy", "x_actuator", "x_parts", "x_party_time", "x_blink_kasm",
        }));
        Assert.That(ExampleCatalog.Files.Keys, Is.EquivalentTo(new[]
        {
            "x_blink/blink.c", "x_light_show/light_show.c", "x_deploy/deploy_when_safe.c",
            "x_actuator/actuator_sweep.c", "x_parts/parts_list.c", "x_party_time/party_time.c",
            "x_blink_kasm/blink.kasm", "x_blink_kasm/purrtty.kinc",
        }));
        foreach (KfsExample e in ExampleCatalog.All)
        {
            Assert.That(e.Files.Keys, Has.All.StartsWith(e.Folder + "/"), e.Folder);
            Assert.That(e.Files.Keys.Select(k => k.Count(c => c == '/')), Has.All.EqualTo(1), "one level deep");
            Assert.That(e.Files, Does.ContainKey(e.MainFile));
            Assert.That(e.Files.Values.Select(v => v.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n')), Has.All.False, "CRLF line ends, as the game's examples");
            Assert.That(e.Description, Is.Not.Empty.And.Not.Contain("\n"));
        }
        Assert.That(ExampleCatalog.Files["x_blink_kasm/purrtty.kinc"], Is.EqualTo(CatalogEmitter.Kinc(XCatalog.Catalog)), "generated from the catalogue");
        Assert.That(ExampleCatalog.All.Count(e => e.IsKc), Is.EqualTo(6));
    }

    [Test]
    public void Every_Example_Builds_Without_Diagnostics()
    {
        foreach (KfsExample e in ExampleCatalog.All)
        {
            using SimVehicle v = Vehicle(lights: 2, deployables: 1, actuators: 1, name: e.Folder);
            BuildResult build = e.IsKc ? Runtime.BuildKc(e.ProjectFiles, v.Layout) : Runtime.BuildKasm(e.ProjectFiles, v.Layout);
            Assert.That(build.Ok, Is.True, $"{e.Folder}:\n{build.Text}");
            Assert.That(build.Diagnostics, Is.Empty, $"{e.Folder} must build with no warning:\n{build.Text}");
        }
    }

    // ------------------------------------------------------------------ x_blink

    [Test]
    public void Blink_Toggles_Every_Switch_Group_At_One_Hertz()
    {
        using SimVehicle v = Vehicle(new SimVehicleSpec
        {
            Lights =
            [
                new SimLightSpec { Name = "Spot A" },
                new SimLightSpec { Name = "Spot A", SharesSwitchWith = 0 },
                new SimLightSpec { Name = "Spot B" },
                new SimLightSpec { Name = "Cabin", Type = LightType.Point, Interior = true },
            ],
        });
        Load(v, "x_blink");

        var on = new List<bool>[4];
        for (int i = 0; i < 4; i++) on[i] = [];
        v.Run(3.0, Tick, s => { for (int i = 0; i < 4; i++) on[i].Add(s.Parts.Light(i).On); });

        // Tick k runs in frame k; its command is applied by the pump of frame k + 1. On for
        // 0.5 s, off for 0.5 s: one blink a second.
        const string expected = "011111000001111100000111110000";
        Assert.That(Bits(on[0]), Is.EqualTo(expected), v.Dump);
        Assert.That(Bits(on[1]), Is.EqualTo(expected), "light 1 is on light 0's switch");
        Assert.That(Bits(on[2]), Is.EqualTo(expected), "light 2 has its own switch");
        Assert.That(Bits(on[3]), Is.EqualTo(new string('1', 30)), "the interior light has no switch: it reads on and is never commanded");
        Assert.That(v.Parts.Applied.Select(a => a.Command.Slot!.Index).Distinct(), Is.EquivalentTo(new[] { 0, 2 }),
            "only the first light of each switch group is switched");
        Assert.That(v.LogText, Has.Member("blink: 4 lights, 2 switch groups"), v.Dump);
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    [Test]
    public void Blink_Kasm_Toggles_Every_Switchable_Light()
    {
        using SimVehicle v = Vehicle(new SimVehicleSpec
        {
            Lights =
            [
                new SimLightSpec { Name = "Spot A" },
                new SimLightSpec { Name = "Spot B" },
                new SimLightSpec { Name = "Cabin", Type = LightType.Point, Interior = true },
            ],
        });
        Load(v, "x_blink_kasm");

        var on = new List<bool>[3];
        for (int i = 0; i < 3; i++) on[i] = [];
        v.Run(3.0, Tick, s => { for (int i = 0; i < 3; i++) on[i].Add(s.Parts.Light(i).On); });

        const string expected = "011111000001111100000111110000";
        Assert.That(Bits(on[0]), Is.EqualTo(expected), v.Dump);
        Assert.That(Bits(on[1]), Is.EqualTo(expected), v.Dump);
        Assert.That(Bits(on[2]), Is.EqualTo(new string('1', 30)));
        Assert.That(v.Parts.Applied.Select(a => a.Command.Slot!.Index).Distinct(), Is.EquivalentTo(new[] { 0, 1 }), "the switchless light is skipped");
        Assert.That(v.LogText, Has.Member("blink.kasm: 3 lights"), v.Dump);
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    // ------------------------------------------------------------------ x_light_show

    [Test]
    public void Light_Show_Drives_Colour_Cone_And_Intensity_Then_Resets()
    {
        using SimVehicle v = Vehicle(new SimVehicleSpec
        {
            Lights =
            [
                new SimLightSpec { Name = "Spot", On = true },
                new SimLightSpec { Name = "Spot", On = true, Intensity = 4f },
                new SimLightSpec { Name = "Lamp", On = true, Type = LightType.Point, Intensity = 1f },
            ],
        });
        Load(v, "x_light_show");

        var failures = new List<string>();
        v.Run(3.0, Tick, s =>
        {
            if (s.FrameCount == 0) return;   // nothing applied yet
            int k = s.FrameCount - 1;        // the tick whose commands this frame's pump applied
            for (int i = 0; i < 2; i++) Check(failures, k, i, s.Parts.Light(i), s.Parts.Light(i).Stock.Intensity);
        });
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures.Take(10)) + "\n" + v.Dump());

        // The effects really move: colour, cone and intensity over the 3 s.
        SimLight spot = v.Parts.Light(0);
        Assert.That(spot.Overridden, Is.True);
        Assert.That(v.Parts.Applied.Count(a => a.Command.Service == Svc.LightSetColor), Is.EqualTo(2 * 29), "every tick, both spot lights (tick 29's commands are applied by the next frame)");
        Assert.That(v.Parts.Applied.Where(a => a.Command.Slot!.Index == 2), Is.Empty, "the point light is left alone");
        Assert.That(v.Parts.Light(2).Overridden, Is.False);
        Assert.That(v.LogText, Has.Member("light show: 3 lights, 2 spot lights"), v.Dump);

        // stop = 1: every light reset, once.
        int applied = v.Parts.Applied.Count;
        v.WriteGlobal("stop", 1u);
        v.Run(1.0, Tick);
        var after = v.Parts.Applied.Skip(applied).Select(a => (a.Command.Service, a.Command.Slot!.Index)).ToList();
        Assert.That(after.Take(6).Select(a => a.Service), Is.EquivalentTo(new[]
        {
            Svc.LightSetColor, Svc.LightSetCone, Svc.LightSetIntensity, Svc.LightSetColor, Svc.LightSetCone, Svc.LightSetIntensity,
        }), "first the last show tick's commands, applied by the next frame");
        Assert.That(after.Skip(6), Is.EquivalentTo(new[] { (Svc.LightReset, 0), (Svc.LightReset, 1), (Svc.LightReset, 2) }),
            "then one reset per light, and nothing more");
        for (int i = 0; i < 3; i++)
        {
            SimLight l = v.Parts.Light(i);
            Assert.That((l.Overridden, l.R, l.G, l.B, l.InnerRad, l.OuterRad, l.Intensity),
                Is.EqualTo((false, l.Stock.R, l.Stock.G, l.Stock.B, l.Stock.InnerRad, l.Stock.OuterRad, l.Stock.Intensity)), $"light {i} is back to stock");
        }
        Assert.That(v.LogText, Has.Member("light show: stopped, 3 lights reset"), v.Dump);

        // stop = 0: the show goes on.
        v.WriteGlobal("stop", 0u);
        v.Run(0.3, Tick);
        Assert.That(v.Parts.Light(0).Overridden && v.Parts.Light(1).Overridden, Is.True, v.Dump);
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    /// <summary>The documented effect of tick <paramref name="k"/> on spot light <paramref name="i"/>.</summary>
    private static void Check(List<string> failures, int k, int i, SimLight l, float stock)
    {
        double t = (float)k * 0.1f;
        double breath = 0.5 - 0.5 * Math.Cos(2 * Math.PI * t / 4.0);
        double outer = 0.15 + 0.45 * breath;
        double hue = ((k + 8 * i) % 64) / 64.0;
        double h = 6 * hue;
        double r = Math.Clamp(Math.Abs(h - 3) - 1, 0, 1), g = Math.Clamp(2 - Math.Abs(h - 2), 0, 1), b = Math.Clamp(2 - Math.Abs(h - 4), 0, 1);
        double intensity = (0.5 + 1.5 * (k % 50) / 50.0) * stock;
        var expected = new[] { r, g, b, 0.5 * outer, outer, intensity };
        var actual = new double[] { l.R, l.G, l.B, l.InnerRad, l.OuterRad, l.Intensity };
        for (int j = 0; j < expected.Length; j++)
        {
            if (Math.Abs(expected[j] - actual[j]) > 1e-4 * Math.Max(1, Math.Abs(expected[j])))
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"tick {k} light {i} value {j}: expected {expected[j]:0.#####}, got {actual[j]:0.#####}"));
        }
    }

    // ------------------------------------------------------------------ x_deploy

    [Test]
    public void Deploy_When_Safe_Follows_The_Atmosphere_And_Skips_Parachutes()
    {
        using SimVehicle v = Vehicle(new SimVehicleSpec
        {
            Deployables =
            [
                new SimAnimationSpec { Name = "Solar Array", DurationS = 2.0, Links = AnimSlot.LinkSolarPanel },
                new SimAnimationSpec { Name = "Cargo Door", DurationS = 2.0 },
                new SimAnimationSpec { Name = "Chute Bay", DurationS = 1.0, Links = AnimSlot.LinkSolarPanel | AnimSlot.LinkParachute },
            ],
        });
        // Up at 5 km/s for 20 s, then down again; the atmosphere ends at 72 km.
        static double Altitude(double t) => 5000.0 * (t <= 20 ? t : 40 - t);
        v.TelemetryScript = t => new SimTelemetry { Flight = new SimFlight { AltitudeM = Altitude(t), InAtmosphere = Altitude(t) < 72_000 } };
        Load(v, "x_deploy");

        v.Run(30.0, Tick);

        var history = v.Parts.DeployHistory.Where(h => h.Index == 0).ToList();
        Assert.That(history.Select(h => h.State), Is.EqualTo(new[] { DeployState.Deploying, DeployState.Deployed, DeployState.Retracting, DeployState.Retracted }), v.Dump);
        // The 2 Hz task sees 72.5 km at 14.5 s and 70 km at 26 s; the next frame's pump applies the
        // command and the parts move from the step of the frame after it.
        Assert.That(history[0].TimeS, Is.EqualTo(14.7).Within(1e-6), "deploying from the second frame after the 14.5 s task");
        Assert.That(history[1].TimeS, Is.EqualTo(16.7).Within(0.11), "deployed after its 2 s");
        Assert.That(history[2].TimeS, Is.EqualTo(26.2).Within(1e-6), "retracting from the second frame after the 26 s task");
        Assert.That(history[3].TimeS, Is.EqualTo(28.2).Within(0.11));
        Assert.That(v.Parts.DeployHistory.Where(h => h.Index != 0), Is.Empty, "the cargo door and the parachute bay never move");
        Assert.That(v.Parts.Applied.Select(a => (a.Command.Service, a.Command.Slot!.Index, a.Command.U)),
            Is.EqualTo(new[] { (Svc.DeploySet, 0, 0u), (Svc.DeploySet, 0, 1u), (Svc.DeploySet, 0, 0u) }), "one command per change, all to the solar array");

        Assert.That(v.LogText, Is.SupersetOf(new[]
        {
            "deploy_when_safe: in the atmosphere at 0.0 km: retracting 1 solar arrays",
            "deploy_when_safe: deployable 2 is also driven by a parachute: left alone",
            "deploy_when_safe: out of the atmosphere at 72.5 km: deploying 1 solar arrays",
            "deploy_when_safe: in the atmosphere at 70.0 km: retracting 1 solar arrays",
        }), v.Dump);
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    // ------------------------------------------------------------------ x_actuator

    [Test]
    public void Actuator_Sweep_Tracks_Its_Triangle_Wave()
    {
        using SimVehicle v = Vehicle(new SimVehicleSpec { Actuators = [new SimAnimationSpec { Name = "Arm", DurationS = 1.0 }] });
        Load(v, "x_actuator");

        // 1 s of travel: each way takes the minimum 4 s, 40 ticks; the target moves 0.025 a tick.
        var failures = new List<string>();
        v.Run(10.0, Tick, s =>
        {
            if (s.FrameCount == 0) return;
            int k = s.FrameCount - 1;
            int step = k % 80;
            double target = step < 40 ? step / 40.0 : (80 - step) / 40.0;
            // This frame's pump set the goal of tick k; the parts were stepped before it, with the
            // goal of tick k - 1 (reached in one step: the part is faster than the wave).
            int before = Math.Max(0, k - 1) % 80;
            double reached = before < 40 ? before / 40.0 : (80 - before) / 40.0;
            SimAnimation a = s.Parts.Actuator(0);
            double goal = a.GoalS / a.DurationS, position = a.PositionS / a.DurationS;
            if (Math.Abs(goal - target) > 1e-6 || Math.Abs(position - reached) > 1e-6)
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"tick {k}: target {target:0.000}, goal {goal:0.000}, position {position:0.000} (expected {reached:0.000})"));
        });
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures.Take(10)) + "\n" + v.Dump());

        Assert.That(v.LogText, Is.SupersetOf(new[]
        {
            "actuator_sweep: actuator 0, 4.0 s each way",
            // A target is read back two ticks later: applied by the next frame's pump, stepped in the frame after.
            "actuator_sweep: turn 1 at the top: target 1.00, position 0.950",
            "actuator_sweep: turn 2 at the bottom: target 0.00, position 0.050",
        }), v.Dump);
        Assert.That(v.LogText.Count(l => l.StartsWith("actuator_sweep: turn", StringComparison.Ordinal)), Is.EqualTo(2), "turns at ticks 40 and 80 of 100");
        Assert.That(v.ReadGlobal<uint>("turns"), Is.EqualTo(2u));
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    [Test]
    public void Actuator_Sweep_Slows_Down_For_A_Slow_Part_And_Stops_Without_One()
    {
        using (SimVehicle slow = Vehicle(new SimVehicleSpec { Actuators = [new SimAnimationSpec { Name = "Bay", DurationS = 3.0 }] }))
        {
            Load(slow, "x_actuator").Run(0.5, Tick);
            Assert.That(slow.LogText, Has.Member("actuator_sweep: actuator 0, 6.0 s each way"), slow.Dump);
            Assert.That(slow.ReadGlobal<uint>("half_ticks"), Is.EqualTo(60u));
        }
        using (SimVehicle none = Vehicle(lights: 1))
        {
            Load(none, "x_actuator").Run(1.0, Tick);
            Assert.That(none.LogText, Has.Member("actuator_sweep: this vehicle has no actuator 0"), none.Dump);
            Assert.That(none.Parts.Applied, Is.Empty);
            Assert.That(none.LogText.Count(l => l.StartsWith("actuator_sweep", StringComparison.Ordinal)), Is.EqualTo(1), "said once");
        }
    }

    // ------------------------------------------------------------------ x_parts

    [Test]
    public void Parts_List_Describes_Then_Rescans_When_The_Vehicle_Changes()
    {
        using SimVehicle v = Vehicle(lights: 2, deployables: 1, actuators: 1);
        Load(v, "x_parts");
        v.Run(1.5, Tick);

        Assert.That(v.LogText, Is.SupersetOf(new[]
        {
            "parts_list: generation 1: 2 lights, 1 deployables, 1 actuators",
            "parts (generation 1): 2 lights, 1 deployable, 1 actuator",
            "light 0: Light 0 (part 100) spot, switch group 0, off",
            "light 1: Light 1 (part 101) spot, switch group 1, off",
            "deployable 0: Deployable 0 (part 200) retracted, 2 s",
            "actuator 0: Actuator 0 (part 300) at 0.00 of 1 s",
        }), v.Dump);
        Assert.That(v.ReadGlobal<uint>("rescans"), Is.Zero, "nothing changed yet");
        int builds = v.Parts.Builds;

        // Undocking and docking: a light leaves, an array arrives.
        v.Parts.Detach(v.Parts.Light(1));
        v.Parts.AddDeployable(new SimAnimationSpec { Name = "Docked Array", DurationS = 4.0, Links = AnimSlot.LinkSolarPanel });
        v.Run(3.0, Tick);

        Assert.That(v.LogText, Is.SupersetOf(new[]
        {
            "parts_list: the vehicle has changed; rescanning",
            "parts rescanned: 1 light, 2 deployables, 1 actuator (generation 2)",
            "parts_list: generation 2: 1 lights, 2 deployables, 1 actuators",
            "parts (generation 2): 1 light, 2 deployables, 1 actuator",
            "deployable 1: Docked Array (part 201) retracted, 4 s, drives a solar panel",
        }), v.Dump);
        Assert.That(v.Parts.Builds, Is.EqualTo(builds + 1), "one rebuild");
        Assert.That((v.ReadGlobal<uint>("rescans"), v.ReadGlobal<uint>("generation"), v.ReadGlobal<uint>("rescanning")), Is.EqualTo((1u, 2u, 0u)));
        Assert.That(v.LogText.Count(l => l.StartsWith("parts (generation", StringComparison.Ordinal)), Is.EqualTo(2), "described twice, never rejected");

        v.Run(3.0, Tick);
        Assert.That(v.ReadGlobal<uint>("rescans"), Is.EqualTo(1u), "no rescan while nothing changes");
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    // ------------------------------------------------------------------ x_party_time

    // The program's documented constants (party_time.c).
    private const double PartyTick = 0.05;      // 20 Hz
    private const double MinStep = 0.25;
    private const double LagS = 0.1, DwellMinS = 0.2, DwellMaxS = 1.5, AssumedMoveS = 1.0;
    private const double StartSpreadS = 2.5;
    private const int WorstCaseCallsPerTick = 132;
    private const int WorstCaseCommandsPerTick = 80;
    private const long MaxCyclesPerTick = 25_000;   // half the budget of a 20 Hz tick on the Bench part (K32F, as the Mk2)

    /// <summary>
    /// Five lights on four parts and four actuators: three on light parts (the twin lamp's two
    /// lights share one), a cargo bay on a part of its own, and a beacon (a point light) whose
    /// part has no actuator.
    /// </summary>
    private static SimVehicleSpec PartySpec(string name = "party") => new()
    {
        Name = name,
        Lights =
        [
            new SimLightSpec { Name = "Lamp", PartId = 500 },
            new SimLightSpec { Name = "Twin Lamp", PartId = 501, Intensity = 4f },
            new SimLightSpec { Name = "Twin Lamp", PartId = 501, Intensity = 4f, SharesSwitchWith = 1 },
            new SimLightSpec { Name = "Beacon", PartId = 503, Type = LightType.Point, Intensity = 1f },
            new SimLightSpec { Name = "Flood", PartId = 504, Intensity = 10f },
        ],
        Actuators =
        [
            new SimAnimationSpec { Name = "Lamp", PartId = 500, DurationS = 1.0 },
            new SimAnimationSpec { Name = "Cargo Bay", PartId = 600, DurationS = 3.0 },
            new SimAnimationSpec { Name = "Twin Lamp", PartId = 501, DurationS = 0.5 },
            new SimAnimationSpec { Name = "Flood", PartId = 504, DurationS = 2.0 },
        ],
    };

    /// <summary>One <c>x_actuator_set</c> as the pump applied it: the pump's time, the actuator, the target.</summary>
    private sealed record Move(double TimeS, int Index, double Target);

    /// <summary>The most cycles a tick of the program's task has used (the game's measured maximum).</summary>
    private static long MaxTaskCycles(SimVehicle v) => v.Computer.Snapshot.Tasks[0].MeasuredMax;

    private static List<Move> Moves(SimVehicle v) =>
        v.Parts.Applied.Where(a => a.Command.Service == Svc.ActuatorSet).Select(a => new Move(a.TimeS, a.Command.Slot!.Index, a.Command.A)).ToList();

    /// <summary>The tick whose command the pump at <paramref name="timeS"/> applied (one tick per frame, tick k runs at k × 0.05 s).</summary>
    private static int TickOf(double timeS) => (int)Math.Round(timeS / PartyTick) - 1;

    /// <summary>
    /// Every move of actuator <paramref name="index"/> obeys the schedule: a target in [0, 1] at
    /// least <see cref="MinStep"/> from the last (the first from where the part was), and a gap to
    /// the next move of the lag, the move's own time (its share of <paramref name="travelS"/>, or
    /// the assumed second) and a dwell of 0.2 to 1.5 s, in whole ticks.
    /// </summary>
    private static void CheckSchedule(List<string> failures, List<Move> moves, int index, double travelS, double initial = 0, bool reported = true)
    {
        var mine = moves.Where(m => m.Index == index).ToList();
        double last = initial;
        for (int n = 0; n < mine.Count; n++)
        {
            Move m = mine[n];
            if (m.Target < 0 || m.Target > 1) failures.Add($"actuator {index} move {n}: target {m.Target} outside [0, 1]");
            if (Math.Abs(m.Target - last) < MinStep - 1e-5) failures.Add($"actuator {index} move {n}: {last:0.000} -> {m.Target:0.000} is below the minimum step");
            if (n + 1 < mine.Count)
            {
                double moveS = reported ? Math.Abs(m.Target - last) * travelS : AssumedMoveS;
                double gap = mine[n + 1].TimeS - m.TimeS;
                double min = LagS + moveS + DwellMinS, max = LagS + moveS + DwellMaxS + PartyTick + 1e-6;
                if (gap < min - 1e-6 || gap > max)
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"actuator {index} move {n} at {m.TimeS:0.00} s: next after {gap:0.00} s, expected {min:0.00} .. {max:0.00}"));
            }
            last = m.Target;
        }
    }

    [Test]
    public void Party_Time_Moves_The_Light_Parts_Now_And_Then_Staggered()
    {
        using SimVehicle v = Vehicle(PartySpec());
        Load(v, "x_party_time");

        // Positions as each frame's step left them (before its pump), per actuator.
        var positions = new List<double[]>();
        var targetsAt = new List<double[]>();
        v.Run(30.0, PartyTick, s =>
        {
            positions.Add(Enumerable.Range(0, 4).Select(i => s.Parts.Actuator(i).PositionS / s.Parts.Actuator(i).DurationS).ToArray());
            targetsAt.Add(Enumerable.Range(0, 4).Select(i => s.Parts.Actuator(i).GoalS / s.Parts.Actuator(i).DurationS).ToArray());
        });

        List<Move> moves = Moves(v);
        Assert.That(v.LogText, Is.SupersetOf(new[]
        {
            "party_time: 5 lights, 4 spot lights",
            "party_time: driving the 3 actuators on light parts, of 4 (seed 2026)",
        }), v.Dump);
        Assert.That(moves.Select(m => m.Index).Distinct(), Is.EquivalentTo(new[] { 0, 2, 3 }), "only the actuators on light parts");
        Assert.That(moves.Where(m => m.Index == 1), Is.Empty, "the cargo bay is never commanded");
        Assert.That(v.Parts.Actuator(1).PositionS, Is.Zero);

        // Occasionally, on its own schedule, with visible steps.
        var failures = new List<string>();
        CheckSchedule(failures, moves, 0, 1.0);
        CheckSchedule(failures, moves, 2, 0.5);
        CheckSchedule(failures, moves, 3, 2.0);
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures.Take(10)));
        foreach (var (index, travel) in new[] { (0, 1.0), (2, 0.5), (3, 2.0) })
        {
            // From the first move (at most 2.6 s in) to the end, each gap is between the shortest
            // and the longest the schedule allows.
            double shortest = LagS + MinStep * travel + DwellMinS, longest = LagS + travel + DwellMaxS + PartyTick;
            int count = moves.Count(m => m.Index == index);
            Assert.That(count, Is.InRange((int)Math.Floor((30.0 - StartSpreadS - 0.1) / longest), (int)Math.Ceiling(30.0 / shortest)), $"actuator {index}");
            Assert.That(count, Is.GreaterThanOrEqualTo(8), $"actuator {index} moves often enough to see");
        }

        // Staggered: the first moves in three different ticks within the start spread, few moves
        // in the same tick as another's, and the actuators at different targets.
        var firsts = new[] { 0, 2, 3 }.Select(i => moves.First(m => m.Index == i).TimeS).ToList();
        Assert.That(firsts.Select(TickOf).Distinct().Count(), Is.EqualTo(3), "first moves in different ticks");
        Assert.That(firsts, Has.All.InRange(0.1, StartSpreadS + 0.2));
        int together = moves.GroupBy(m => TickOf(m.TimeS)).Where(g => g.Count() > 1).Sum(g => g.Count());
        Assert.That(together, Is.LessThan(moves.Count / 5), "moves rarely fall in one tick");
        int apart = 0, alike = 0, samples = 0;
        for (int f = 60; f < targetsAt.Count; f += 20)   // once a second from 3 s on
        {
            double[] t = targetsAt[f];
            double[] driven = [t[0], t[2], t[3]];
            samples++;
            if (driven.Max() - driven.Min() < 0.05) alike++;
            if (Math.Abs(t[0] - t[2]) > 0.05 && Math.Abs(t[0] - t[3]) > 0.05 && Math.Abs(t[2] - t[3]) > 0.05) apart++;
        }
        Assert.That(alike, Is.LessThanOrEqualTo(samples / 10), "the three targets are rarely alike");
        Assert.That(apart, Is.GreaterThan(samples / 2), "at most moments all three targets differ");

        // The parts really move, and a move is over before the next one is commanded.
        foreach (int index in new[] { 0, 2, 3 })
        {
            var mine = moves.Where(m => m.Index == index).ToList();
            for (int n = 0; n + 1 < mine.Count; n++)
            {
                int frame = (int)Math.Round(mine[n + 1].TimeS / PartyTick);
                Assert.That(positions[frame][index], Is.EqualTo(mine[n].Target).Within(1e-4),
                    $"actuator {index} reached move {n}'s target before move {n + 1}");
            }
        }
        Assert.That((v.RejectedRequests, v.Overruns, v.Binding.Dropped), Is.EqualTo((0u, 0u, 0L)), v.Dump);
    }

    [Test]
    public void Party_Time_Schedule_Is_Reproducible_And_Follows_The_Seed()
    {
        static List<Move> Run(uint? seed)
        {
            using SimVehicle v = Vehicle(PartySpec());
            Load(v, "x_party_time");
            if (seed is uint s) v.WriteGlobal("seed", s);   // after the start: kfs start resets every global
            v.Run(15.0, PartyTick);
            Assert.That(v.LogText, Has.Member($"party_time: driving the 3 actuators on light parts, of 4 (seed {seed ?? 2026u})"), v.Dump);
            return Moves(v);
        }

        List<Move> first = Run(null), again = Run(null), other = Run(7u);
        Assert.That(again, Is.EqualTo(first), "the same seed gives the same party");
        Assert.That(other, Is.Not.EqualTo(first), "another seed gives another");
        Assert.That(other.Take(3).Select(m => (m.Index, TickOf(m.TimeS))), Is.Not.EqualTo(first.Take(3).Select(m => (m.Index, TickOf(m.TimeS)))), "from the first moves on");
    }

    [Test]
    public void Party_Time_Lights_Cycle_In_Different_Colours_Within_The_Request_Limits()
    {
        using SimVehicle v = Vehicle(PartySpec());
        Load(v, "x_party_time");

        var failures = new List<string>();
        var perTick = new List<long>();
        long calls = 0;
        int logs = 0;
        var colours = new List<(float R, float G, float B)[]>();
        v.Run(10.0, PartyTick, s =>
        {
            if (s.FrameCount == 0) return;
            // This frame's pump applied tick k; each light was last refreshed on the latest tick of
            // its parity, from tick 3 (ticks 0, 1 and 2 scan the lights, scan the actuators and
            // choose the ones to drive).
            int k = s.FrameCount - 1;
            for (int i = 0; i < 5; i++)
            {
                int last = k % 2 == i % 2 ? k : k - 1;
                if (last >= 3) CheckParty(failures, last, i, s.Parts.Light(i));
            }
            colours.Add(Enumerable.Range(0, 5).Select(i => (s.Parts.Light(i).R, s.Parts.Light(i).G, s.Parts.Light(i).B)).ToArray());
        });
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures.Take(10)) + "\n" + v.Dump());

        // Different colours at the same moment, changing over time.
        (float R, float G, float B)[] now = colours[^1];
        Assert.That(now.Distinct().Count(), Is.EqualTo(5), "five lights, five colours");
        Assert.That(colours.Select(c => c[0]).Distinct().Count(), Is.GreaterThan(30), "light 0 cycles");
        for (int i = 0; i < 5; i++) Assert.That(v.Parts.Light(i).On && v.Parts.Light(i).Overridden, Is.True, $"light {i} is on and driven");
        Assert.That(v.Parts.Applied.Where(a => a.Command.Service == Svc.LightSetCone).Select(a => a.Command.Slot!.Index).Distinct(),
            Is.EquivalentTo(new[] { 0, 1, 2, 4 }), "the point light gets no cone");
        Assert.That(v.Parts.Light(3).InnerRad, Is.EqualTo(v.Parts.Light(3).Stock.InnerRad));

        // Requests per tick, one tick per frame: getters and commands answered by the extension plus
        // the program's log lines.
        using (SimVehicle w = Vehicle(PartySpec("calls")))
        {
            Load(w, "x_party_time");
            for (int f = 0; f < 200; f++)
            {
                long before = w.Binding.Answered + w.Binding.Accepted;
                int logBefore = w.LogText.Count;
                Assert.That(w.Frame(PartyTick), Is.EqualTo(1));
                calls = w.Binding.Answered + w.Binding.Accepted - before;
                logs = w.LogText.Count - logBefore;
                perTick.Add(calls + logs);
            }
            // 5 lights and 4 actuators: a light scan (count, 4 reads each, the switch, a line), an
            // actuator scan (count, 4 reads each), the choice (a line), then at most 3 lights × 4
            // and 3 actuators × 2, plus a line.
            Assert.That(perTick[0], Is.EqualTo(1 + 5 * 4 + 1 + 1), "the light scan");
            Assert.That(perTick[1], Is.EqualTo(1 + 4 * 4), "the actuator scan");
            Assert.That(perTick[2], Is.EqualTo(1), "the choice");
            Assert.That(perTick.Skip(3).Max(), Is.LessThanOrEqualTo(3 * 4 + 3 * 2 + 1));
            Assert.That(perTick.Max(), Is.LessThan(WorstCaseCallsPerTick));
        }

        // Time warp: ten ticks a frame, as many lights and actuators as the program takes.
        using (SimVehicle big = Vehicle(new SimVehicleSpec
        {
            Name = "big",
            Lights = Enumerable.Range(0, 40).Select(i => new SimLightSpec { Name = $"Lamp {i}", PartId = (uint)(1000 + i) }).ToArray(),
            Actuators = Enumerable.Range(0, 40).Select(i => new SimAnimationSpec { Name = $"Hinge {i}", PartId = (uint)(1000 + i), DurationS = 0.2 }).ToArray(),
        }))
        {
            Load(big, "x_party_time");
            long maxPerFrame = 0;
            for (int f = 0; f < 60; f++)
            {
                long before = big.Binding.Answered + big.Binding.Accepted;
                int logBefore = big.LogText.Count;
                int ticks = big.Frame(0.5);
                long frameCalls = big.Binding.Answered + big.Binding.Accepted - before + big.LogText.Count - logBefore;
                maxPerFrame = Math.Max(maxPerFrame, frameCalls);
                Assert.That(frameCalls, Is.LessThanOrEqualTo(ticks * WorstCaseCallsPerTick), $"frame {f}");
                Assert.That(big.Binding.PendingCount, Is.LessThanOrEqualTo(ticks * WorstCaseCommandsPerTick), $"frame {f}: commands waiting for the pump");
            }
            Assert.That(big.TicksPerFrame.Skip(1), Has.All.EqualTo(10));
            Assert.That(big.LogText, Is.SupersetOf(new[]
            {
                "party_time: 40 lights, 32 spot lights",
                "party_time: only the first 32 lights take part",
                "party_time: only the first 32 actuators take part",
                "party_time: driving the 32 actuators on light parts, of 32 (seed 2026)",
            }), big.Dump);
            Assert.That(big.Parts.Applied.Where(a => a.Command.Service == Svc.ActuatorSet).Select(a => a.Command.Slot!.Index).Distinct().Count(), Is.EqualTo(32));
            Assert.That(big.Parts.Light(35).Overridden, Is.False, "light 35 is not one of the first 32");
            Assert.That(MaxTaskCycles(big), Is.LessThan(MaxCyclesPerTick), "a tick of 32 lights and 32 actuators stays well inside the 50,000-cycle budget at 20 Hz");
            Assert.That((big.RejectedRequests, big.Overruns, big.Binding.Dropped), Is.EqualTo((0u, 0u, 0L)), big.Dump);
        }
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    /// <summary>The documented look of light <paramref name="i"/> refreshed at tick <paramref name="k"/> (party_time.c).</summary>
    private static void CheckParty(List<string> failures, int k, int i, SimLight l)
    {
        int offset = 49 * i % 80;
        double own = offset / 80.0;
        double hue = (k + offset) % 80 / 80.0, h = 6 * hue;
        double r = Math.Clamp(Math.Abs(h - 3) - 1, 0, 1), g = Math.Clamp(2 - Math.Abs(h - 2), 0, 1), b = Math.Clamp(2 - Math.Abs(h - 4), 0, 1);
        double outer = 0.15 + 0.55 * Wave(k % 60 / 60.0 + own);
        double scale = 0.5 + 1.5 * Wave(k % 20 / 20.0 + own);
        bool spot = l.Type == LightType.Spot;
        var expected = new[] { r, g, b, spot ? 0.5 * outer : l.Stock.InnerRad, spot ? outer : l.Stock.OuterRad, scale * l.Stock.Intensity };
        var actual = new double[] { l.R, l.G, l.B, l.InnerRad, l.OuterRad, l.Intensity };
        for (int j = 0; j < expected.Length; j++)
        {
            if (Math.Abs(expected[j] - actual[j]) > 1e-4 * Math.Max(1, Math.Abs(expected[j])))
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"tick {k} light {i} value {j}: expected {expected[j]:0.#####}, got {actual[j]:0.#####}"));
        }
        if (spot && (l.OuterRad < 0.15f - 1e-5 || l.OuterRad > 0.70f + 1e-5)) failures.Add($"tick {k} light {i}: cone {l.OuterRad} outside 0.15 .. 0.70");
        if (l.Intensity < 0.5f * l.Stock.Intensity - 1e-4 || l.Intensity > 2f * l.Stock.Intensity + 1e-4) failures.Add($"tick {k} light {i}: intensity {l.Intensity} outside half .. twice stock");
    }

    /// <summary>party_time.c's <c>wave</c>: a smoothstep of a triangle over one turn.</summary>
    private static double Wave(double x)
    {
        x %= 1.0;
        double t = x < 0.5 ? 2 * x : 2 - 2 * x;
        return t * t * (3 - 2 * t);
    }

    [Test]
    public void Party_Time_Falls_Back_To_Every_Actuator_And_Can_Be_Told_To()
    {
        // No actuator on a light part (default part ids: lights 100+, actuators 300+): all of them.
        using (SimVehicle v = Vehicle(lights: 2, actuators: 3))
        {
            Load(v, "x_party_time").Run(10.0, PartyTick);
            Assert.That(v.LogText, Has.Member("party_time: no actuator is on a light part: driving all 3 actuators (seed 2026)"), v.Dump);
            Assert.That(Moves(v).Select(m => m.Index).Distinct(), Is.EquivalentTo(new[] { 0, 1, 2 }));
            Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
        }

        // all_actuators on the party vehicle: the cargo bay too.
        using (SimVehicle v = Vehicle(PartySpec()))
        {
            Load(v, "x_party_time");
            v.WriteGlobal("all_actuators", 1u);
            v.Run(10.0, PartyTick);
            Assert.That(v.LogText, Has.Member("party_time: all_actuators: driving all 4 actuators (seed 2026)"), v.Dump);
            Assert.That(Moves(v).Select(m => m.Index).Distinct(), Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
            var failures = new List<string>();
            CheckSchedule(failures, Moves(v), 1, 3.0);
            Assert.That(failures, Is.Empty, () => string.Join("\n", failures));

            // Back to the light parts mid-run: the bay is left where it is.
            v.WriteGlobal("all_actuators", 0u);
            v.Run(0.1, PartyTick);
            int applied = v.Parts.Applied.Count;
            v.Run(10.0, PartyTick);
            Assert.That(v.LogText.Count(l => l == "party_time: driving the 3 actuators on light parts, of 4 (seed 2026)"), Is.EqualTo(1), v.Dump);
            Assert.That(v.Parts.Applied.Skip(applied).Where(a => a.Command.Service == Svc.ActuatorSet && a.Command.Slot!.Index == 1), Is.Empty);
            Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
        }
    }

    [Test]
    public void Party_Time_Without_Actuators_Or_Without_Lights()
    {
        // Lights only: the show runs, one line says there is nothing to actuate.
        using (SimVehicle v = Vehicle(lights: 3))
        {
            Load(v, "x_party_time").Run(5.0, PartyTick);
            Assert.That(v.LogText.Where(l => l.StartsWith("party_time:", StringComparison.Ordinal)).Take(2), Is.EqualTo(new[]
            {
                "party_time: 3 lights, 3 spot lights",
                "party_time: no actuators: nothing to actuate, lights only",
            }), v.Dump);
            Assert.That(v.LogText.Count(l => l.Contains("nothing to actuate", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(v.Parts.Lights.All(l => l.Overridden && l.On), Is.True);
            Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
        }

        // Actuators only: no actuator shares a part with a light, so every actuator is driven.
        using (SimVehicle v = Vehicle(actuators: 2))
        {
            Load(v, "x_party_time").Run(10.0, PartyTick);
            Assert.That(v.LogText, Is.SupersetOf(new[]
            {
                "party_time: 0 lights, 0 spot lights",
                "party_time: no actuator is on a light part: driving all 2 actuators (seed 2026)",
            }), v.Dump);
            var failures = new List<string>();
            CheckSchedule(failures, Moves(v), 0, 1.0);
            CheckSchedule(failures, Moves(v), 1, 1.0);
            Assert.That(failures, Is.Empty, () => string.Join("\n", failures));
            Assert.That(Moves(v).Count(m => m.Index == 1), Is.GreaterThan(3));
            Assert.That(v.Parts.Applied.Where(a => a.Command.Service != Svc.ActuatorSet).Select(a => a.Command.Service), Is.All.EqualTo(Svc.LightsSetAll));
            Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
        }
    }

    [Test]
    public void Party_Time_Waits_For_Slow_Parts_And_Assumes_A_Second_Without_A_Duration()
    {
        using SimVehicle v = Vehicle(new SimVehicleSpec
        {
            Lights = [new SimLightSpec { PartId = 1 }, new SimLightSpec { PartId = 2 }, new SimLightSpec { PartId = 3 }],
            Actuators =
            [
                new SimAnimationSpec { Name = "Fast", PartId = 1, DurationS = 0.4 },
                new SimAnimationSpec { Name = "Slow", PartId = 2, DurationS = 5.0 },
                new SimAnimationSpec { Name = "Silent", PartId = 3, DurationS = 2.0 },
            ],
        });
        Load(v, "x_party_time");
        // Actuator 2 reports no duration from the actuator scan (tick 1) on; its slot stays listed.
        v.Frame(PartyTick);
        v.Parts.Actuator(2).DurationS = 0;
        v.Run(40.0, PartyTick);

        Assert.That(v.ReadGlobal<float>("act_travel_s[2]"), Is.Zero);
        List<Move> moves = Moves(v);
        var failures = new List<string>();
        CheckSchedule(failures, moves, 0, 0.4);
        CheckSchedule(failures, moves, 1, 5.0);
        CheckSchedule(failures, moves, 2, 0, reported: false);
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures));
        int fast = moves.Count(m => m.Index == 0), slow = moves.Count(m => m.Index == 1), silent = moves.Count(m => m.Index == 2);
        Assert.That(slow, Is.LessThan(fast / 2), $"slow {slow}, fast {fast}");
        Assert.That(silent, Is.InRange((int)((40 - StartSpreadS) / (LagS + AssumedMoveS + DwellMaxS + PartyTick)), (int)(40 / (LagS + AssumedMoveS + DwellMinS)) + 1));
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    [Test]
    public void Party_Time_Stop_Resets_The_Lights_Once_And_Leaves_The_Actuators()
    {
        using SimVehicle v = Vehicle(PartySpec());
        Load(v, "x_party_time").Run(6.0, PartyTick);
        var positions = Enumerable.Range(0, 4).Select(i => v.Parts.Actuator(i)).ToList();

        v.WriteGlobal("stop", 1u);
        int applied = v.Parts.Applied.Count;
        v.Run(5.0, PartyTick);
        // The frame after the poke applies the last party tick's commands; then only the resets.
        var after = v.Parts.Applied.Skip(applied).ToList();
        int lastParty = after.FindLastIndex(a => a.Command.Service != Svc.LightReset);
        Assert.That(after.Where(a => a.TimeS > after[0].TimeS + 1e-9).Select(a => (a.Command.Service, a.Command.Slot!.Index)),
            Is.EqualTo(Enumerable.Range(0, 5).Select(i => (Svc.LightReset, i))), "one reset per light, then nothing");
        Assert.That(lastParty, Is.LessThan(after.Count - 5));
        for (int i = 0; i < 5; i++)
        {
            SimLight l = v.Parts.Light(i);
            Assert.That((l.Overridden, l.R, l.G, l.B, l.InnerRad, l.OuterRad, l.Intensity),
                Is.EqualTo((false, l.Stock.R, l.Stock.G, l.Stock.B, l.Stock.InnerRad, l.Stock.OuterRad, l.Stock.Intensity)), $"light {i} is back to stock");
        }
        Assert.That(positions.Select(a => a.PositionS), Is.EqualTo(positions.Select(a => a.GoalS)), "the actuators stopped where they were sent");
        Assert.That(v.LogText, Has.Member("party_time: stopped, 5 lights reset; the actuators stay where they are"), v.Dump);

        // stop = 0: the party goes on, the actuators start again staggered.
        v.WriteGlobal("stop", 0u);
        applied = v.Parts.Applied.Count;
        v.Run(5.0, PartyTick);
        Assert.That(v.LogText, Has.Member("party_time: resumed"));
        Assert.That(Enumerable.Range(0, 5).All(i => v.Parts.Light(i).Overridden), Is.True);
        var resumed = v.Parts.Applied.Skip(applied).Where(a => a.Command.Service == Svc.ActuatorSet).Select(a => new Move(a.TimeS, a.Command.Slot!.Index, a.Command.A)).ToList();
        Assert.That(resumed.Select(m => m.Index).Distinct(), Is.EquivalentTo(new[] { 0, 2, 3 }));
        var firsts = new[] { 0, 2, 3 }.Select(i => TickOf(resumed.First(m => m.Index == i).TimeS)).ToList();
        Assert.That(firsts.Distinct().Count(), Is.EqualTo(3), "restarted in three different ticks");
        Assert.That(v.LogText.Count(l => l.StartsWith("party_time: stopped", StringComparison.Ordinal)), Is.EqualTo(1));
        Assert.That(v.RejectedRequests, Is.Zero, v.Dump);
    }

    [Test]
    public void Party_Time_Skips_Parts_That_Leave()
    {
        using SimVehicle v = Vehicle(PartySpec());
        Load(v, "x_party_time").Run(5.0, PartyTick);
        SimAnimation twin = v.Parts.Actuator(2);
        v.Parts.Detach(twin);
        v.Parts.Detach(v.Parts.Light(4));
        double leftS = v.TimeS;
        v.Run(15.0, PartyTick);

        // The next frame's pump still applies the last tick's commands (issued while the parts were
        // there); the tick after it sees them gone.
        var later = v.Parts.Applied.Where(a => a.TimeS > leftS + 1.5 * PartyTick).ToList();
        Assert.That(later.Where(a => a.Command.Service == Svc.ActuatorSet).Select(a => a.Command.Slot!.Index).Distinct(), Is.EquivalentTo(new[] { 0, 3 }));
        Assert.That(later.Where(a => a.Command.Slot?.Index == 4 && a.Command.Service != Svc.ActuatorSet), Is.Empty, "the flood light is left alone");
        Assert.That(v.LogText.Count(l => l == "party_time: actuator 2 has left the vehicle"), Is.EqualTo(1), v.Dump);
        Assert.That(v.ReadGlobal<uint>("driven"), Is.EqualTo(2u));
        Assert.That((v.RejectedRequests, v.Binding.Rejected), Is.EqualTo((0u, 0L)), v.Dump);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Builds the example in <paramref name="folder"/> as its own project, as the game would, and loads and starts it.</summary>
    private static SimVehicle Load(SimVehicle v, string folder)
    {
        KfsExample e = ExampleCatalog.Find(folder) ?? throw new AssertionException($"no example {folder}");
        if (e.IsKc) v.LoadKc(e.ProjectFiles);
        else v.LoadKasm(e.ProjectFiles);
        Assert.That(v.LastBuild!.Diagnostics, Is.Empty, $"{folder} must build with no warning:\n{v.LastBuild.Text}");
        return v;
    }

    private static string Bits(List<bool> values) => string.Concat(values.Select(b => b ? '1' : '0'));
}
