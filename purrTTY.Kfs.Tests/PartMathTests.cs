using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Tests;

/// <summary>The pure clamps and conversions of plan §4.1 that both parts back ends share.</summary>
[TestFixture]
public sealed class PartMathTests
{
    private const float Inf = float.PositiveInfinity;

    [TestCase(-1f, 0f)]
    [TestCase(0f, 0f)]
    [TestCase(0.25f, 0.25f)]
    [TestCase(1f, 1f)]
    [TestCase(2f, 1f)]
    [TestCase(float.NaN, 0f)]
    [TestCase(float.PositiveInfinity, 1f)]
    [TestCase(float.NegativeInfinity, 0f)]
    [TestCase(float.MaxValue, 1f)]
    public void PartMath_Color_Clamps_To_Unit(float input, float expected) =>
        Assert.That(PartMath.Color(input), Is.EqualTo(expected));

    [TestCase(-1f, PartMath.MinOuterConeRad)]
    [TestCase(0f, PartMath.MinOuterConeRad)]
    [TestCase(0.6f, 0.6f)]
    [TestCase(3f, PartMath.MaxOuterConeRad)]
    [TestCase(float.NaN, PartMath.MinOuterConeRad)]
    [TestCase(float.PositiveInfinity, PartMath.MaxOuterConeRad)]
    [TestCase(float.NegativeInfinity, PartMath.MinOuterConeRad)]
    public void PartMath_Outer_Cone_Is_Banded(float input, float expected) =>
        Assert.That(PartMath.OuterCone(input), Is.EqualTo(expected));

    [Test]
    public void PartMath_Cone_Keeps_Inner_Within_Outer()
    {
        Assert.That(PartMath.Cone(0.2f, 0.6f), Is.EqualTo((0.2f, 0.6f)));
        Assert.That(PartMath.Cone(0.9f, 0.6f), Is.EqualTo((0.6f, 0.6f)), "inner above outer is lowered to outer");
        Assert.That(PartMath.Cone(2f, 3f), Is.EqualTo((PartMath.MaxOuterConeRad, PartMath.MaxOuterConeRad)), "outer is clamped first");
        Assert.That(PartMath.Cone(-1f, 0.5f), Is.EqualTo((0f, 0.5f)));
        Assert.That(PartMath.Cone(0.3f, 0f), Is.EqualTo((PartMath.MinOuterConeRad, PartMath.MinOuterConeRad)));
        Assert.That(PartMath.Cone(float.NaN, 0.5f), Is.EqualTo((0f, 0.5f)));
        Assert.That(PartMath.Cone(Inf, Inf), Is.EqualTo((PartMath.MaxOuterConeRad, PartMath.MaxOuterConeRad)));
        Assert.That(PartMath.Cone(float.NegativeInfinity, float.NaN), Is.EqualTo((0f, PartMath.MinOuterConeRad)));
    }

    [TestCase(0.3f, -1f, PartMath.MinOuterConeRad)]          // an outer bound below the band: banded first, never an exception
    [TestCase(0.3f, float.NaN, PartMath.MinOuterConeRad)]
    [TestCase(0.3f, float.PositiveInfinity, 0.3f)]
    [TestCase(5f, float.PositiveInfinity, PartMath.MaxOuterConeRad)]
    [TestCase(float.NaN, 0.5f, 0f)]
    public void PartMath_Inner_Cone_Is_Total(float inner, float outer, float expected) =>
        Assert.That(PartMath.InnerCone(inner, outer), Is.EqualTo(expected));

    // Review C8: intensity and range have a ceiling, so float.MaxValue (which passes the VM's
    // finite:>=0 domain) cannot reach a light.
    [TestCase(-3f, 0f)]
    [TestCase(0f, 0f)]
    [TestCase(42.5f, 42.5f)]
    [TestCase(1e6f, 1e6f)]
    [TestCase(1.5e6f, 1e6f)]
    [TestCase(float.MaxValue, 1e6f)]
    [TestCase(float.PositiveInfinity, 1e6f)]
    [TestCase(float.NegativeInfinity, 0f)]
    [TestCase(float.NaN, 0f)]
    public void PartMath_Magnitudes_Are_Clamped(float input, float expected)
    {
        Assert.That(PartMath.Intensity(input), Is.EqualTo(expected));
        Assert.That(PartMath.RangeM(input), Is.EqualTo(expected));
    }

