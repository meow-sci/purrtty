using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using purrTTY.Kfs.Game;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// Per-light private templates (<see cref="LightOverrides"/>, plan §4.10) on the game's real
/// <c>LightModule</c> and <c>LightModule.TemplateData</c> types. A module is created without running
/// its constructor (which draws an instance id from <c>Universe</c>): the override code touches
/// only its <c>Template</c> field, so nothing else of it is needed.
/// </summary>
[TestFixture]
public sealed class LightOverridesTests : GameFixture
{
    [TearDown]
    public void Forget() => LightOverrides.RestoreAll();

    [Test]
    public void Private_Copy_Isolates_One_Light_Of_A_Shared_Template()
    {
        Assert.That(IsolationHolds(), Is.Null);
    }

    [Test]
    public void Private_Copy_Carries_Every_Field_And_Owns_What_Is_Written()
    {
        Assert.That(CopyDifferences(), Is.Empty);
    }

    [Test]
    public void Reset_Restores_The_Original_Only_While_The_Template_Is_Ours()
    {
        Assert.That(ResetBehaviour(), Is.Null);
    }

    [Test]
    public void Another_Mods_Template_Is_Copied_Afresh_And_Never_Overwritten_By_A_Reset()
    {
        Assert.That(ForeignTemplateBehaviour(), Is.Null);
    }

    [Test]
    public void Restore_All_Puts_Back_Every_Template_Still_Ours()
    {
        Assert.That(RestoreAllBehaviour(), Is.Null);
    }

    // ------------------------------------------------------------------ bodies (game types allowed in bodies and helper signatures)

    /// <summary>Null when every check passed, else what failed.</summary>
    private static string? IsolationHolds()
    {
        KSA.LightModule.TemplateData shared = NewTemplate(intensity: 2f);
        KSA.LightModule a = NewModule(shared), b = NewModule(shared);

        KSA.LightModule.TemplateData mine = LightOverrides.EnsurePrivate(a);
        if (ReferenceEquals(mine, shared) || !ReferenceEquals(a.Template, mine)) return "a did not get its own template";
        if (!ReferenceEquals(b.Template, shared)) return "b's template changed";
        if (!ReferenceEquals(LightOverrides.EnsurePrivate(a), mine)) return "a second call made another copy";

        mine.Intensity.Value = 9f;
        mine.Range.Value = 77f;
        mine.InnerAngle.Value = 0.1f;
        mine.OuterAngle.Value = 0.2f;
        mine.ColorRgb.R = 0.5f;
        if (shared.Intensity.Value != 2f || shared.Range.Value != 10f || shared.InnerAngle.Value != 0.3f
            || shared.OuterAngle.Value != 0.6f || shared.ColorRgb.R != 1f)
            return "a write to the copy reached the shared template";

        if (!LightOverrides.IsOverridden(a) || LightOverrides.IsOverridden(b)) return "IsOverridden is wrong";
        if (!ReferenceEquals(LightOverrides.StockTemplate(a), shared) || !ReferenceEquals(LightOverrides.StockTemplate(b), shared))
            return "StockTemplate is not the shared original";
        if (LightOverrides.Count != 1) return $"{LightOverrides.Count} overrides recorded, expected 1";
        return null;
    }

