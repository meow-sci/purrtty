using KSA.Kfs.Host;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Game;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// The game's per-frame pump (<see cref="MainThreadPump"/>, plan §4.12) and its dispatch-thread
/// fallback, driven through the host abstraction with simulated hosts (<see cref="SimHostSource"/>:
/// game <c>FakeVehicle</c> computers, simulated parts) — the bind / drain-order / prune logic the
/// game runs, without game vehicles. The real registry of the shared runtime is used, so every test
/// unbinds what it bound.
/// </summary>
[TestFixture]
public sealed class MainThreadPumpTests : RuntimeFixture
{
    private SimHostSource _source = null!;
    private MainThreadPump _pump = null!;

    [SetUp]
    public void NewPump()
    {
        _source = new SimHostSource(Runtime);
        _pump = new MainThreadPump(_source);
    }

    [TearDown]
    public void UnbindEverything()
    {
        PartsRegistry.Fallback = null;
        foreach (VehicleBinding b in PartsRegistry.Live.Where(b => _source.Owns(b)).ToList()) PartsRegistry.Unbind(b.Computer);
    }

    [Test]
    public void Binds_New_Hosts_And_Drains_In_Key_Order()
    {
        var journal = new List<string>();
        _source.Factory = key => new FaultyVehicleParts(new SimVehicleParts(lights: 1)) { OnApply = _ => journal.Add(key) };
        VehicleComputer charlie = _source.Add("charlie"), alpha = _source.Add("alpha"), bravo = _source.Add("bravo");

        _pump.RunFrame();

        Assert.That(_pump.Bound, Is.EqualTo(3));
        Assert.That(_pump.Drained, Is.EqualTo(3));
        VehicleBinding[] bindings = [Bound(alpha), Bound(bravo), Bound(charlie)];
        Assert.That(bindings.Select(b => b.IsBound), Is.All.True);

        // One command each, queued in reverse key order: applied in key (Vehicle.Id) order.
        for (int i = bindings.Length - 1; i >= 0; i--) EnqueueIntensity(bindings[i], 2f);
        _pump.RunFrame();

        Assert.That(journal, Is.EqualTo(new[] { "alpha", "bravo", "charlie" }));
        Assert.That(_pump.Bound, Is.EqualTo(3), "an existing binding is reused, never rebuilt");
        Assert.That(_source.CreatedCount, Is.EqualTo(3));
        Assert.That(bindings.Select(b => ((SimVehicleParts)((FaultyVehicleParts)b.Parts).Inner).Light(0).Intensity), Is.All.EqualTo(2f));
    }

    [Test]
    public void Prunes_Bindings_Whose_Host_Disappeared_And_Discards_Their_Commands()
    {
        using SimVehicle bystander = Vehicle(lights: 1, name: "bystander");   // bound by the runtime, not by the pump
        VehicleComputer a = _source.Add("a"), b = _source.Add("b");
        _pump.RunFrame();
        VehicleBinding bindingA = Bound(a), bindingB = Bound(b);
        var partsB = (SimVehicleParts)bindingB.Parts;

        EnqueueIntensity(bindingB, 7f);
        Assert.That(_source.Remove("b"), Is.True);
        _pump.RunFrame();

        Assert.That(bindingB.IsBound, Is.False, "its host is gone");
        Assert.That(PartsRegistry.TryGet(b, out _), Is.False);
        Assert.That(PartsRegistry.Live, Does.Not.Contain(bindingB));
        Assert.That(partsB.Applied, Is.Empty, "its queued command was discarded, not applied");
        Assert.That(_pump.Pruned, Is.EqualTo(1));
        Assert.That(bindingA.IsBound, Is.True, "a live host keeps its binding");
        Assert.That(bystander.Binding.IsBound, Is.True, "a binding the source did not make is not the pump's to prune");

        // The host comes back (a new host for the vehicle): a fresh binding with nothing queued.
        VehicleComputer b2 = _source.Add("b");
        _pump.RunFrame();
        VehicleBinding again = Bound(b2);
        Assert.That(again, Is.Not.SameAs(bindingB));
        Assert.That(again.PendingCount, Is.Zero);
        Assert.That(_pump.Pruned, Is.EqualTo(1));
    }

    [Test]
    public void With_No_Hosts_A_Frame_Does_Nothing()
    {
        using SimVehicle bystander = Vehicle(lights: 1, name: "bystander");
        IReadOnlyList<VehicleBinding> before = PartsRegistry.Live;

        for (int i = 0; i < 5; i++) _pump.RunFrame();

        Assert.That(_pump.Frames, Is.EqualTo(5));
        Assert.That(_source.Snapshots, Is.EqualTo(5));
        Assert.That((_pump.Bound, _pump.Drained, _pump.Pruned, _pump.Failures), Is.EqualTo((0L, 0L, 0L, 0L)));
        Assert.That(PartsRegistry.Live, Is.EqualTo(before), "nothing bound, nothing unbound");
    }

