using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Game;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// The pure decisions of the game back end (<see cref="KsaPartRules"/>), over plain values: what
/// <c>KsaVehicleParts</c> decides about live modules, tested without the game.
/// </summary>
[TestFixture]
public sealed class KsaPartRulesTests
{
    private sealed class Switch(string name)
    {
        public override string ToString() => name;
    }

    [Test]
    public void Effective_Switch_Needs_A_Switch_On_This_Tree_And_No_Interior_Template()
    {
        object vehicleTree = new(), otherTree = new();
        var sw = new Switch("s");

        Assert.That(KsaPartRules.EffectiveSwitch(false, sw, vehicleTree, vehicleTree), Is.SameAs(sw), "an ordinary light on this vehicle");
        Assert.That(KsaPartRules.EffectiveSwitch(true, sw, vehicleTree, vehicleTree), Is.Null, "DisableInIva: an interior fill light ignores switch and power");
        Assert.That(KsaPartRules.EffectiveSwitch<Switch>(false, null, vehicleTree, vehicleTree), Is.Null, "a part without a switch");
        Assert.That(KsaPartRules.EffectiveSwitch(false, sw, otherTree, vehicleTree), Is.Null, "a switch on another vehicle's tree is never ours");
        Assert.That(KsaPartRules.EffectiveSwitch(false, sw, null, vehicleTree), Is.Null, "a switch on no tree");
        Assert.That(KsaPartRules.EffectiveSwitch(false, sw, null, null), Is.Null, "a vehicle without a tree has no switches");
    }

    /// <summary>
    /// The switch groups the back end publishes: <see cref="PartMath.SwitchGroups{TSwitch}"/> over
    /// the effective switches, so lights whose switch is ineffective form groups of their own.
    /// </summary>
    [Test]
    public void Switch_Groups_Over_Effective_Switches()
    {
        object tree = new(), elsewhere = new();
        Switch a = new("a"), b = new("b"), c = new("c");
        // (part switch, switch's tree, DisableInIva) per light, in instance-id order.
        var lights = new (Switch? Switch, object? Tree, bool Interior)[]
        {
            (a, tree, false),       // 0: group 0
            (a, tree, false),       // 1: same part as 0 → group 0
            (null, null, false),    // 2: no switch → itself
            (a, tree, true),        // 3: interior inheriting a's switch → itself (no effective switch)
            (b, tree, false),       // 4: group 4
            (c, elsewhere, false),  // 5: switch on another tree → itself
            (b, tree, false),       // 6: group 4
            (a, tree, false),       // 7: group 0
        };
        Switch?[] effective = lights.Select(l => KsaPartRules.EffectiveSwitch(l.Interior, l.Switch, l.Tree, tree)).ToArray();

        Assert.That(PartMath.SwitchGroups(effective), Is.EqualTo(new[] { 0, 0, 2, 3, 4, 5, 4, 0 }));
        Assert.That(effective.Select(s => s is not null), Is.EqualTo(new[] { true, true, false, false, true, false, true, true }), "HAS_SWITCH per light");
    }

    [Test]
    public void Links_Compose_The_Documented_Bits()
    {
        Assert.That(KsaPartRules.Links(false, false, false), Is.Zero);
        Assert.That(KsaPartRules.Links(true, false, false), Is.EqualTo(1u).And.EqualTo(AnimSlot.LinkSolarPanel));
        Assert.That(KsaPartRules.Links(false, true, false), Is.EqualTo(2u).And.EqualTo(AnimSlot.LinkParachute));
        Assert.That(KsaPartRules.Links(false, false, true), Is.EqualTo(4u).And.EqualTo(AnimSlot.LinkLight));
        Assert.That(KsaPartRules.Links(true, true, true), Is.EqualTo(7u));
        Assert.That(KsaPartRules.Links(true, false, true), Is.EqualTo(5u));
    }

    [TestCase(2f, true)]
    [TestCase(1e-6f, true)]
    [TestCase(0f, false)]
    [TestCase(-1f, false)]
    [TestCase(float.NaN, false)]
    [TestCase(float.PositiveInfinity, false)]
    public void Only_Animations_With_A_Finite_Positive_Length_Are_Listed(float durationS, bool listed) =>
        Assert.That(KsaPartRules.HasTravel(durationS), Is.EqualTo(listed));

    [Test]
    public void Show_Deploy_Retract_Picks_The_List()
    {
        Assert.That(KsaPartRules.AnimationKind(true), Is.EqualTo(PartKind.Deployable));
        Assert.That(KsaPartRules.AnimationKind(false), Is.EqualTo(PartKind.Actuator));
    }

