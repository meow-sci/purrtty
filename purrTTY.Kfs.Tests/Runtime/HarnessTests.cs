using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// The test runtime itself (plan §5.8, task B1): scripted telemetry, program globals, machine
/// state, pause, simulated-time control and explicit misuse.
/// </summary>
[TestFixture]
public sealed class HarnessTests : RuntimeFixture
{
    // ------------------------------------------------------------------ telemetry

    [Test]
    public void Scripted_Telemetry_Reaches_Program()
    {
        using SimVehicle v = Runtime.Vehicle().LoadKc("""
            #include <kfs.h>
            #include <vehicle.h>
            #pragma kfs tick_hz 10
            #pragma kfs subscribe MSG_FLIGHT_DATA flight
            #pragma kfs task run rate_hz=10
            kfs_msg_flight_data_t flight;
            uint32_t seq;
            float alt;
            float apo;
            uint32_t atm;
            float parent_mass;
            void run(void)
            {
                seq = flight.hdr.seq;
                alt = flight.altitude_m;
                apo = flight.apoapsis_m;
                atm = flight.in_atmosphere;
                parent_mass = flight.parent_mass_kg;
                if (seq % 3u == 1u) {
                    kfs_logf("fd seq=%u alt=%.1f apo=%.1f atm=%u", seq, alt, apo, atm);
                }
            }
            """);
        // Climb at 1 km/s; apoapsis grows; the atmosphere ends at 0.5 s; a leaf-only field too.
        v.TelemetryScript = t => new SimTelemetry
        {
            Flight = { AltitudeM = 1000 * t, ApoapsisM = 2000 + 500 * t, InAtmosphere = t < 0.5 },
        }.Leaf("MSG_FLIGHT_DATA", "parent_mass_kg", 5.0e24);

        var seen = new List<(uint Seq, float Alt, float Apo, uint Atm)>();
        for (int frame = 0; frame < 10; frame++)
        {
            v.Frame(0.1);
            seen.Add((v.ReadGlobal<uint>("seq"), v.ReadGlobal<float>("alt"), v.ReadGlobal<float>("apo"), v.ReadGlobal<uint>("atm")));
        }

        // Tick k runs at k * 0.1 s and sees the sample of its dispatch; hdr.seq is k + 1.
        var expected = Enumerable.Range(0, 10).Select(k => ((uint)(k + 1), 100f * k, (float)(2000 + 50.0 * k), k < 5 ? 1u : 0u));
        Assert.That(seen, Is.EqualTo(expected), v.Dump);
        Assert.That(v.ReadGlobal<float>("parent_mass"), Is.EqualTo(5.0e24f), "a leaf-only field, scripted through Leaves");
        Assert.That(v.LogText.Where(l => l.StartsWith("fd ", StringComparison.Ordinal)), Is.EqualTo(new[]
        {
            "fd seq=1 alt=0.0 apo=2000.0 atm=1",
            "fd seq=4 alt=300.0 apo=2150.0 atm=1",
            "fd seq=7 alt=600.0 apo=2300.0 atm=0",
            "fd seq=10 alt=900.0 apo=2450.0 atm=0",
        }), v.Dump);
        Assert.That(v.SamplesTaken, Is.EqualTo(10), "one sample per dispatch that ran a tick");
        Assert.That(v.LastTelemetry!.Flight.AltitudeM, Is.EqualTo(900).Within(1e-9));

        // MSG_NAV_STATE: every field typed; a fixed Telemetry changed between frames.
        using SimVehicle nav = Runtime.Vehicle(name: "nav").LoadKc("""
            #include <kfs.h>
            #include <vehicle.h>
            #pragma kfs tick_hz 10
            #pragma kfs subscribe MSG_NAV_STATE nav
            #pragma kfs task run rate_hz=10
            kfs_msg_nav_state_t nav;
            double vx;
            float vz;
            float qw;
            uint32_t valid;
            void run(void)
            {
                vx = nav.v_cci_mps.x;
                vz = (float)nav.v_cci_mps.z;
                qw = nav.q_body_to_cci.w;
                valid = nav.valid;
            }
            """);
        nav.Frame(0.1);
        Assert.That((nav.ReadGlobal<double>("vx"), nav.ReadGlobal<float>("qw"), nav.ReadGlobal<uint>("valid")), Is.EqualTo((0.0, 1f, 1u)),
            "defaults: at rest, identity attitude, valid");
        nav.Telemetry.Nav.VelocityCciMps = new SimVec3(123.25, 0, 7800);
        nav.Frame(0.1);
        Assert.That((nav.ReadGlobal<double>("vx"), nav.ReadGlobal<float>("vz")), Is.EqualTo((123.25, 7800f)));
        Assert.That(nav.ReadGlobal<double>("nav.v_cci_mps.x"), Is.EqualTo(123.25), "the subscription buffer itself is a global");
    }

