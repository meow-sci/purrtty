using System.Text;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// Every <c>…_geti</c> / <c>…_getf</c> property of every part kind, read where there is no part —
/// an index out of range, a part that has left, a back end that fails, the bench — reads the
/// documented "nothing": 0 (and <c>X_STATE_NONE</c> for <c>X_DEPLOY_STATE</c>), so that no zero
/// can be mistaken for a state of a part that is not there.
/// </summary>
[TestFixture]
public sealed class NothingValueTests : RuntimeFixture
{
    private static readonly (string Getter, string Prefix, EnumDef Props)[] Kinds =
    [
        ("x_light", "l", XCatalog.LightPropEnum),
        ("x_deploy", "d", XCatalog.DeployPropEnum),
        ("x_actuator", "a", XCatalog.ActuatorPropEnum),
    ];

    [TestCase("out-of-range")]
    [TestCase("departed")]
    [TestCase("faulted")]
    public void Every_Property_Reads_Nothing(string where)
    {
        var spec = SimVehicleSpec.With(lights: 1, deployables: 1, actuators: 1) with
        {
            Deployables = [new SimAnimationSpec { Name = "Solar", InitialFraction = 1.0, Links = AnimSlot.LinkSolarPanel }],
        };
        using SimVehicle v = Runtime.CreateVehicle(spec, parts => new FaultyVehicleParts(parts));
        uint index = where == "out-of-range" ? 99u : 0u;
        v.LoadKc(Kc.Ticked($$"""
                if (k == 1u) {
            {{Reads(index)}}
                    trap = (x_deploy_geti({{index}}u, X_DEPLOY_STATE) == X_STATE_DEPLOYED) ? 1u : 0u;
                    none = (x_deploy_geti({{index}}u, X_DEPLOY_STATE) == X_STATE_NONE) ? 1u : 0u;
                }
            """, Globals() + "uint32_t trap;\nuint32_t none;\n"));
        v.Frame(0.1);                                     // tick 0: the run's lists hold every part
        Assert.That(v.Parts.Deployable(0).State, Is.EqualTo(DeployState.Deployed), "present, the deployable reads deployed");
        switch (where)
        {
            case "departed":
                v.Parts.Detach(v.Parts.Light(0));
                v.Parts.Detach(v.Parts.Deployable(0));
                v.Parts.Detach(v.Parts.Actuator(0));
                break;
            case "faulted":
                ((FaultyVehicleParts)v.BoundParts).ThrowOnRead = true;
                break;
        }
        v.Run(0.2, 0.1);

        Assert.That(v.LogText, Has.None.Contains("rejected"), v.Dump);
        var notNothing = new List<string>();
        foreach (var (getter, prefix, props) in Kinds)
        {
            foreach (var (name, value) in props.Constants)
            {
                bool isFloat = value >= props.FirstFloat;
                string leaf = $"{prefix}{(isFloat ? 'f' : 'i')}[{(isFloat ? value - props.FirstFloat!.Value : value)}]";
                double read = isFloat ? v.ReadGlobal<float>(leaf) : v.ReadGlobal<uint>(leaf);
                if (read != 0) notNothing.Add($"{getter}_get{(isFloat ? 'f' : 'i')}({index}, {name}) = {read}");
            }
        }
        Assert.That(notNothing, Is.Empty, v.Dump);
        Assert.That((v.ReadGlobal<uint>("trap"), v.ReadGlobal<uint>("none")), Is.EqualTo((0u, 1u)), "STATE == X_STATE_DEPLOYED is false where there is no part");
    }

    [Test]
    public void Every_Property_Reads_Nothing_On_The_Bench()
    {
        string program = Kc.Ticked($$"""
                if (k == 0u) {
            {{Reads(0u)}}
                    for (uint32_t i = 0u; i < 8u; i++) { bad = bad | li[i]; if (lf[i] != 0.0f) { bad = bad | 0x100u; } }
                    for (uint32_t i = 0u; i < 6u; i++) { bad = bad | di[i]; }
                    for (uint32_t i = 0u; i < 4u; i++) { bad = bad | ai[i]; }
                    for (uint32_t i = 0u; i < 3u; i++) { if (df[i] != 0.0f || af[i] != 0.0f) { bad = bad | 0x200u; } }
                    kfs_logf("bench nothing bad=%u", bad);
                }
            """, Globals() + "uint32_t bad;\n");
        BuildResult build = Runtime.BuildKc(program, Runtime.Tables.BenchLayout);
        Assert.That(build.Ok, Is.True, build.Text);
        var bench = Runtime.CreateBench();
        Assert.That(bench.LoadBench(build.Image!).Ok, Is.True);
        bench.Start();
        Assert.That(bench.Tick(2), Is.EqualTo(2));
        var log = bench.Console.Snapshot().Select(l => l.Text).ToList();
        Assert.That(log, Has.Member("bench nothing bad=0"), string.Join("\n", log));
        Assert.That(log, Has.None.Contains("rejected"), "getters are silent on the bench (optional hook)");
    }

    /// <summary>One assignment per property: <c>li[n] = x_light_geti(index, X_LIGHT_…);</c>, <c>lf[n - 16] = x_light_getf(…)</c>, ….</summary>
    private static string Reads(uint index)
    {
        var text = new StringBuilder();
        foreach (var (getter, prefix, props) in Kinds)
        {
            foreach (var (name, value) in props.Constants)
            {
                bool isFloat = value >= props.FirstFloat;
                int slot = isFloat ? value - props.FirstFloat!.Value : value;
                text.Append($"        {prefix}{(isFloat ? 'f' : 'i')}[{slot}] = {getter}_get{(isFloat ? 'f' : 'i')}({index}u, {name});\n");
            }
        }
        return text.ToString();
    }

    private static string Globals()
    {
        var text = new StringBuilder();
        foreach (var (_, prefix, props) in Kinds)
        {
            int ints = props.RealValues.Count(v => v < props.FirstFloat);
            int floats = props.RealValues.Count(v => v >= props.FirstFloat);
            Assert.That(props.RealValues.Where(v => v < props.FirstFloat), Is.EqualTo(Enumerable.Range(0, ints)), "integer properties are contiguous from 0");
            text.Append($"uint32_t {prefix}i[{ints}];\nfloat {prefix}f[{floats}];\n");
        }
        return text.ToString();
    }
}
