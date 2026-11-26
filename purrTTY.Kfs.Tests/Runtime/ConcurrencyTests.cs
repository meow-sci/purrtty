using KSA.Kfs.Host;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// The three places where threads meet (plan §4.7, §4.8): the registry (pump, console and dispatch
/// threads binding at once), a binding's queue (dispatch producing while the pump drains) and a
/// back end's lists (a rebuild while the dispatch thread reads). Deterministic in what they assert:
/// threads start together on a <see cref="Barrier"/>, are joined, run a fixed number of iterations,
/// and never sleep; the interleaving varies, the invariants may not.
/// </summary>
[TestFixture]
public sealed class ConcurrencyTests : RuntimeFixture
{
    private const int Threads = 8;

    [Test]
    public void Concurrent_Binds_Create_One_Binding_Per_Computer()
    {
        const int computers = 6, rounds = 40;
        VehicleComputer[] cs = Computers(computers, "bind");
        Action[] originals = cs.Select(c => (Action)(() => { })).ToArray();
        for (int i = 0; i < computers; i++) cs[i].ImageLoaded = originals[i];
        try
        {
            for (int round = 0; round < rounds; round++)
            {
                var seen = new VehicleBinding?[Threads, computers];
                RunTogether(t =>
                {
                    // Every thread binds every computer, half of them in reverse order, and looks them up.
                    for (int j = 0; j < computers; j++)
                    {
                        int i = t % 2 == 0 ? j : computers - 1 - j;
                        VehicleBinding b = PartsRegistry.Bind(cs[i], new SimVehicleParts(lights: 1));
                        if (!ReferenceEquals(PartsRegistry.Find(cs[i]), b)) throw new InvalidOperationException("Find disagrees with Bind");
                        seen[t, i] = b;
                    }
                });
                for (int i = 0; i < computers; i++)
                {
                    VehicleBinding first = seen[0, i]!;
                    for (int t = 1; t < Threads; t++) Assert.That(seen[t, i], Is.SameAs(first), $"round {round}: one binding for computer {i}");
                    Assert.That(PartsRegistry.Live.Count(b => ReferenceEquals(b.Computer, cs[i])), Is.EqualTo(1));
                    Assert.That(ReferenceEquals(cs[i].ImageLoaded, originals[i]), Is.True, "ImageLoaded untouched");
                }

                // Unbind and rebind racing: afterwards a computer is bound at most once, and a
                // binding that lookups can find is live.
                RunTogether(t =>
                {
                    for (int i = 0; i < computers; i++)
                    {
                        if ((t + i) % 3 == 0) PartsRegistry.Unbind(cs[i]);
                        else if ((t + i) % 3 == 1) PartsRegistry.Bind(cs[i], new SimVehicleParts(lights: 1));
                        else PartsRegistry.Find(cs[i]);
                    }
                });
                for (int i = 0; i < computers; i++)
                {
                    var live = PartsRegistry.Live.Where(b => ReferenceEquals(b.Computer, cs[i])).ToList();
                    Assert.That(live, Has.Count.LessThanOrEqualTo(1), $"round {round}, computer {i}");
                    VehicleBinding? found = PartsRegistry.Find(cs[i]);
                    Assert.That(found is null ? live.Count == 0 : live.Count == 1 && ReferenceEquals(live[0], found) && found.IsBound, Is.True,
                        "the registry's lookup and live list agree");
                    PartsRegistry.Unbind(cs[i]);
                }
            }
        }
        finally
        {
            foreach (VehicleComputer c in cs) PartsRegistry.Unbind(c);
        }
        for (int i = 0; i < computers; i++)
            Assert.That(cs[i].ImageLoaded!.GetInvocationList(), Has.Length.EqualTo(1));
    }