    [Test]
    public void Telemetry_Mistakes_Fail_The_Frame()
    {
        // The sample is taken only when a tick is due, so each attempt runs to the next tick.
        using SimVehicle v = Runtime.Vehicle(engines: 1).LoadKc(Kc.Ticked(""));
        v.Telemetry.Leaf("MSG_FLIGHT_DATA", "no_such_field", 1);
        var e = Assert.Throws<ArgumentException>(() => v.RunTicks(1));
        Assert.That(e!.Message, Does.Contain("MSG_FLIGHT_DATA has no leaf 'no_such_field'"));

        v.Telemetry = new SimTelemetry { Props = { Engines = [new SimEngine(), new SimEngine()] } };
        e = Assert.Throws<ArgumentException>(() => v.RunTicks(1));
        Assert.That(e!.Message, Does.Contain("2 engine(s) given; this vehicle's layout has 1"));

        v.Telemetry = new SimTelemetry().Leaf("MSG_WRENCH_CMD", "throttle", 1);
        e = Assert.Throws<ArgumentException>(() => v.RunTicks(1));
        Assert.That(e!.Message, Does.Contain("MSG_WRENCH_CMD is not published by the host"));
    }

    // ------------------------------------------------------------------ globals

    private const string GlobalsProgram = """
        #include <kfs.h>
        #include <vehicle.h>
        #pragma kfs tick_hz 10
        #pragma kfs task run rate_hz=10
        uint32_t n;
        int32_t neg = -5;
        float f = 1.5f;
        double d;
        vec3 err;
        float va[3] = { 1.0f, 2.0f, 3.0f };
        enum { RED, GREEN, BLUE } colour = GREEN;
        void run(void)
        {
            n = n + 1u;
            if (n == 3u) {
                err.x = 0.25f;
                va[2] = 7.0f;
                neg = -7;
                d = 2.5;
                colour = BLUE;
            }
        }
        """;

    [Test]
    public void ReadGlobal_Reads_Scalars()
    {
        using SimVehicle v = Runtime.Vehicle().LoadKc(GlobalsProgram);
        Assert.That((v.ReadGlobal<uint>("n"), v.ReadGlobal<int>("neg"), v.ReadGlobal<float>("f"), v.ReadGlobal<double>("d")),
            Is.EqualTo((0u, -5, 1.5f, 0.0)), "initialised data, before any tick");
        Assert.That(v.ReadGlobal<uint>("colour"), Is.EqualTo(1u), "an enum is an integer leaf");

        v.RunTicks(3);
        Assert.That((v.ReadGlobal<uint>("n"), v.ReadGlobal<int>("neg"), v.ReadGlobal<double>("d"), v.ReadGlobal<uint>("colour")),
            Is.EqualTo((3u, -7, 2.5, 2u)));
        Assert.That(v.ReadGlobal<long>("n"), Is.EqualTo(3L), "widening reads are allowed");
        Assert.That(v.ReadGlobal<double>("n"), Is.EqualTo(3.0));
        Assert.That(v.ReadGlobal<bool>("n"), Is.True);
    }

    [Test]
    public void ReadGlobal_Reads_A_Struct_Member()
    {
        using SimVehicle v = Runtime.Vehicle().LoadKc(GlobalsProgram);
        Assert.That(v.GlobalPaths, Is.SupersetOf(new[] { "err.x", "err.y", "err.z" }));
        v.RunTicks(2);
        Assert.That(v.ReadGlobal<float>("err.x"), Is.EqualTo(0f), "before the program writes it (tick 2)");
        v.RunTicks(1);
        Assert.That(v.ReadGlobal<float>("err.x"), Is.EqualTo(0.25f), "after");
        Assert.That(v.ReadGlobal<float>("err.y"), Is.EqualTo(0f));
    }