    [Test]
    public void Fallback_State_Is_That_Of_An_Animation_At_Its_Goal()
    {
        Assert.That(PartMath.FromGameDeploymentState(KsaPartRules.FallbackGameDeploymentState(2f)), Is.EqualTo(DeployState.Deployed));
        Assert.That(PartMath.FromGameDeploymentState(KsaPartRules.FallbackGameDeploymentState(0f)), Is.EqualTo(DeployState.Retracted));
        Assert.That(PartMath.FromGameDeploymentState(KsaPartRules.FallbackGameDeploymentState(-1f)), Is.EqualTo(DeployState.Retracted));
    }

    [Test]
    public void Unknown_Light_Types_Read_As_Point()
    {
        Assert.That(KsaPartRules.LightTypeOf(0), Is.EqualTo(LightType.Spot));
        Assert.That(KsaPartRules.LightTypeOf(1), Is.EqualTo(LightType.Point));
        Assert.That(KsaPartRules.LightTypeOf(7), Is.EqualTo(LightType.Point), "no cone is written to a type this build does not know");
    }

    [Test]
    public void Key_Joins_Part_Template_And_Module_Key()
    {
        Assert.That(KsaPartRules.Key("Spotlight_Mk1", "Light"), Is.EqualTo("Spotlight_Mk1/Light"));
        Assert.That(KsaPartRules.Key("Spotlight_Mk1", ""), Is.EqualTo("Spotlight_Mk1"));
        Assert.That(KsaPartRules.Key(null, "Light"), Is.EqualTo("Light"));
        Assert.That(KsaPartRules.Key(null, null), Is.Empty);
    }

    [Test]
    public void Is_First_Compares_References()
    {
        Switch a = new("a"), b = new("b");
        Assert.That(KsaPartRules.IsFirst<Switch>(new[] { a, b }, a), Is.True);
        Assert.That(KsaPartRules.IsFirst<Switch>(new[] { a, b }, b), Is.False);
        Assert.That(KsaPartRules.IsFirst<Switch>(Array.Empty<Switch>(), a), Is.False);
        Assert.That(KsaPartRules.IsFirst<Switch>(new[] { new Switch("a") }, a), Is.False, "an equal-looking object is not the same module");
    }

    [Test]
    public void Dirty_Sees_Count_Changes_And_Equal_Count_Swaps()
    {
        Assert.That(KsaPartRules.Dirty(2, 1, 1, 2, 1, 1, anyFrozenDeparted: false), Is.False, "nothing changed");
        Assert.That(KsaPartRules.Dirty(3, 1, 1, 2, 1, 1, false), Is.True, "a light docked on");
        Assert.That(KsaPartRules.Dirty(2, 0, 1, 2, 1, 1, false), Is.True, "a deployable left");
        Assert.That(KsaPartRules.Dirty(2, 1, 2, 2, 1, 1, false), Is.True, "an actuator arrived");
        Assert.That(KsaPartRules.Dirty(2, 1, 1, 2, 1, 1, anyFrozenDeparted: true), Is.True, "one left, one arrived: equal counts, still dirty");
    }
}

/// <summary>The rules that mirror game code, checked against the game's own code where it is callable offline.</summary>
[TestFixture]
public sealed class KsaPartRulesParityTests : GameFixture
{
    [Test]
    public void Derived_State_Matches_The_Game()
    {
        float[] times = [float.NegativeInfinity, -1f, 0f, 1e-6f, 0.5f, 1f, 2f, float.PositiveInfinity, float.NaN];
        foreach (float goal in times)
        {
            foreach (float current in times)
            {
                Assert.That(KsaPartRules.DeriveGameDeploymentState(goal, current), Is.EqualTo(GameDerive(goal, current)), $"goal {goal}, current {current}");
            }
        }
    }

    [Test]
    public void Game_Enumerations_Have_The_Values_The_Rules_Assume()
    {
        Assert.That(GameDeploymentStates(), Is.EqualTo(new[] { ("Deployed", 0), ("Retracted", 1), ("Deploying", 2), ("Retracting", 3), ("Broken", 4) }));
        Assert.That(KsaPartRules.LightTypeOf(GameSpot()), Is.EqualTo(LightType.Spot));
        Assert.That(KsaPartRules.LightTypeOf(GamePoint()), Is.EqualTo(LightType.Point));
    }

    // Helpers: game types in bodies only.
    private static int GameDerive(float goal, float current) => (int)KSA.KeyframeAnimationModule.DeriveDeploymentState(goal, current);

    private static (string, int)[] GameDeploymentStates()
    {
        // A loop, not a lambda: a lambda over a game enum would put a game-typed delegate field
        // into a compiler-generated class of this assembly (see ModuleInit.cs).
        var values = Enum.GetValues<KSA.KeyframeAnimationModule.DeploymentState>();
        var named = new (string, int)[values.Length];
        for (int i = 0; i < values.Length; i++) named[i] = (values[i].ToString(), (int)values[i]);
        return named;
    }

    private static int GameSpot() => (int)KSA.LightModule.TemplateData.LightType.Spot;

    private static int GamePoint() => (int)KSA.LightModule.TemplateData.LightType.Point;
}