    [Test]
    public void Queue_Loses_And_Duplicates_Nothing_Under_Concurrent_Producers()
    {
        // Several producers (more than the game's one dispatch thread per computer) and one
        // drainer. Each command carries (producer, sequence); afterwards every command was either
        // drained exactly once or counted as dropped, and each producer's survivors kept their order.
        const int producers = 4, perProducer = 20_000;
        using SimVehicle v = Vehicle(lights: 1, name: "queue");
        VehicleBinding binding = v.Binding;
        PartSlot slot = v.Parts.Sets.Lights[0];
        var drained = new List<(uint Producer, uint Sequence)>(producers * perProducer);
        int done = 0;
        using var start = new Barrier(producers + 1);

        var threads = new List<Thread>();
        for (int p = 0; p < producers; p++)
        {
            uint producer = (uint)p;
            threads.Add(new Thread(() =>
            {
                start.SignalAndWait();
                for (uint s = 0; s < perProducer; s++) binding.Enqueue(new PartCommand(Svc.LightSetIntensity, slot, U: producer, A: s));
                Interlocked.Increment(ref done);
            }));
        }
        var drainer = new Thread(() =>
        {
            start.SignalAndWait();
            for (long spins = 0; ; spins++)
            {
                bool finished = Volatile.Read(ref done) == producers;   // read before taking: nothing can follow
                List<PartCommand> taken = binding.Take();
                foreach (PartCommand c in taken) drained.Add((c.U, (uint)c.A));
                binding.Recycle(taken);
                if (finished && binding.PendingCount == 0) break;
                if (spins > 100_000_000) throw new TimeoutException("drainer did not finish");
                if (taken.Count == 0) Thread.Yield();
            }
        });
        threads.Add(drainer);
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        long produced = (long)producers * perProducer;
        Assert.That(drained.Count + binding.Dropped, Is.EqualTo(produced), "every command drained or counted as dropped");
        Assert.That(drained.Distinct().Count(), Is.EqualTo(drained.Count), "nothing drained twice");
        for (uint p = 0; p < producers; p++)
        {
            uint[] sequence = drained.Where(d => d.Producer == p).Select(d => d.Sequence).ToArray();
            Assert.That(sequence, Is.Ordered.Ascending, $"producer {p}: order kept");
            Assert.That(sequence, Is.All.LessThan((uint)perProducer));
        }
        Assert.That(binding.Accepted, Is.EqualTo(produced));
        Assert.That(binding.PendingCount, Is.Zero);
    }

    [Test]
    public void Queue_Overflow_While_Draining_Drops_Only_The_Oldest()
    {
        // The game's shape: one producer, one drainer. The producer overfills the queue before the
        // drainer starts (so the drop path certainly runs), then both race. Whatever is dropped is
        // the oldest waiting, so the survivors are in order, the newest always survives, and the
        // first `early` commands — dropped while nothing drained — are gone.
        const int total = 60_000, early = 100;
        using SimVehicle v = Vehicle(lights: 1, name: "overflow");
        VehicleBinding binding = v.Binding;
        PartSlot slot = v.Parts.Sets.Lights[0];
        var drained = new List<int>(total);
        int done = 0;
        using var start = new Barrier(2);
        var producer = new Thread(() =>
        {
            for (int s = 0; s < VehicleBinding.QueueLimit + early; s++) binding.Enqueue(new PartCommand(Svc.LightSetIntensity, slot, A: s));
            start.SignalAndWait();
            for (int s = VehicleBinding.QueueLimit + early; s < total; s++) binding.Enqueue(new PartCommand(Svc.LightSetIntensity, slot, A: s));
            Volatile.Write(ref done, 1);
        });
        var drainer = new Thread(() =>
        {
            start.SignalAndWait();
            for (long spins = 0; ; spins++)
            {
                bool finished = Volatile.Read(ref done) == 1;
                List<PartCommand> taken = binding.Take();
                foreach (PartCommand c in taken) drained.Add((int)c.A);
                binding.Recycle(taken);
                if (finished && binding.PendingCount == 0) break;
                if (spins > 100_000_000) throw new TimeoutException("drainer did not finish");
                // No yield: let the queue overflow now and then.
            }
        });
        producer.Start();
        drainer.Start();
        producer.Join();
        drainer.Join();

        Assert.That(drained.Count + binding.Dropped, Is.EqualTo(total));
        Assert.That(binding.Dropped, Is.GreaterThanOrEqualTo(early));
        Assert.That(drained, Is.Ordered.Ascending.And.Unique);
        Assert.That(drained[0], Is.GreaterThanOrEqualTo(early), "the oldest were dropped, not newer ones");
        Assert.That(drained[^1], Is.EqualTo(total - 1), "the newest command is never dropped");
    }