    [Test]
    public void ReadGlobal_Reads_An_Array_Element()
    {
        using SimVehicle v = Runtime.Vehicle().LoadKc(GlobalsProgram);
        Assert.That(new[] { v.ReadGlobal<float>("va[0]"), v.ReadGlobal<float>("va[1]"), v.ReadGlobal<float>("va[2]") },
            Is.EqualTo(new[] { 1f, 2f, 3f }));
        v.RunTicks(3);
        Assert.That(v.ReadGlobal<float>("va[2]"), Is.EqualTo(7f));
        Assert.That(v.TryReadGlobal("va[3]", out float _), Is.False, "past the end");
    }

    [Test]
    public void ReadGlobal_Missing_Or_Mistyped_Is_Explicit()
    {
        using SimVehicle v = Runtime.Vehicle();
        Assert.That(v.TryReadGlobal("n", out uint _), Is.False, "nothing loaded");
        v.LoadKc(GlobalsProgram);

        Assert.That(v.TryReadGlobal("nope", out uint missing), Is.False);
        Assert.That(missing, Is.Zero);
        var e = Assert.Throws<KeyNotFoundException>(() => v.ReadGlobal<uint>("nope"));
        Assert.That(e!.Message, Does.Contain("no global 'nope'").And.Contain("known: "));
        Assert.That(v.TryReadGlobal("va", out float _), Is.False, "an aggregate is not a leaf");

        var cast = Assert.Throws<InvalidCastException>(() => v.ReadGlobal<uint>("f"));
        Assert.That(cast!.Message, Does.Contain("32-bit float"));
        cast = Assert.Throws<InvalidCastException>(() => v.ReadGlobal<uint>("neg"));
        Assert.That(cast!.Message, Does.Contain("-5 does not fit in UInt32"));
    }

    // ------------------------------------------------------------------ machine state

    [Test]
    public void Machine_State_Is_Observable()
    {
        // Tick count at the program's tick rate (20 Hz) while its task runs at 10 Hz.
        using SimVehicle rate = Runtime.Vehicle(name: "rate").LoadKc("""
            #include <kfs.h>
            #pragma kfs tick_hz 20
            #pragma kfs task run rate_hz=10
            uint32_t n;
            void run(void) { n = n + 1u; }
            """);
        Assert.That((rate.State, rate.TicksRun, rate.LastTick), Is.EqualTo((SimComputerState.Running, 0L, (ulong?)null)));
        rate.Run(1.0);
        Assert.That((rate.TickHz, rate.TicksRun, rate.LastTick, rate.ReadGlobal<uint>("n")), Is.EqualTo((20, 20L, (ulong?)19, 10u)),
            "ticks at 0, 0.05 … 0.95 s in 60 frames of 1/60 s; the task every other tick");
        Assert.That((rate.Overruns, rate.RejectedRequests, rate.Trap), Is.EqualTo((0u, 0u, (SimTrap?)null)));

        // Overrun: a task that never finishes in tick 3 is abandoned; the program goes on.
        using SimVehicle spin = Runtime.Vehicle(name: "spin").LoadKc(Kc.Ticked("""
                if (k == 3u) {
                    while (spins != 0xFFFFFFFFu) { spins = spins + 1u; }
                }
                if (k == 6u) { kfs_logf("alive %u", k); }
            """, "uint32_t spins;\n"));
        spin.RunTicks(8);
        Assert.That(spin.Overruns, Is.EqualTo(1u), spin.Dump);
        Assert.That(spin.State, Is.EqualTo(SimComputerState.Running));
        Assert.That(spin.LogText, Has.Some.StartsWith("overrun: task run used the tick's whole budget"), spin.Dump);
        Assert.That(spin.LogText, Has.Member("alive 6"), spin.Dump);

        // A rejected request is counted (kfs_translate: always rejected by the host).
        using SimVehicle reject = Runtime.Vehicle(name: "reject").LoadKc(Kc.Ticked("""
                if (k == 1u) { t.x = 1.0f; t.y = 0.0f; t.z = 0.0f; kfs_translate(t); }
            """, "vec3 t;\n"));
        reject.RunTicks(3);
        Assert.That(reject.RejectedRequests, Is.EqualTo(1u), reject.Dump);
        Assert.That(reject.LogText, Has.Some.StartsWith("rejected translate: "), reject.Dump);

        // A trap halts the program with its file and line; the halted computer still takes ticks
        // but no code runs.
        string trapping = Kc.Ticked("""
                if (k == 4u) {
                    q = 10u / zero;
                }
            """, "uint32_t zero;\nuint32_t q;\n");
        int trapLine = trapping.Split('\n').ToList().FindIndex(l => l.Contains("10u / zero", StringComparison.Ordinal)) + 1;
        using SimVehicle trap = Runtime.Vehicle(name: "trap").LoadKc(trapping);
        trap.RunTicks(5);
        Assert.That(trap.State, Is.EqualTo(SimComputerState.Halted), trap.Dump);
        Assert.That(trap.Trap, Is.EqualTo(new SimTrap("DIV_ZERO", trap.Trap!.Pc, 0, "run", "main.c", trapLine, 0)), trap.Dump);
        Assert.That(trap.LogText, Has.Some.StartsWith("trap DIV_ZERO at ").And.Contain($"(run, main.c:{trapLine})"), trap.Dump);
        Assert.That(trap.ReadGlobal<uint>("n"), Is.EqualTo(5u));
        trap.RunTicks(5);
        Assert.That((trap.TicksRun, trap.ReadGlobal<uint>("n"), trap.State), Is.EqualTo((10L, 5u, SimComputerState.Halted)),
            "halted: the host keeps taking ticks, the program does not run");

        // kfs_halt(): halted without a trap.
        using SimVehicle halt = Runtime.Vehicle(name: "halt").LoadKc(Kc.Ticked("if (k == 2u) { kfs_halt(); }"));
        halt.RunTicks(6);
        Assert.That((halt.State, halt.Trap, halt.ReadGlobal<uint>("n")), Is.EqualTo((SimComputerState.Halted, (SimTrap?)null, 3u)), halt.Dump);
    }