    /// <summary>Every instance field of the copy against the original: equal, except the five written objects, which are distinct with equal values.</summary>
    private static List<string> CopyDifferences()
    {
        KSA.LightModule.TemplateData original = NewTemplate(intensity: 3f);
        original.Type = KSA.LightModule.TemplateData.LightType.Point;
        original.RayTracing = true;
        original.DisableInIva = true;
        original.Key = "Emitter2";
        KSA.LightModule m = NewModule(original);
        KSA.LightModule.TemplateData copy = LightOverrides.EnsurePrivate(m);

        var differences = new List<string>();
        if (copy.GetType() != original.GetType()) differences.Add("runtime type");
        string[] owned = ["Range", "Intensity", "InnerAngle", "OuterAngle", "ColorRgb"];
        for (Type? t = original.GetType(); t is not null && t != typeof(object); t = t.BaseType)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object? o = f.GetValue(original), c = f.GetValue(copy);
                if (owned.Contains(f.Name))
                {
                    if (ReferenceEquals(o, c)) differences.Add($"{f.Name} is shared, not owned");
                }
                else if (!Equals(o, c))
                {
                    differences.Add($"{t.Name}.{f.Name}: {o} != {c}");
                }
            }
        }
        if (copy.Range.Value != original.Range.Value || copy.Intensity.Value != original.Intensity.Value
            || copy.InnerAngle.Value != original.InnerAngle.Value || copy.OuterAngle.Value != original.OuterAngle.Value)
            differences.Add("float values not carried");
        if (copy.ColorRgb.R != original.ColorRgb.R || copy.ColorRgb.G != original.ColorRgb.G || copy.ColorRgb.B != original.ColorRgb.B
            || copy.ColorRgb.IndexedColor != original.ColorRgb.IndexedColor || !Equals(RenderedColor(copy.ColorRgb), RenderedColor(original.ColorRgb)))
            differences.Add("colour (including the rendered value) not carried");
        if (!ReferenceEquals(copy.Transform, original.Transform)) differences.Add("Transform should stay shared (never written)");
        return differences;
    }

    private static string? ResetBehaviour()
    {
        KSA.LightModule.TemplateData shared = NewTemplate(intensity: 1f);
        KSA.LightModule m = NewModule(shared);
        if (LightOverrides.Reset(m)) return "reset of an untouched light reported a restore";

        LightOverrides.EnsurePrivate(m).Intensity.Value = 5f;
        if (!LightOverrides.Reset(m)) return "reset did not restore";
        if (!ReferenceEquals(m.Template, shared) || shared.Intensity.Value != 1f) return "the original is not back, unchanged";
        if (LightOverrides.IsOverridden(m) || LightOverrides.Count != 0) return "the override was not forgotten";
        if (LightOverrides.Reset(m)) return "a second reset reported a restore";

        // A new write after a reset makes a new copy of the (restored) original.
        KSA.LightModule.TemplateData again = LightOverrides.EnsurePrivate(m);
        if (ReferenceEquals(again, shared) || again.Intensity.Value != 1f) return "the copy after a reset is wrong";
        return null;
    }

    private static string? ForeignTemplateBehaviour()
    {
        KSA.LightModule.TemplateData shared = NewTemplate(intensity: 1f);
        KSA.LightModule m = NewModule(shared);
        KSA.LightModule.TemplateData mine = LightOverrides.EnsurePrivate(m);

        KSA.LightModule.TemplateData foreign = NewTemplate(intensity: 4f);   // another mod's own copy
        m.Template = foreign;
        if (LightOverrides.IsOverridden(m)) return "still reported overridden after another mod replaced the template";
        if (!ReferenceEquals(LightOverrides.StockTemplate(m), foreign)) return "StockTemplate should be the template in use";
        if (LightOverrides.Reset(m) || !ReferenceEquals(m.Template, foreign)) return "a reset overwrote another mod's template";

        // The reset forgot the entry: the next write copies the template in use.
        KSA.LightModule.TemplateData copy2 = LightOverrides.EnsurePrivate(m);
        if (ReferenceEquals(copy2, foreign) || ReferenceEquals(copy2, mine)) return "no fresh copy of the foreign template";
        if (copy2.Intensity.Value != 4f) return "the copy is not of the current (foreign) template";

        // Replaced again while an entry exists: copied afresh from what is there now.
        KSA.LightModule.TemplateData foreign2 = NewTemplate(intensity: 6f);
        m.Template = foreign2;
        KSA.LightModule.TemplateData copy3 = LightOverrides.EnsurePrivate(m);
        if (ReferenceEquals(copy3, copy2) || copy3.Intensity.Value != 6f) return "a stale copy was reused";
        if (!LightOverrides.Reset(m) || !ReferenceEquals(m.Template, foreign2)) return "reset should restore the template it copied";
        return null;
    }

    private static string? RestoreAllBehaviour()
    {
        KSA.LightModule.TemplateData shared = NewTemplate(intensity: 1f);
        KSA.LightModule a = NewModule(shared), b = NewModule(shared), c = NewModule(shared);
        LightOverrides.EnsurePrivate(a);
        LightOverrides.EnsurePrivate(b);
        LightOverrides.EnsurePrivate(c);
        KSA.LightModule.TemplateData foreign = NewTemplate(intensity: 2f);
        c.Template = foreign;   // no longer ours: left alone

        int restored = LightOverrides.RestoreAll();
        if (restored != 2) return $"restored {restored}, expected 2";
        if (!ReferenceEquals(a.Template, shared) || !ReferenceEquals(b.Template, shared)) return "a template was not restored";
        if (!ReferenceEquals(c.Template, foreign)) return "another mod's template was overwritten";
        if (LightOverrides.Count != 0) return "overrides left after RestoreAll";
        return LightOverrides.RestoreAll() == 0 ? null : "RestoreAll is not idempotent";
    }

    /// <summary>The rendered <c>ColorRgbReference.Value</c>, boxed (its type lives in an assembly this project does not reference).</summary>
    private static object? RenderedColor(KSA.ColorRgbReference color) =>
        typeof(KSA.ColorRgbReference).GetProperty("Value")!.GetValue(color);

    private static KSA.LightModule.TemplateData NewTemplate(float intensity) => new()
    {
        Range = new KSA.FloatReference(10f),
        Intensity = new KSA.FloatReference(intensity),
        InnerAngle = new KSA.FloatReference(0.3f),
        OuterAngle = new KSA.FloatReference(0.6f),
        ColorRgb = NewColor(),
    };

    private static KSA.ColorRgbReference NewColor()
    {
        var color = new KSA.ColorRgbReference { R = 1f, G = 0.5f, B = 0.25f };
        color.OnDataLoad(null!);
        return color;
    }

    private static KSA.LightModule NewModule(KSA.LightModule.TemplateData template)
    {
        var module = (KSA.LightModule)RuntimeHelpers.GetUninitializedObject(typeof(KSA.LightModule));
        module.Template = template;
        return module;
    }
}