    [Test]
    public void Rebuild_Never_Exposes_A_Partial_List()
    {
        // Writers rebuild while readers read: every list a reader sees is one whole generation
        // (each slot of every list carries the generation that built it) and generations only grow.
        var parts = new GenerationTaggedParts(lights: 7, deployables: 5, actuators: 3);
        Assert.That(parts.Sets.Generation, Is.EqualTo(1), "the first build, before the race");
        const int writers = 3, rebuildsEach = 400;   // the other threads read
        int writing = writers;
        long reads = 0;
        RunTogether(t =>
        {
            if (t < writers)
            {
                for (int i = 0; i < rebuildsEach; i++) parts.Rebuild();
                Interlocked.Decrement(ref writing);
                return;
            }
            int last = 0;
            for (long spins = 0; Volatile.Read(ref writing) > 0 || spins < 1000; spins++)
            {
                PartSets sets = parts.Sets;
                int g = sets.Generation;
                if (g < last) throw new InvalidOperationException($"generation went back from {last} to {g}");
                last = g;
                if (sets.Lights.Length != 7 || sets.Deployables.Length != 5 || sets.Actuators.Length != 3)
                    throw new InvalidOperationException($"generation {g}: partial lists");
                foreach (PartSlot s in sets.Lights) Check(s, g);
                foreach (PartSlot s in sets.Deployables) Check(s, g);
                foreach (PartSlot s in sets.Actuators) Check(s, g);
                Interlocked.Increment(ref reads);
                if (spins > 50_000_000) throw new TimeoutException("writers did not finish");
            }
        });
        Assert.That(parts.Sets.Generation, Is.EqualTo(1 + writers * rebuildsEach), "every rebuild published exactly once");
        Assert.That(reads, Is.GreaterThan(0));

        static void Check(PartSlot slot, int generation)
        {
            if (slot.PartId != (uint)generation) throw new InvalidOperationException($"slot {slot} of generation {slot.PartId} in generation {generation}");
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Runs <paramref name="body"/>(thread index) on <see cref="Threads"/> threads released together; rethrows the first failure.</summary>
    private static void RunTogether(Action<int> body)
    {
        using var barrier = new Barrier(Threads);
        var failures = new Exception?[Threads];
        var threads = Enumerable.Range(0, Threads).Select(t => new Thread(() =>
        {
            barrier.SignalAndWait();
            try { body(t); }
            catch (Exception e) { failures[t] = e; }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        if (failures.FirstOrDefault(f => f is not null) is { } failure) throw new AssertionException("a thread failed: " + failure);
    }

    private static VehicleComputer[] Computers(int count, string name) =>
        Enumerable.Range(0, count).Select(i => new FakeVehicle(Runtime.Tables, 1, $"{name}{i}").Computer).ToArray();

    /// <summary>A back end whose every slot records the generation that built it (as its part id).</summary>
    private sealed class GenerationTaggedParts(int lights, int deployables, int actuators) : VehiclePartsBase
    {
        private int _builds;

        public override bool Dirty => false;

        protected override Collected Collect()
        {
            uint g = (uint)++_builds;   // called under the rebuild lock, once per published generation
            return new Collected(
                Enumerable.Range(0, lights).Select(i => new LightSlot(i, g, "light", "", i)).ToArray(),
                Enumerable.Range(0, deployables).Select(i => new AnimSlot(PartKind.Deployable, i, g, "deployable", "", 0)).ToArray(),
                Enumerable.Range(0, actuators).Select(i => new AnimSlot(PartKind.Actuator, i, g, "actuator", "", 0)).ToArray());
        }

        public override bool IsPresent(PartSlot slot) => true;
        public override bool HasSwitch(LightSlot slot) => false;
        protected override bool ReadSwitchOn(LightSlot slot) => false;
        protected override bool ReadLit(LightSlot slot) => false;
        protected override LightType ReadLightType(LightSlot slot) => LightType.Spot;
        protected override bool ReadOverridden(LightSlot slot) => false;
        protected override float ReadLightF(LightSlot slot, LightProp prop) => 0f;
        protected override double ReadDurationS(AnimSlot slot) => 1;
        protected override double ReadPositionS(AnimSlot slot) => 0;
        protected override double ReadGoalS(AnimSlot slot) => 0;
        protected override void WriteSwitchOn(LightSlot slot, bool on) { }
        protected override void WriteAllSwitches(bool on) { }
        protected override void WriteColor(LightSlot slot, float r, float g, float b) { }
        protected override void WriteCone(LightSlot slot, float innerRad, float outerRad) { }
        protected override void WriteIntensity(LightSlot slot, float intensity) { }
        protected override void WriteRange(LightSlot slot, float rangeM) { }
        protected override void WriteReset(LightSlot slot) { }
        protected override void WriteGoalS(AnimSlot slot, double goalS) { }
    }
}