    // ------------------------------------------------------------------ time

    [Test]
    public void Pause_Stops_Ticks_But_Applies_Commands()
    {
        using SimVehicle v = Runtime.Vehicle(lights: 1, deployables: 1).LoadKc(Kc.Ticked("""
                if (k == 2u) {
                    x_light_set_on(0u, 1u);
                    x_deploy_set(0u, 1u);
                }
            """));
        v.RunTicks(3);
        Assert.That((v.Parts.Light(0).On, v.Binding.Applied), Is.EqualTo((false, 0L)), "tick 2's commands wait for the next pump");

        v.Pause();
        long time = v.TimeNs;
        for (int i = 0; i < 5; i++) Assert.That(v.Frame(0.1), Is.Zero);

        Assert.That(v.Parts.Light(0).On, Is.True, "the pump ran while paused");
        Assert.That(v.Parts.Deployable(0).GoalS, Is.EqualTo(2.0));
        Assert.That(v.Parts.Deployable(0).PositionS, Is.Zero, "the world does not move while paused");
        Assert.That((v.TicksRun, v.TimeNs, v.ReadGlobal<uint>("n")), Is.EqualTo((3L, time, 3u)), "no tick, no time");
        Assert.Throws<InvalidOperationException>(() => v.RunTicks(1));

        v.Resume();
        Assert.That(v.Frame(0.1), Is.EqualTo(1));
        Assert.That((v.TicksRun, v.ReadGlobal<uint>("n")), Is.EqualTo((4L, 4u)));
        Assert.That(v.Parts.Deployable(0).PositionS, Is.EqualTo(0.1).Within(1e-9));
    }