    [Test]
    public void PartMath_Ceilings_Are_The_Documented_Values()
    {
        Assert.That(PartMath.MaxIntensity, Is.EqualTo(1_000_000f));
        Assert.That(PartMath.MaxRangeM, Is.EqualTo(1_000_000f));
        Assert.That(XCatalog.Catalog.ByName("x_light_set_intensity")!.Params[1].Doc, Does.Contain("at most 1e6"));
        Assert.That(XCatalog.Catalog.ByName("x_light_set_range")!.Params[1].Doc, Does.Contain("at most 1e6"));
    }

    [Test]
    public void PartMath_Fraction_Seconds_Conversions()
    {
        Assert.That(PartMath.FractionToSeconds(0.25f, 8.0), Is.EqualTo(2.0));
        Assert.That(PartMath.FractionToSeconds(1.5f, 8.0), Is.EqualTo(8.0), "fractions are clamped to [0, 1]");
        Assert.That(PartMath.FractionToSeconds(-0.5f, 8.0), Is.EqualTo(0.0));
        Assert.That(PartMath.FractionToSeconds(float.NaN, 8.0), Is.EqualTo(0.0));
        Assert.That(PartMath.FractionToSeconds(Inf, 8.0), Is.EqualTo(8.0));
        Assert.That(PartMath.FractionToSeconds(0.5f, 0.0), Is.EqualTo(0.0), "no duration, no travel");
        Assert.That(PartMath.FractionToSeconds(0.5f, double.NaN), Is.EqualTo(0.0));
        Assert.That(PartMath.FractionToSeconds(0f, double.PositiveInfinity), Is.EqualTo(0.0), "an unusable duration gives 0, never NaN");
        Assert.That(PartMath.FractionToSeconds(1f, double.PositiveInfinity), Is.EqualTo(0.0));
        Assert.That(PartMath.SecondsToFraction(2.0, 8.0), Is.EqualTo(0.25f));
        Assert.That(PartMath.SecondsToFraction(2.0, 0.0), Is.EqualTo(0f));
        Assert.That(PartMath.SecondsToFraction(double.NaN, 8.0), Is.EqualTo(0f));
        Assert.That(PartMath.SecondsToFraction(2.0, double.NaN), Is.EqualTo(0f));
        Assert.That(PartMath.SecondsToFraction(double.PositiveInfinity, 8.0), Is.EqualTo(1f));
        Assert.That(PartMath.SecondsToFraction(9.0, 8.0), Is.EqualTo(1f), "a fraction of the travel stays within [0, 1]");
        Assert.That(PartMath.SecondsToFraction(-1.0, 8.0), Is.EqualTo(0f));
        Assert.That(PartMath.SecondsToFraction(PartMath.FractionToSeconds(0.7f, 3.0), 3.0), Is.EqualTo(0.7f).Within(1e-6f));
    }

    // Independent of the implementation: the states a program reads (X_STATE_*) and whether the
    // part is moving, stated per case. NONE (0) is never derived from a present animation.
    [TestCase(0.0, 0.0, 2u, false)]    // at rest, goal 0: X_STATE_RETRACTED
    [TestCase(2.0, 2.0, 1u, false)]    // at rest, goal above 0: X_STATE_DEPLOYED
    [TestCase(0.5, 2.0, 3u, true)]     // X_STATE_DEPLOYING
    [TestCase(1.5, 0.0, 4u, true)]     // X_STATE_RETRACTING
    [TestCase(1.5, 1.0, 4u, true)]
    [TestCase(1.0, 1.0, 1u, false)]
    [TestCase(double.NaN, 1.0, 3u, true)]   // a non-finite time counts as 0
    [TestCase(double.NaN, double.NaN, 2u, false)]
    [TestCase(double.PositiveInfinity, 0.0, 2u, false)]
    public void PartMath_State_Derivation(double position, double goal, uint expectedState, bool expectedMoving)
    {
        Assert.That((uint)PartMath.State(position, goal), Is.EqualTo(expectedState));
        Assert.That(PartMath.Moving(position, goal), Is.EqualTo(expectedMoving));
    }

