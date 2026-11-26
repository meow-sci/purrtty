using HarmonyLib;
using KSA.Kfs.Host;
using KSA.Kfs.Vm;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Patches;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// What must never happen on the dispatch thread: an exception reaching the computer, an
/// allocation per call, an unbounded queue — plus the registry's fallback and the hook targets.
/// </summary>
[TestFixture]
public sealed class RobustnessTests : RuntimeFixture
{
    /// <summary>
    /// Each member of the back end throwing on its own, every tick, while the program calls every
    /// kind of service: the router's answer and accept catches, the pump's apply catch and the
    /// run-start rebuild catch take it, and the computer keeps running.
    /// </summary>
    [TestCase("Read", "answer")]
    [TestCase("Sets", "answer", "accept")]
    [TestCase("Dirty", "answer", "accept")]
    [TestCase("IsPresent", "accept")]
    [TestCase("HasSwitch", "accept")]
    [TestCase("Apply", "apply")]
    [TestCase("Rebuild", "rebuild", "apply")]
    public void Faulty_Back_End_Never_Faults_The_Computer(string member, params string[] keys)
    {
        var before = keys.ToDictionary(k => k, KfsLog.Count);
        using SimVehicle v = Runtime.CreateVehicle(SimVehicleSpec.With(lights: 2), parts => Faulty(parts, member));
        v.LoadKc(Kc.Ticked("""
                got = got + x_light_geti(0u, X_LIGHT_ON) + (uint32_t)x_light_getf(0u, X_LIGHT_INTENSITY)
                          + x_parts_info(X_PARTS_DIRTY) + x_light_count();
                x_light_set_on(0u, 1u);
                x_light_set_intensity(1u, 3.0f);
                if (k == 2u) { x_parts_rescan(); }
                if (k == 3u) { x_parts_describe(X_KIND_LIGHT); }
                if (k == 6u) { kfs_logf("alive %u", k); }
            """, "uint32_t got;\n"));
        v.Run(1.0, 0.1);

        Assert.That(v.Computer.State, Is.EqualTo(ComputerState.Running), v.Dump);
        Assert.That(v.Computer.HostFault, Is.Null);
        Assert.That(v.Trap, Is.Null);
        Assert.That(v.LogText, Has.Member("alive 6"), "the program keeps ticking\n" + v.Dump());
        foreach (string key in keys)
            Assert.That(KfsLog.Count(key), Is.GreaterThan(before[key]), $"the '{key}' catch took the fault of {member}");
        Assert.That(v.Binding.RouterFaults + v.Binding.ApplyFailures, Is.GreaterThan(0));
    }

    private static IVehicleParts Faulty(SimVehicleParts parts, string member) => new FaultyVehicleParts(parts)
    {
        ThrowOnRead = member == "Read",
        ThrowOnSets = member == "Sets",
        ThrowOnDirty = member == "Dirty",
        ThrowOnIsPresent = member == "IsPresent",
        ThrowOnHasSwitch = member == "HasSwitch",
        ThrowOnApply = member == "Apply",
        ThrowOnRebuild = member == "Rebuild",
    };