    [Test]
    public void Frames_At_Rest_Allocate_Nothing()
    {
        // No hosts, then hosts bound with nothing queued: what the game pays every frame.
        long idle = AllocatedOver(_pump, 1000);
        _source.Add("x");
        _source.Add("y");
        _pump.RunFrame();
        long bound = AllocatedOver(_pump, 1000);

        Assert.That(idle, Is.Zero, "a frame with no hosts");
        Assert.That(bound, Is.Zero, "a frame with two bound hosts and nothing queued");
    }

    [Test]
    public void Survives_A_Factory_And_An_Apply_That_Throw()
    {
        int failingBuilds = 2;
        _source.Factory = key => key switch
        {
            "a" when failingBuilds-- > 0 => throw new InvalidOperationException("injected: cannot build a's parts"),
            "b" => new FaultyVehicleParts(new SimVehicleParts(lights: 1)) { ThrowOnApply = true },
            _ => new SimVehicleParts(lights: 1),
        };
        VehicleComputer a = _source.Add("a"), b = _source.Add("b"), c = _source.Add("c");
        long logged = KfsLog.Count("pump bind");

        Assert.DoesNotThrow(_pump.RunFrame);
        Assert.That(PartsRegistry.TryGet(a, out _), Is.False, "a's factory threw: skipped this frame");
        Assert.That(_pump.Failures, Is.EqualTo(1));
        Assert.That(KfsLog.Count("pump bind"), Is.EqualTo(logged + 1));
        VehicleBinding bindingB = Bound(b), bindingC = Bound(c);

        EnqueueIntensity(bindingB, 3f);
        EnqueueIntensity(bindingC, 3f);
        Assert.DoesNotThrow(_pump.RunFrame);   // a fails a second time; b's apply throws
        Assert.That(bindingB.ApplyFailures, Is.EqualTo(1), "b's failure is counted on its binding");
        Assert.That(((SimVehicleParts)bindingC.Parts).Light(0).Intensity, Is.EqualTo(3f), "c is applied regardless");
        Assert.That(_pump.Failures, Is.EqualTo(2));

        _pump.RunFrame();
        Assert.That(PartsRegistry.TryGet(a, out VehicleBinding? bindingA), Is.True, "a binds once its factory works");
        Assert.That(bindingA!.IsBound && bindingB.IsBound && bindingC.IsBound, Is.True);
    }

    [Test]
    public void Fallback_Binds_Exactly_Once_Under_Concurrency_With_The_Pump()
    {
        const int rounds = 40, dispatchThreads = 6;
        PartsRegistry.Fallback = _pump.BindFromDispatchDelegate;
        for (int round = 0; round < rounds; round++)
        {
            VehicleComputer computer = _source.Add($"host{round:D2}");
            var found = new VehicleBinding?[dispatchThreads];
            using var start = new Barrier(dispatchThreads + 1);
            var threads = new List<Thread>();
            for (int t = 0; t < dispatchThreads; t++)
            {
                int index = t;
                threads.Add(new Thread(() =>
                {
                    start.SignalAndWait();
                    found[index] = PartsRegistry.Find(computer);   // a service call of a host the pump has not seen
                }));
            }
            threads.Add(new Thread(() =>
            {
                start.SignalAndWait();
                _pump.RunFrame();
            }));
            threads.ForEach(th => th.Start());
            threads.ForEach(th => th.Join());

            Assert.That(PartsRegistry.TryGet(computer, out VehicleBinding? binding), Is.True, $"round {round}: bound");
            Assert.That(found, Is.All.SameAs(binding), $"round {round}: every dispatch thread got the one binding");
            Assert.That(PartsRegistry.Live.Count(b => ReferenceEquals(b.Computer, computer)), Is.EqualTo(1), $"round {round}: one live binding");
            _pump.RunFrame();
            Assert.That(binding!.IsBound, Is.True, $"round {round}: a fallback-made binding of a live host is not pruned");
        }

        Assert.That(_pump.Bound + _pump.FallbackBinds, Is.EqualTo(rounds), "exactly one creator per host");
        Assert.That(_pump.Pruned, Is.Zero);

        // An unknown computer: the fallback finds no host and binds nothing.
        VehicleComputer stranger = new FakeVehicle(Runtime.Tables, 1, "stranger").Computer;
        Assert.That(PartsRegistry.Find(stranger), Is.Null);
        Assert.That(PartsRegistry.TryGet(stranger, out _), Is.False);
    }

    // ------------------------------------------------------------------ helpers

    private static VehicleBinding Bound(VehicleComputer computer) =>
        PartsRegistry.TryGet(computer, out VehicleBinding? binding) ? binding : throw new AssertionException("the computer is not bound");

    private static void EnqueueIntensity(VehicleBinding binding, float intensity) =>
        binding.Enqueue(new PartCommand(Svc.LightSetIntensity, binding.Parts.Sets.Lights[0], A: intensity));

    private static long AllocatedOver(MainThreadPump pump, int frames)
    {
        for (int i = 0; i < 20; i++) pump.RunFrame();   // warm-up: JIT, first-use statics
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++) pump.RunFrame();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
