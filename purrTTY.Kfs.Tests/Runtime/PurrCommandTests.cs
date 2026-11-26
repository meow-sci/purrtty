using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.ExamplePrograms;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// The <c>purr</c> verbs with the extension installed (the shared runtime) and a simulated console
/// host: <c>purr status</c> after a program ran, <c>purr parts</c> through the same resolution the
/// game uses (<see cref="ControlledParts.For"/> over the vehicle's computer: the bound computer's
/// frozen lists, or a preview), and <c>purr examples</c> into an in-memory workspace.
/// </summary>
[TestFixture]
public sealed class PurrCommandTests : RuntimeFixture
{
    private static List<string> Run(SimConsoleHost host, string args)
    {
        host.Lines.Clear();
        PurrConsole.Run(args, host);
        return host.Lines;
    }

    [Test]
    public void Help_Says_The_Extension_Is_Active()
    {
        var host = new SimConsoleHost();
        Assert.That(Run(host, "help")[0], Is.EqualTo("purr: the purrTTY kFS extension is active; verbs (purr <verb> [arguments]):"));
    }

    [Test]
    public void Lights_Reset_Restores_What_A_Stopped_Program_Left()
    {
        using SimVehicle v = Vehicle(lights: 3);
        v.LoadKc(Kc.Ticked("if (k == 1u) { x_light_set_intensity(0u, 9.0f); x_light_set_range(2u, 5.0f); }"));
        v.Run(0.5, 0.1);
        v.Stop();
        v.Run(0.2, 0.1);
        Assert.That(v.Parts.Light(0).Overridden && v.Parts.Light(2).Overridden, Is.True, "the program's look outlives it");

        var host = new SimConsoleHost().Control(v);
        List<string> parts = Run(host, "parts lights");
        Assert.That(parts, Has.Member("  overridden: a program's colour, cone, intensity or range, kept after it stops; purr lights reset restores the stock look"));

        Assert.That(Run(host, "lights reset"), Is.EqualTo(new[] { "purr lights reset: 2 lights of \"sim\" back to their stock look" }));
        Assert.That((v.Parts.Light(0).Overridden, v.Parts.Light(0).Intensity, v.Parts.Light(2).RangeM), Is.EqualTo((false, 2f, 30f)));
        Assert.That(Run(host, "parts lights"), Has.None.StartsWith("  overridden:"));
        Assert.That(Run(host, "lights reset"), Is.EqualTo(new[] { "purr lights reset: no light of \"sim\" has a program's look" }));

        // Another vehicle's light: only "all" reaches it.
        using SimVehicle other = Vehicle(lights: 1, name: "other");
        other.Parts.Light(0).Intensity = 7f;
        other.Parts.Light(0).Overridden = true;
        host.AllParts.Add(other.Parts);
        Assert.That(Run(host, "lights reset"), Is.EqualTo(new[] { "purr lights reset: no light of \"sim\" has a program's look" }));
        Assert.That(Run(host, "lights reset all"), Is.EqualTo(new[] { "purr lights reset all: 1 light back to its stock look, on every vehicle" }));
        Assert.That(other.Parts.Light(0).Overridden, Is.False);

        Assert.That(Run(host, "lights"), Is.EqualTo(new[] { "purr lights: usage: purr lights reset [all] (the controlled vehicle's lights, or every vehicle's)" }));
        Assert.That(Run(host, "lights reset everything")[0], Does.StartWith("purr lights: usage:"));
        Assert.That(Run(new SimConsoleHost(), "lights reset"), Is.EqualTo(new[] { "purr lights reset: no vehicle is controlled (purr lights reset all restores every vehicle's lights)" }));
    }