    /// <summary>Review C5: a back end failing while a setter is accepted gets the setter rejected and counted, not dropped.</summary>
    [TestCase("Sets")]
    [TestCase("IsPresent")]
    [TestCase("HasSwitch")]
    public void Router_Fault_Rejects_The_Request(string member)
    {
        using SimVehicle v = Runtime.CreateVehicle(SimVehicleSpec.With(lights: 1), parts => new FaultyVehicleParts(parts));
        var faulty = (FaultyVehicleParts)v.BoundParts;
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) { r0 = sys.rejected_requests; x_light_set_on(0u, 1u); }
                if (k == 2u) { kfs_logf("rej %u", sys.rejected_requests - r0); }
            """, "uint32_t r0;\n"));
        v.Frame(0.1);                       // tick 0: the run's lists are built
        long faults = v.Binding.RouterFaults, accept = KfsLog.Count("accept");
        faulty.ThrowOnSets = member == "Sets";
        faulty.ThrowOnIsPresent = member == "IsPresent";
        faulty.ThrowOnHasSwitch = member == "HasSwitch";
        v.Run(0.3, 0.1);

        Assert.That(v.LogText, Has.Member("rejected x_light_set_on: " + PartsText.InternalError), v.Dump);
        Assert.That(v.LogText, Has.Member("rej 1"), "sys.rejected_requests moved\n" + v.Dump());
        Assert.That((v.Binding.RouterFaults - faults, v.Binding.Rejected), Is.EqualTo((1L, 1L)));
        Assert.That(KfsLog.Count("accept"), Is.EqualTo(accept + 1));
        Assert.That(v.Binding.PendingCount + v.Parts.Applied.Count, Is.Zero, "nothing queued");
        Assert.That(v.Computer.State, Is.EqualTo(ComputerState.Running));
    }

    /// <summary>
    /// The hooks' own catches (<c>accept hook</c>, <c>answer hook</c>), which only an exception
    /// escaping the router reaches — forced here through the router's entry seam: the getter reads
    /// 0, the setter is rejected and counted, and the computer keeps running.
    /// </summary>
    [Test]
    public void Hook_Catches_Keep_The_Computer_Running()
    {
        long acceptHook = KfsLog.Count("accept hook"), answerHook = KfsLog.Count("answer hook");
        using SimVehicle v = Vehicle(lights: 2);
        v.LoadKc(Kc.Ticked("""
                if (k == 1u) { r0 = sys.rejected_requests; kfs_logf("count %u", x_light_count()); x_light_set_on(0u, 1u); }
                if (k == 2u) { kfs_logf("rej %u", sys.rejected_requests - r0); }
                if (k == 4u) { kfs_logf("alive count %u", x_light_count()); }
            """, "uint32_t r0;\n"));
        v.Frame(0.1);
        ServiceRouter.EntryFault = id =>
        {
            if (id is Svc.LightCount or Svc.LightSetOn) throw new InvalidOperationException("router bug");
        };
        try
        {
            v.Frame(0.1);                   // tick 1, with the router failing
        }
        finally
        {
            ServiceRouter.EntryFault = null;
        }
        v.Run(0.4, 0.1);

        Assert.That(KfsLog.Count("answer hook"), Is.GreaterThan(answerHook));
        Assert.That(KfsLog.Count("accept hook"), Is.GreaterThan(acceptHook));
        Assert.That(v.LogText, Has.Member("count 0"), v.Dump);
        Assert.That(v.LogText, Has.Member("rejected x_light_set_on: " + PartsText.InternalError), v.Dump);
        Assert.That(v.LogText, Has.Member("rej 2"), "the setter and the getter (both claimed by the failing accept hook)\n" + v.Dump());
        Assert.That(v.LogText, Has.Member("alive count 2"), v.Dump);
        Assert.That(v.Computer.State, Is.EqualTo(ComputerState.Running));
        Assert.That(v.Parts.Light(0).On, Is.False);
    }

    /// <summary>
    /// An id inside purrTTY's block that the catalogue does not define. No image can carry such a
    /// call — the game's image reader refuses a <c>SYSCALL</c> whose id is not in the tables
    /// ("unknown service id"), and the augmented tables hold exactly the catalogue's rows — so the
    /// router can only meet one through a direct call (or tables of another catalogue, which the
    /// installer refuses). It answers 0 and rejects the request.
    /// </summary>
    [Test]
    public void Router_Rejects_An_Id_The_Catalogue_Does_Not_Define()
    {
        const ushort undefined = 0x50FE;
        Assert.That(XCatalog.Catalog.ById(undefined), Is.Null);
        Assert.That(Runtime.Tables.Services.ContainsKey(undefined), Is.False);
        using SimVehicle v = Vehicle(lights: 1);

        // The game refuses an image that calls it.
        BuildResult build = Runtime.BuildKasm("probe.kasm", """
            .include <kfs.kinc>
            .tick_hz 10
            .task main rate_hz=10
            .func main
                SYSCALL 0x50FE
                RET   0
            .endfunc