    [Test]
    public void Deploy_State_Values_Are_The_Program_Constants()
    {
        // The program-facing numbers (part of the image format): 0 means "no part".
        Assert.That(new[] { DeployState.None, DeployState.Deployed, DeployState.Retracted, DeployState.Deploying, DeployState.Retracting, DeployState.Broken }
            .Select(s => (uint)s), Is.EqualTo(new uint[] { 0, 1, 2, 3, 4, 5 }));
        Assert.That(XCatalog.DeployStateEnum.Constants, Is.EqualTo(new[]
        {
            ("X_STATE_NONE", 0), ("X_STATE_DEPLOYED", 1), ("X_STATE_RETRACTED", 2), ("X_STATE_DEPLOYING", 3), ("X_STATE_RETRACTING", 4), ("X_STATE_BROKEN", 5),
        }));
    }

    // The game's KeyframeAnimationModule.DeploymentState: Deployed 0, Retracted 1, Deploying 2,
    // Retracting 3, Broken 4. A back end reading it converts here.
    [TestCase(0, DeployState.Deployed)]
    [TestCase(1, DeployState.Retracted)]
    [TestCase(2, DeployState.Deploying)]
    [TestCase(3, DeployState.Retracting)]
    [TestCase(4, DeployState.Broken)]
    [TestCase(5, DeployState.None)]
    [TestCase(-1, DeployState.None)]
    public void Game_Deployment_State_Converts_At_The_Boundary(int game, DeployState expected) =>
        Assert.That(PartMath.FromGameDeploymentState(game), Is.EqualTo(expected));

    [Test]
    public void PartMath_Slew_Lands_Exactly_On_The_Goal()
    {
        Assert.That(PartMath.Slew(0.0, 2.0, 0.5), Is.EqualTo(0.5));
        Assert.That(PartMath.Slew(1.8, 2.0, 0.5), Is.EqualTo(2.0));
        Assert.That(PartMath.Slew(2.0, 0.0, 0.5), Is.EqualTo(1.5));
        Assert.That(PartMath.Slew(0.2, 0.0, 0.5), Is.EqualTo(0.0));
        Assert.That(PartMath.Slew(1.0, 1.0, 0.5), Is.EqualTo(1.0));
        Assert.That(PartMath.Slew(0.0, 2.0, 0.0), Is.EqualTo(0.0));
        Assert.That(PartMath.Slew(0.0, 2.0, double.NaN), Is.EqualTo(0.0), "no usable time step, no movement");
        Assert.That(PartMath.Slew(0.0, 2.0, double.PositiveInfinity), Is.EqualTo(2.0));
        Assert.That(PartMath.Slew(double.NaN, 2.0, 0.5), Is.EqualTo(0.5), "a non-finite position counts as 0");
        Assert.That(PartMath.Slew(1.0, double.NaN, 0.5), Is.EqualTo(0.5), "a non-finite goal counts as 0");

        double position = 0;
        int frames = 0;
        while (position != 2.0 && frames < 1000) { position = PartMath.Slew(position, 2.0, 0.1); frames++; }
        Assert.That(frames, Is.EqualTo(20), "two seconds of travel in 0.1 s steps");
    }

    [Test]
    public void PartMath_Switch_Groups_Are_The_Lowest_Index_Sharing_A_Switch()
    {
        object a = new(), b = new();
        Assert.That(PartMath.SwitchGroups(new[] { a, null, b, a, b, null }), Is.EqualTo(new[] { 0, 1, 2, 0, 2, 5 }));
        Assert.That(PartMath.SwitchGroups(Array.Empty<object>()), Is.Empty);
    }
}