    [Test]
    public void RunUntil_Uses_Simulated_Time()
    {
        using SimVehicle v = Runtime.Vehicle(deployables: 1).LoadKc(Kc.Ticked("if (k == 5u) { x_deploy_set(0u, 1u); }"));

        double took = v.RunUntil(() => v.Parts.Deployable(0).State == DeployState.Deployed, timeoutSeconds: 10, frameDt: 0.1);

        // Issued in tick 5 (0.5 s), applied by the 0.6 s frame's pump, moving from the 0.7 s frame's
        // step (the game's next physics job), 2 s of travel: deployed at 2.6 s.
        Assert.That(took, Is.EqualTo(2.6).Within(1e-9));
        Assert.That(v.TimeS, Is.EqualTo(2.6).Within(1e-9));
        Assert.That(v.RunUntil(() => true, 10), Is.Zero, "a condition already true runs no frame");

        // An hour of simulated time costs no wall-clock waiting; the timeout names the state.
        var e = Assert.Throws<TimeoutException>(() => v.RunUntil(() => false, timeoutSeconds: 3600, frameDt: 1.0));
        Assert.That(e!.Message, Does.StartWith("condition not met within 3600 s of simulated time"));
        Assert.That(v.TimeS, Is.EqualTo(3602.6).Within(1e-6));
    }

    [Test]
    public void RunTicks_Runs_Exactly_N()
    {
        using SimVehicle v = Runtime.Vehicle().LoadKc("""
            #include <kfs.h>
            #pragma kfs tick_hz 20
            #pragma kfs task run rate_hz=10
            uint32_t n;
            void run(void) { n = n + 1u; }
            """);
        v.RunTicks(7);
        Assert.That((v.TicksRun, v.LastTick, v.ReadGlobal<uint>("n")), Is.EqualTo((7L, (ulong?)6, 4u)), "the task runs in ticks 0, 2, 4, 6");
        Assert.That(v.TimeS, Is.EqualTo(0.30).Within(1e-9), "tick 6 runs at 0.30 s");
        v.RunTicks(5);
        Assert.That((v.TicksRun, v.ReadGlobal<uint>("n")), Is.EqualTo((12L, 6u)));
        Assert.That(v.TicksPerFrame, Is.All.EqualTo(1), "one dispatch per tick");
        v.RunTicks(0);
        Assert.That(v.TicksRun, Is.EqualTo(12L));

        // After ordinary frames that leave the grid between ticks.
        v.Run(0.12, 0.04);
        long before = v.TicksRun;
        v.RunTicks(3);
        Assert.That(v.TicksRun - before, Is.EqualTo(3L));
    }

    // ------------------------------------------------------------------ misuse

    [Test]
    public void Runtime_Misuse_Is_Explicit()
    {
        // One runtime per process.
        KfsRuntime current = Runtime;
        var e = Assert.Throws<InvalidOperationException>(() => KfsRuntime.Start());
        Assert.That(e!.Message, Does.Contain("already active in this process"));
        Assert.That(KfsRuntime.Current, Is.SameAs(current));

        // A failed build: an assertion-friendly exception with the compiler's diagnostics.
        using (SimVehicle v = Runtime.Vehicle())
        {
            var build = Assert.Throws<KfsBuildException>(() => v.LoadKc(Kc.Ticked("    undeclared = 1u;")));
            Assert.That(build!.Diagnostics, Has.Some.Matches<BuildDiagnostic>(d => d.Severity == BuildSeverity.Error && d.File == "main.c"));
            Assert.That(build.Message, Does.StartWith("kC build failed:\nmain.c:"));
            Assert.That(build, Is.InstanceOf<InvalidOperationException>());

            // Nothing loaded: no ticks to run.
            var idle = Assert.Throws<InvalidOperationException>(() => v.RunTicks(1));
            Assert.That(idle!.Message, Does.StartWith("the computer takes no ticks (state NoSoftware)"));
        }

        // A disposed vehicle.
        SimVehicle gone = Runtime.Vehicle(lights: 1).LoadKc(Kc.Ticked(""));
        gone.Dispose();
        Assert.DoesNotThrow(gone.Dispose, "Dispose is idempotent");
        Assert.Throws<ObjectDisposedException>(() => gone.Frame());
        Assert.Throws<ObjectDisposedException>(() => gone.Run(1));
        Assert.Throws<ObjectDisposedException>(() => gone.RunTicks(1));
        Assert.Throws<ObjectDisposedException>(() => gone.LoadKc(Kc.Ticked("")));
        Assert.Throws<ObjectDisposedException>(() => gone.ReadGlobal<uint>("n"));
        Assert.Throws<ObjectDisposedException>(() => gone.Pause());
    }
}