            """, v.Layout);
        // The assembler refuses it first ("SYSCALL 20734 is not a service in the table"); an image
        // built elsewhere would be refused by ImageReader ("unknown service id 0x50FE").
        string refusal = build.Ok ? LoadRefusal(v, build.Image!) : build.Text;
        Assert.That(refusal, Does.Contain("SYSCALL 20734 is not a service in the table").Or.Contain("unknown service id 0x50FE"), refusal);

        v.LoadKc(Kc.Ticked("")).Run(0.1, 0.1);
        Assert.That(ServiceRouter.Answer(v.Computer, undefined, [0, 0]), Is.Zero);
        ServiceRouter.Accept(v.Computer, new ServiceRequest(undefined, [1], null));
        bool claimed = false;
        ApplyRequestPatch.Postfix(v.Computer, new ServiceRequest(undefined, [], null), ref claimed);

        Assert.That(claimed, Is.True, "every id in the block is claimed");
        Assert.That(v.LogText.Count(t => t == "rejected 0x50FE: " + PartsText.UnknownService), Is.EqualTo(2), v.Dump);
        Assert.That(v.Binding.Rejected, Is.EqualTo(2));
        Assert.That(v.RejectedRequests, Is.EqualTo(2u));
    }

    private static string LoadRefusal(SimVehicle v, byte[] image)
    {
        var load = v.Computer.Load(image, v.Layout);
        return load.Ok ? "" : load.ToString();
    }

    /// <summary>A computer nothing bound (and no fallback finds): getters read 0, every request is rejected.</summary>
    [Test]
    public void Router_Without_A_Binding()
    {
        var fake = new FakeVehicle(Runtime.Tables, 1, "unbound");
        BuildResult build = Runtime.BuildKc(Kc.Ticked("""
                if (k == 0u) { kfs_logf("n %u", x_light_count()); x_light_set_on(0u, 1u); }
                if (k == 1u) { kfs_logf("rej %u", sys.rejected_requests); }
            """), fake.Layout);
        Assert.That(build.Ok, Is.True, build.Text);
        Assert.That(PartsRegistry.Fallback, Is.Null);
        Assert.That(fake.Computer.Load(build.Image!, fake.Layout).Ok, Is.True);
        fake.Computer.Start(0L);
        fake.Dispatch(0L, default(KSA.Kfs.Host.TruthSample));
        fake.Dispatch(100_000_000L, default(KSA.Kfs.Host.TruthSample));

        var log = fake.Computer.Console.Snapshot().Select(l => l.Text).ToList();
        string dump = string.Join("\n", log);
        Assert.That(log, Has.Member("n 0"), dump);
        Assert.That(log, Has.Member("rejected x_light_set_on: " + PartsText.NoBinding), dump);
        Assert.That(log, Has.Member("rejected x_light_count: " + PartsText.NoBinding), dump);
        Assert.That(log, Has.Member("rej 2"), dump);
        Assert.That(PartsRegistry.Find(fake.Computer), Is.Null);
        Assert.That(fake.Computer.State, Is.EqualTo(ComputerState.Running));
    }

    [Test]
    public void Router_Does_Not_Allocate()
    {
        using SimVehicle v = Vehicle(lights: 2, deployables: 1, actuators: 1);
        VehicleComputer computer = v.Computer;
        for (int round = 0; round < 3; round++)
        {
            // Warm-up: JIT, and both queue lists grown to the working size by the swap.
            RouterCalls(computer, out int warmUpCalls);
            Assert.That(warmUpCalls, Is.GreaterThan(0));
            BindingPump.DrainAndApply(v.Binding);
        }

        long allocated = RouterCalls(computer, out int calls);
        BindingPump.DrainAndApply(v.Binding);

        Assert.That(allocated, Is.Zero, $"ServiceRouter.Answer and the accept path of an ordinary setter allocate nothing per call ({calls} calls)");
        Assert.That(v.Binding.Rejected, Is.Zero);
        Assert.That(v.Binding.Accepted, Is.EqualTo(4 * 500 * 5));
    }

    /// <summary>
    /// A whole tick through the game's VM, the host and both Harmony hooks: the bytes a dispatch
    /// allocates per call of a service, against the same program without the calls. The VM itself
    /// allocates a request per <c>SYSCALL</c>, so a stock request (<c>kfs_set_throttle</c>) is the
    /// baseline; what the hooks and the router add on top of it is what this measures.
    /// </summary>
    [Test]
    public void Tick_Allocation_Through_The_Hooks()
    {
        const int calls = 100;
        long empty = BytesPerTick("");
        long getter = BytesPerTick("sink = sink + x_light_geti(0u, X_LIGHT_ON);");
        long noArgGetter = BytesPerTick("sink = sink + x_light_count();");
        long setter = BytesPerTick("x_light_set_intensity(0u, 1.0f);");
        long stock = BytesPerTick("kfs_set_throttle(0.5f);");

        double PerCall(long bytes) => (bytes - empty) / (double)calls;
        string report = System.FormattableString.Invariant(
            $"bytes per call: x_light_geti {PerCall(getter):0.#}, x_light_count {PerCall(noArgGetter):0.#}, x_light_set_intensity {PerCall(setter):0.#}, kfs_set_throttle {PerCall(stock):0.#} (empty tick {empty} bytes)");

        // Measured on 2026-11-25 (macOS arm64, Debug and Release alike): x_light_geti 152,
        // x_light_count 144, x_light_set_intensity 120, kfs_set_throttle 120 bytes per call. A
        // request costs the same whether the stock host or purrTTY's hook and router take it; a
        // getter costs one more small array, the result cell the VM allocates for every
        // result-bearing SYSCALL (Answer and Accept themselves allocate nothing:
        // Router_Does_Not_Allocate). Asserted with margins, since the VM's own allocation may change.
        Assert.That(Math.Abs(PerCall(setter) - PerCall(stock)), Is.LessThan(8), report);
        Assert.That(PerCall(getter) - PerCall(stock), Is.LessThanOrEqualTo(40), report);
        Assert.That(PerCall(getter) - PerCall(noArgGetter), Is.LessThanOrEqualTo(16), report);
    }

    /// <summary>Steady-state bytes one dispatch of one tick allocates for a program running <paramref name="statement"/> 100 times a tick.</summary>
    private static long BytesPerTick(string statement)
    {
        string body = statement.Length == 0 ? "" : "for (uint32_t i = 0u; i < 100u; i++) { " + statement + " }";
        using SimVehicle v = Vehicle(lights: 1);
        v.LoadKc(Kc.Ticked(body, "uint32_t sink;\n"));
        v.Run(3.0, 0.1);                                   // warm-up: JIT, lists, queue capacity
        long total = 0;
        const int frames = 40;
        for (int i = 0; i < frames; i++)
        {
            v.Frame(0.1);
            Assert.That(v.LastTicksRun, Is.EqualTo(1));
            total += v.LastDispatchAllocatedBytes;
        }
        Assert.That(v.Overruns, Is.Zero, v.Dump);
        Assert.That(v.LogText, Has.None.Contains("rejected").And.None.Contains("dropped"), v.Dump);
        return total / frames;
    }

    /// <summary>
    /// Calls <see cref="ServiceRouter.Answer"/> for every getter and <see cref="ServiceRouter.Accept"/>
    /// for ordinary setters 500 times each and returns the bytes this thread allocated during the
    /// calls (requests and argument arrays are made before the measurement). Fully optimised from
    /// the start, so no tiering or on-stack replacement happens inside the measured loop.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveOptimization)]
    private static long RouterCalls(VehicleComputer computer, out int calls)
    {
        uint[] lightOn = [0, (uint)LightProp.On];
        uint[] lightIntensity = [0, (uint)LightProp.Intensity];
        uint[] deployState = [0, (uint)DeployProp.State];
        uint[] deployPosition = [0, (uint)DeployProp.Position];
        uint[] actuatorMoving = [0, (uint)ActuatorProp.Moving];
        uint[] actuatorTarget = [0, (uint)ActuatorProp.Target];
        uint[] info = [(uint)PartsInfo.Dirty];
        uint[] none = [];
        ServiceRequest[] setters =
        [
            new(Svc.LightSetOn, [0, 1], null),
            new(Svc.LightSetIntensity, [1, BitConverter.SingleToUInt32Bits(3f)], null),
            new(Svc.LightSetColor, [0, BitConverter.SingleToUInt32Bits(0.5f), 0, BitConverter.SingleToUInt32Bits(1f)], null),
            new(Svc.DeploySet, [0, 1], null),
            new(Svc.ActuatorSet, [0, BitConverter.SingleToUInt32Bits(0.25f)], null),
            new(Svc.LightGeti, [0, (uint)LightProp.On], null, Results: [0]),   // a getter arriving after its answer
        ];

        calls = 0;
        uint sink = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++)
        {
            sink += ServiceRouter.Answer(computer, Svc.PartsInfo, info);
            sink += ServiceRouter.Answer(computer, Svc.LightCount, none);
            sink += ServiceRouter.Answer(computer, Svc.LightGeti, lightOn);
            sink += ServiceRouter.Answer(computer, Svc.LightGetf, lightIntensity);
            sink += ServiceRouter.Answer(computer, Svc.DeployGeti, deployState);
            sink += ServiceRouter.Answer(computer, Svc.DeployGetf, deployPosition);
            sink += ServiceRouter.Answer(computer, Svc.ActuatorGeti, actuatorMoving);
            sink += ServiceRouter.Answer(computer, Svc.ActuatorGetf, actuatorTarget);
            for (int s = 0; s < setters.Length; s++) ServiceRouter.Accept(computer, setters[s]);
            calls += 8 + setters.Length;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        return allocated;
    }

    [Test]
    public void Queue_Drops_Oldest_Above_The_Limit()
    {
        using SimVehicle v = Vehicle(lights: 1);
        PartSlot slot = v.Parts.Sets.Lights[0];
        for (int i = 0; i < VehicleBinding.QueueLimit + 4; i++)
            v.Binding.Enqueue(new PartCommand(Svc.LightSetIntensity, slot, A: i));

        Assert.That((v.Binding.PendingCount, v.Binding.Dropped), Is.EqualTo((VehicleBinding.QueueLimit, 4L)));
        Assert.That(v.LogText.Count(t => t.StartsWith("x_ commands: queue full", StringComparison.Ordinal)), Is.EqualTo(1), "one event per overflow");

        Assert.That(BindingPump.DrainAndApply(v.Binding), Is.EqualTo(VehicleBinding.QueueLimit));
        Assert.That(v.Parts.Applied[0].Command.A, Is.EqualTo(4f), "the oldest four were dropped");
        Assert.That(v.Parts.Light(0).Intensity, Is.EqualTo(VehicleBinding.QueueLimit + 3));
        Assert.That(v.Binding.PendingCount, Is.Zero);
    }

    [Test]
    public void Registry_Fallback_Binds_An_Unknown_Computer()
    {
        var fake = new FakeVehicle(Runtime.Tables, 1, "late");
        var parts = new SimVehicleParts(lights: 3);
        BuildResult build = Runtime.BuildKc(Kc.Ticked("""if (k == 0u) { kfs_logf("late %u", x_light_count()); }"""), fake.Layout);
        Assert.That(build.Ok, Is.True, build.Text);
        int asked = 0;
        PartsRegistry.Fallback = computer =>
        {
            asked++;
            return ReferenceEquals(computer, fake.Computer) ? PartsRegistry.Bind(computer, parts) : null;
        };
        try
        {
            // Loaded and ticked before anything bound it: the dispatch-thread lookup binds it.
            Assert.That(fake.Computer.Load(build.Image!, fake.Layout).Ok, Is.True);
            fake.Computer.Start(0L);
            fake.Dispatch(0L, default(KSA.Kfs.Host.TruthSample));
            var log = fake.Computer.Console.Snapshot().Select(l => l.Text).ToList();
            Assert.That(log, Has.Member("late 3"), string.Join("\n", log));
            Assert.That(asked, Is.EqualTo(1), "asked once; found in the registry from then on");
            Assert.That(PartsRegistry.Find(fake.Computer)!.Parts, Is.SameAs(parts));
        }
        finally
        {
            PartsRegistry.Fallback = null;
            PartsRegistry.Unbind(fake.Computer);
        }
    }

    [Test]
    public void Registry_Fallback_Failure_Is_Not_Found()
    {
        var fake = new FakeVehicle(Runtime.Tables, 1, "throws");
        long before = KfsLog.Count("registry fallback");
        PartsRegistry.Fallback = _ => throw new InvalidOperationException("discovery failed");
        try
        {
            Assert.That(PartsRegistry.Find(fake.Computer), Is.Null);
            Assert.That(ServiceRouter.Answer(fake.Computer, Svc.LightCount, []), Is.Zero);
            Assert.That(KfsLog.Count("registry fallback"), Is.GreaterThan(before));
        }
        finally
        {
            PartsRegistry.Fallback = null;
        }
    }

    [Test]
    public void Patch_Targets_Resolve_And_Carry_The_Hooks()
    {
        var apply = ApplyRequestPatch.TargetMethod();
        var issue = IssueResultPatch.TargetMethod();
        Assert.That(apply, Is.Not.Null);
        Assert.That(issue, Is.Not.Null);
        Assert.That(apply, Is.Not.SameAs(issue));
        Assert.That(apply!.DeclaringType, Is.EqualTo(typeof(VehicleComputer)));
        Assert.That(issue!.DeclaringType, Is.EqualTo(typeof(VehicleComputer)));
        Assert.That(issue.GetParameters().Select(p => p.ParameterType),
            Is.EqualTo(new[] { typeof(ushort), typeof(ReadOnlySpan<uint>), typeof(Span<uint>) }));

        foreach (var target in new[] { apply, issue })
        {
            var info = Harmony.GetPatchInfo(target);
            Assert.That(info?.Postfixes.Select(p => p.owner), Has.Member(Runtime.HarmonyId), $"{target.Name} carries the runtime's postfix");
        }
    }
}
