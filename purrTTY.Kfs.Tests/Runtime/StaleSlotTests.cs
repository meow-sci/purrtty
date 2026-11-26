using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// Review C6: <see cref="VehiclePartsBase.Apply"/> hands raw writes (and the raw reads it needs)
/// slots of an <b>older generation</b> whenever the lists were rebuilt between accept and apply.
/// A back end must reach the part through the slot object, never through <c>slot.Index</c> into
/// its current lists; the base class discards a command whose part has left.
/// </summary>
[TestFixture]
public sealed class StaleSlotTests : RuntimeFixture
{
    [Test]
    public void Stale_Slots_Reach_Their_Own_Part_Or_Are_Discarded()
    {
        var probe = new GenerationProbeParts("A", "B", "C");
        using SimVehicle v = Runtime.CreateVehicle(SimVehicleSpec.With(name: "stale"), _ => probe);
        PartSets first = probe.Sets;
        Assert.That(first.Generation, Is.EqualTo(1));
        LightSlot a = first.Lights[0], b = first.Lights[1];

        // Accepted against generation 1, then A leaves and a rescan earlier in the same batch
        // renumbers: B becomes index 0, C index 1.
        probe.Present["A"] = false;
        v.Binding.Enqueue(new PartCommand(Svc.PartsRescan, null));
        v.Binding.Enqueue(new PartCommand(Svc.LightSetIntensity, b, A: 7f));
        v.Binding.Enqueue(new PartCommand(Svc.LightSetCone, b, A: 0.1f, B: 0.2f));
        v.Binding.Enqueue(new PartCommand(Svc.LightSetIntensity, a, A: 9f));
        BindingPump.DrainAndApply(v.Binding);

        Assert.That(probe.Sets.Generation, Is.EqualTo(2));
        Assert.That(probe.Writes, Is.EqualTo(new[]
        {
            // Generation-1 slots while generation 2 is current; through slot.Index they would have hit C.
            "intensity 7 -> B (slot generation 1, index 1; current generation 2, index 1 is C)",
            "cone 0.1 0.2 -> B (slot generation 1, index 1; current generation 2, index 1 is C)",
        }));
        Assert.That(probe.TypeReads, Is.EqualTo(new[] { "B (slot generation 1)" }), "the cone's raw read also gets the old slot");
        Assert.That((v.Binding.Applied, v.Binding.Discarded), Is.EqualTo((3L, 1L)), "A's command discarded and counted");
    }

    [Test]
    public void Apply_Refuses_A_Departed_Slot_Itself()
    {
        // The base class re-checks presence, so a caller other than the pump cannot hand a raw
        // write a part that has left.
        var probe = new GenerationProbeParts("A", "B");
        LightSlot a = probe.Sets.Lights[0];
        probe.Present["A"] = false;

        var refused = Assert.Throws<PartCommandException>(() => probe.Apply(new PartCommand(Svc.LightSetIntensity, a, A: 1f)));
        Assert.That(refused!.Message, Is.EqualTo("light 0 left the vehicle"));
        Assert.That(probe.Writes, Is.Empty);
    }

    /// <summary>
    /// A back end that keeps a per-generation table (as a game back end might cache module arrays)
    /// and records, for every raw write, which part the slot object names and which part
    /// <c>slot.Index</c> would name in the current generation.
    /// </summary>
    private sealed class GenerationProbeParts(params string[] parts) : VehiclePartsBase
    {
        private readonly string[] _parts = parts;
        private int _generation;

        public Dictionary<string, bool> Present { get; } = parts.ToDictionary(p => p, _ => true);
        public List<string> Writes { get; } = [];
        public List<string> TypeReads { get; } = [];

        private sealed class ProbeSlot(int index, string part, int generation) : LightSlot(index, 0, part, "", index)
        {
            public string Part { get; } = part;
            public int Generation { get; } = generation;
        }

        public override bool Dirty => false;

        protected override Collected Collect()
        {
            int generation = ++_generation;
            LightSlot[] lights = _parts.Where(p => Present[p]).Select((p, i) => (LightSlot)new ProbeSlot(i, p, generation)).ToArray();
            return new Collected(lights, [], []);
        }

        public override bool IsPresent(PartSlot slot) => Present[((ProbeSlot)slot).Part];
        public override bool HasSwitch(LightSlot slot) => false;

        protected override bool ReadSwitchOn(LightSlot slot) => false;
        protected override bool ReadLit(LightSlot slot) => false;
        protected override bool ReadOverridden(LightSlot slot) => false;
        protected override float ReadLightF(LightSlot slot, LightProp prop) => 0f;
        protected override double ReadDurationS(AnimSlot slot) => 0;
        protected override double ReadPositionS(AnimSlot slot) => 0;
        protected override double ReadGoalS(AnimSlot slot) => 0;

        protected override LightType ReadLightType(LightSlot slot)
        {
            var s = (ProbeSlot)slot;
            TypeReads.Add($"{s.Part} (slot generation {s.Generation})");
            return LightType.Spot;
        }

        protected override void WriteIntensity(LightSlot slot, float intensity) => Record(slot, $"intensity {intensity}");
        protected override void WriteCone(LightSlot slot, float innerRad, float outerRad) => Record(slot, $"cone {innerRad} {outerRad}");
        protected override void WriteSwitchOn(LightSlot slot, bool on) => Record(slot, "switch");
        protected override void WriteAllSwitches(bool on) { }
        protected override void WriteColor(LightSlot slot, float r, float g, float b) => Record(slot, "color");
        protected override void WriteRange(LightSlot slot, float rangeM) => Record(slot, "range");
        protected override void WriteReset(LightSlot slot) => Record(slot, "reset");
        protected override void WriteGoalS(AnimSlot slot, double goalS) { }

        private void Record(LightSlot slot, string what)
        {
            var s = (ProbeSlot)slot;
            PartSets current = Sets;
            string byIndex = s.Index < current.Lights.Length ? ((ProbeSlot)current.Lights[s.Index]).Part : "nothing";
            Writes.Add(System.FormattableString.Invariant(
                $"{what} -> {s.Part} (slot generation {s.Generation}, index {s.Index}; current generation {current.Generation}, index {s.Index} is {byIndex})"));
        }
    }
}
