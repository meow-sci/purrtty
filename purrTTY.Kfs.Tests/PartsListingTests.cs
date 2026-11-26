using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// <see cref="PartsListing"/>, the formatter of <c>purr parts</c>, over simulated part sets: one
/// golden block per case. No runtime: the formatter only reads <see cref="IVehicleParts"/>.
/// </summary>
[TestFixture]
public sealed class PartsListingTests : GameFixture
{
    [Test]
    public void Lights_Shared_Switch_Switchless_Overridden_And_Departed()
    {
        var parts = new SimVehicleParts(new SimVehicleSpec
        {
            Lights =
            [
                new SimLightSpec { Name = "Spotlight Mk1", On = true },
                new SimLightSpec { Name = "Spotlight Mk1", SharesSwitchWith = 0 },
                new SimLightSpec { Name = "Cabin (interior)", Type = LightType.Point, Interior = true },
                new SimLightSpec { Name = "Floodlight" },
                new SimLightSpec { Name = "Strobe", Type = LightType.Point, On = true },
                new SimLightSpec { Name = "Unpowered Spot", On = true, Powered = false },
            ],
        });
        _ = parts.Sets;                               // the lists a run froze
        parts.Light(3).Overridden = true;             // a program changed its appearance
        parts.Detach(parts.Light(4));                 // staged away after the lists were built
        parts.Step(0);

        Assert.That(Text(new ControlledParts("Explorer 1", parts, PartsListingSource.RunningProgram), PartKind.Light), Is.EqualTo("""
            vehicle "Explorer 1" — parts as its running program sees them (generation 1)
              the vehicle has changed since these lists were built: x_parts_rescan() (or a new run) renumbers
            lights (6): type, switch, on/off, lit/dark
                0  Spotlight Mk1     spot   switch group 0   on   lit
                1  Spotlight Mk1     spot   switch group 0   on   lit
                2  Cabin (interior)  point  no switch             lit
                3  Floodlight        spot   switch group 3   off  dark  overridden
                4  Strobe            not present
                5  Unpowered Spot    spot   switch group 5   on   dark
              overridden: a program's colour, cone, intensity or range, kept after it stops; purr lights reset restores the stock look
            """));
    }

    [Test]
    public void Deployables_In_Each_State_With_Links()
    {
        var parts = new SimVehicleParts(new SimVehicleSpec
        {
            Deployables =
            [
                new SimAnimationSpec { Name = "Solar Array XL", DurationS = 8.0, Links = AnimSlot.LinkSolarPanel },
                new SimAnimationSpec { Name = "Antenna", DurationS = 2.5, InitialFraction = 1.0 },
                new SimAnimationSpec { Name = "Radiator", DurationS = 4.0 },
                new SimAnimationSpec { Name = "Chute Bay", DurationS = 1.0, InitialFraction = 1.0, Links = AnimSlot.LinkParachute },
                new SimAnimationSpec { Name = "Spot Arm", DurationS = 2.0, Links = AnimSlot.LinkLight | AnimSlot.LinkSolarPanel },
            ],
        });
        parts.Deployable(2).GoalS = 4.0;              // deploying, a quarter of the way out
        parts.Deployable(2).PositionS = 1.0;
        parts.Deployable(3).GoalS = 0.0;              // retracting, 60 % out
        parts.Deployable(3).PositionS = 0.6;
        parts.Step(0);                                // committed by a physics step: what a program reads

        Assert.That(Text(new ControlledParts("Explorer 1", parts, PartsListingSource.Computer), PartKind.Deployable), Is.EqualTo("""
            vehicle "Explorer 1" — parts as its kFS computer holds them (generation 1); a program started now gets fresh lists
            deployables (5): state, position, travel time
                0  Solar Array XL  retracted      0%    8.0 s  drives a solar panel
                1  Antenna         deployed     100%    2.5 s
                2  Radiator        deploying     25%    4.0 s
                3  Chute Bay       retracting    60%    1.0 s  also driven by a parachute
                4  Spot Arm        retracted      0%    2.0 s  drives a solar panel, aims a light
            """));
    }

    [Test]
    public void Actuators_Mid_Travel_And_At_Rest()
    {
        var parts = new SimVehicleParts(new SimVehicleSpec
        {
            Actuators =
            [
                new SimAnimationSpec { Name = "Cargo Bay 2m", DurationS = 6.0 },
                new SimAnimationSpec { Name = "Ramp", DurationS = 12.0, InitialFraction = 1.0 },
            ],
        });
        parts.Actuator(0).GoalS = 4.5;                // driving to 0.75, at 0.25 now
        parts.Actuator(0).PositionS = 1.5;
        _ = parts.Sets;
        parts.Detach(parts.Actuator(1));

        Assert.That(Text(new ControlledParts("Hauler", parts, PartsListingSource.RunningProgram), PartKind.Actuator), Is.EqualTo("""
            vehicle "Hauler" — parts as its running program sees them (generation 1)
              the vehicle has changed since these lists were built: x_parts_rescan() (or a new run) renumbers
            actuators (2): position → target, travel time
                0  Cargo Bay 2m  0.25 → 0.75    6.0 s
                1  Ramp          not present
            """));
    }

    [Test]
    public void Preview_Of_Every_Kind_With_Empty_Sections_And_A_Long_Name()
    {
        var parts = new SimVehicleParts(new SimVehicleSpec
        {
            Lights = [new SimLightSpec { Name = "A Very Long Experimental Searchlight Assembly" }],
            Actuators = [new SimAnimationSpec { Name = "Arm", DurationS = 1.0 }],
        });

        Assert.That(Text(new ControlledParts("Probe", parts, PartsListingSource.Preview), PartKind.All), Is.EqualTo("""
            vehicle "Probe" — parts as a program started now would see them (preview: indices are fixed when a program starts)
            lights (1): type, switch, on/off, lit/dark
                0  A Very Long Experimental Search…  spot   switch group 0   off  dark
            deployables: none
            actuators (1): position → target, travel time
                0  Arm                               0.00 → 0.00    1.0 s
            """));
    }

    [Test]
    public void Output_Is_Culture_Invariant()
    {
        var parts = new SimVehicleParts(new SimVehicleSpec { Actuators = [new SimAnimationSpec { Name = "Arm", DurationS = 1.5, InitialFraction = 0.5 }] });
        var controlled = new ControlledParts("x", parts, PartsListingSource.Preview);
        string invariant = Text(controlled, PartKind.All);

        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.That(Text(controlled, PartKind.All), Is.EqualTo(invariant));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
        Assert.That(invariant, Does.Contain("0.50 → 0.50    1.5 s"));
    }

    private static string Text(ControlledParts parts, PartKind kind) => string.Join("\n", PartsListing.Lines(parts, kind));
}
