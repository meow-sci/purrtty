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
            "x_blink", "x_light_show", "x_deploy", "x_actuator", "x_parts", "x_blink_kasm",
        }));
        Assert.That(ExampleCatalog.Files.Keys, Is.EquivalentTo(new[]
        {
            "x_blink/blink.c", "x_light_show/light_show.c", "x_deploy/deploy_when_safe.c",
            "x_actuator/actuator_sweep.c", "x_parts/parts_list.c", "x_blink_kasm/blink.kasm", "x_blink_kasm/purrtty.kinc",
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
        Assert.That(ExampleCatalog.All.Count(e => e.IsKc), Is.EqualTo(5));
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