    [Test]
    public void Status_Reflects_Counters_After_A_Program_Ran()
    {
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) {
                    x_light_set_on(0u, 1u);
                    kfs_logf("n %u", x_light_count());
                }
            """));
        v.Run(0.5, 0.1);
        Assert.That(v.Parts.Light(0).On, Is.True, v.Dump);

        var host = new SimConsoleHost();
        List<string> lines = Run(host, "status");
        KfsExtensionStatus status = KfsExtension.Status;
        KfsExtensionCounters c = status.Counters;
        Assert.That(lines, Is.EqualTo(status.Describe().Split('\n').Select(ConsoleText.ToAscii)), "purr status prints Describe(), in ASCII");
        Assert.That(lines[0], Does.StartWith("purrTTY kFS extension: active - 22 services 0x5000-0x5033, 3 hooks (+1 optional), tables published"));
        Assert.That(lines, Has.Member("  console:  none (installed without a game console)"));
        Assert.That(lines, Has.Some.StartsWith($"  vehicles: {c.Bindings} bound; requests {c.Accepted} accepted"));
        Assert.That(c.Accepted, Is.GreaterThan(0));
        Assert.That(c.Answered, Is.GreaterThan(0));
        Assert.That(c.Applied, Is.GreaterThan(0));
        Assert.That(KfsExtension.StatusSummary, Is.EqualTo("active — 22 services"));
    }

    // ------------------------------------------------------------------ purr parts

    [Test]
    public void Parts_Of_A_Running_Program_Are_Its_Frozen_Lists_With_The_Generation()
    {
        using SimVehicle v = Vehicle(new SimVehicleSpec
        {
            Name = "Explorer 1",
            Lights =
            [
                new SimLightSpec { Name = "Spotlight Mk1", On = true },
                new SimLightSpec { Name = "Spotlight Mk1", SharesSwitchWith = 0 },
                new SimLightSpec { Name = "Cabin (interior)", Type = LightType.Point, Interior = true },
            ],
            Deployables = [new SimAnimationSpec { Name = "Solar Array XL", DurationS = 8.0, Links = 1 }],
            Actuators = [new SimAnimationSpec { Name = "Cargo Bay 2m", DurationS = 6.0 }],
        });
        v.LoadKc(Kc.Ticked("if (k == 2u) { x_deploy_set(0u, 1u); x_parts_rescan(); }"));
        v.Run(1.0, 0.1);
        var host = new SimConsoleHost().Control(v);
        int generation = v.Parts.Sets.Generation;
        Assert.That(generation, Is.GreaterThan(1), "the run's lists and the rescan each built a generation");

        Assert.That(string.Join("\n", Run(host, "parts")), Is.EqualTo($"""
            vehicle "Explorer 1" - parts as its running program sees them (generation {generation})
            lights (3): type, switch, on/off, lit/dark
                0  Spotlight Mk1     spot   switch group 0   on   lit
                1  Spotlight Mk1     spot   switch group 0   on   lit
                2  Cabin (interior)  point  no switch             lit
            deployables (1): state, position, travel time
                0  Solar Array XL    deploying      8%    8.0 s  drives a solar panel
            actuators (1): position -> target, travel time
                0  Cargo Bay 2m      0.00 -> 0.00    6.0 s
            """), "x_deploy_set in tick 2, applied at 0.3 s: 0.7 s of 8 s travelled");

        // One kind only, and the dirty hint once the vehicle changed under the frozen lists.
        v.Parts.Detach(v.Parts.Light(2));
        Assert.That(Run(host, "parts lights"), Is.EqualTo(new[]
        {
            $"vehicle \"Explorer 1\" - parts as its running program sees them (generation {generation})",
            "  the vehicle has changed since these lists were built: x_parts_rescan() (or a new run) renumbers",
            "lights (3): type, switch, on/off, lit/dark",
            "    0  Spotlight Mk1     spot   switch group 0   on   lit",
            "    1  Spotlight Mk1     spot   switch group 0   on   lit",
            "    2  Cabin (interior)  not present",
        }));
        Assert.That(Run(host, "parts DEPLOY"), Has.Count.EqualTo(4).And.Member("deployables (1): state, position, travel time"));
        Assert.That(Run(host, "parts actuator"), Has.Count.EqualTo(4).And.Member("actuators (1): position -> target, travel time"));
        Assert.That(Run(host, "parts engines"), Is.EqualTo(new[] { "purr parts: unknown kind 'engines' (usage: purr parts [lights | deploy | actuators])" }));
        Assert.That(v.Parts.Sets.Generation, Is.EqualTo(generation), "listing never rebuilds");
    }

    [Test]
    public void Parts_Of_A_Stopped_Computer_Say_The_Next_Run_Rebuilds()
    {
        using SimVehicle v = Vehicle(lights: 1, name: "Probe");
        v.LoadKc(Kc.Ticked(""));
        v.Run(0.3, 0.1);
        v.Stop();
        var host = new SimConsoleHost().Control(v);

        List<string> lines = Run(host, "parts");
        Assert.That(lines[0], Is.EqualTo($"vehicle \"Probe\" - parts as its kFS computer holds them (generation {v.Parts.Sets.Generation}); a program started now gets fresh lists"));
        Assert.That(lines[2], Is.EqualTo("    0  Light 0   spot   switch group 0   off  dark"));
    }

    [Test]
    public void Parts_Without_A_Bound_Computer_Are_A_Preview()
    {
        var spec = new SimVehicleSpec
        {
            Lights = [new SimLightSpec { Name = "Nav Light", Type = LightType.Point, On = true }],
            Deployables = [new SimAnimationSpec { Name = "Antenna", DurationS = 2.0, InitialFraction = 1.0 }],
        };
        var host = new SimConsoleHost().ControlWithoutComputer("Lander", spec);
        Assert.That(Run(host, "parts"), Is.EqualTo(new[]
        {
            "vehicle \"Lander\" - parts as a program started now would see them (preview: indices are fixed when a program starts)",
            "lights (1): type, switch, on/off, lit/dark",
            "    0  Nav Light  point  switch group 0   on   lit",
            "deployables (1): state, position, travel time",
            "    0  Antenna    deployed     100%    2.0 s",
            "actuators: none",
        }));

        // A vehicle whose computer has no binding (unbound) is previewed the same way.
        SimVehicle gone = Vehicle(lights: 1, name: "Old");
        gone.Dispose();
        host.Control(gone, spec);
        Assert.That(Run(host, "parts")[0], Does.StartWith("vehicle \"Old\" - parts as a program started now would see them (preview"));
    }

    [Test]
    public void Parts_Without_A_Controlled_Vehicle_Says_So()
    {
        var host = new SimConsoleHost();
        Assert.That(Run(host, "parts"), Is.EqualTo(new[] { "purr parts: no vehicle is controlled (take control of one and try again)" }));
    }

    // ------------------------------------------------------------------ purr examples

    [Test]
    public void Examples_Are_Written_Kept_And_Overwritten_With_Force()
    {
        var host = new SimConsoleHost();
        List<string> first = Run(host, "examples");
        Assert.That(host.Files.Keys, Is.EquivalentTo(ExampleCatalog.Files.Keys));
        foreach (var (path, text) in ExampleCatalog.Files) Assert.That(host.Files[path], Is.EqualTo(text), path);
        Assert.That(host.Files.Keys.Select(p => p[..p.IndexOf('/')]).Distinct(), Is.EquivalentTo(ExampleCatalog.All.Select(e => e.Folder)), "one folder per example");
        Assert.That(first[0], Is.EqualTo($"purr examples: {ExampleCatalog.Files.Count} written, 0 kept, 0 failed, in <sim workspace>"));
        Assert.That(first, Has.Member("  wrote   x_blink/blink.c"));
        Assert.That(first, Has.Member("  x_deploy       deploy the solar arrays above the atmosphere, retract them below (MSG_FLIGHT_DATA, LINKS)"));
        Assert.That(first[^1], Is.EqualTo("load one onto the controlled vehicle: kfs on; kfs load x_blink/blink.c --vehicle; kfs start"));

        // Again: nothing is overwritten.
        host.Files["x_blink/blink.c"] = "/* mine */";
        host.Writes.Clear();
        List<string> second = Run(host, "examples");
        Assert.That(host.Writes, Is.Empty);
        Assert.That(host.Files["x_blink/blink.c"], Is.EqualTo("/* mine */"));
        Assert.That(second[0], Is.EqualTo($"purr examples: 0 written, {ExampleCatalog.Files.Count} kept, 0 failed, in <sim workspace>"));
        Assert.That(second, Has.Member("  kept    x_blink/blink.c (exists; purr examples --force overwrites it)"));

        // --force overwrites every file; a failing write is reported and the rest still written.
        host.FailingPaths.Add("x_parts/parts_list.c");
        List<string> forced = Run(host, "examples --force");
        Assert.That(host.Writes, Is.EquivalentTo(ExampleCatalog.Files.Keys));
        Assert.That(host.Files["x_blink/blink.c"], Is.EqualTo(ExampleCatalog.Files["x_blink/blink.c"]));
        Assert.That(forced[0], Is.EqualTo($"purr examples: {ExampleCatalog.Files.Count - 1} written, 0 kept, 1 failed, in <sim workspace>"));
        Assert.That(forced, Has.Member("  FAILED  x_parts/parts_list.c: injected failure writing x_parts/parts_list.c"));

        Assert.That(Run(host, "examples --all"), Is.EqualTo(new[] { "purr examples: unexpected argument '--all' (usage: purr examples [--force])" }));
    }
}
