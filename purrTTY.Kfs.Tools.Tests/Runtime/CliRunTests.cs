using NUnit.Framework;

namespace purrTTY.Kfs.Tools.Tests.Runtime;

/// <summary><c>purr-kfs run</c> on real temporary workspaces, in process, on the shared runtime.</summary>
[TestFixture]
public sealed class CliRunTests : ToolsRuntimeFixture
{
    [Test]
    public void Cli_Run_Blink_On_Two_Lights_Prints_The_Final_Light_State()
    {
        using var ws = new TempWorkspace().Write("blink.c", CliCheckTests.Blink);

        CliOutcome r = PurrKfs.In(ws, "run", "blink.c", "--lights", "2", "--for", "1s", "--state");

        // Ticks 0..10 run (0 to 1 s); light 0 follows (n / 5) % 2. The last command, tick 10's
        // "off", is issued in the last frame and applied at the next frame boundary, so the state
        // shows tick 9's "on" (as the game would at that moment). Light 1 is never touched.
        Assert.That(r.ExitCode, Is.EqualTo(0), r.ToString);
        Assert.That(r.Stdout, Does.EndWith(
            "state:\n" +
            "  machine: Running, 11 ticks at 10 Hz, 0 overruns, 0 rejected requests, no trap\n" +
            "  light 0 \"Light 0\": on, lit, powered; intensity 2, range 30 m, color 1 0.9 0.8, cone 0.3-0.6 rad\n" +
            "  light 1 \"Light 1\": off, dark, powered; intensity 2, range 30 m, color 1 0.9 0.8, cone 0.3-0.6 rad\n"), r.ToString);
        Assert.That(r.Stdout, Does.Contain("run: 0 to 1 s simulated, 61 frames at 60 Hz, vehicle \"sim\" (2 lights, 0 deployables, 0 actuators)\n"));
        Assert.That(r.Stdout, Does.Not.Contain("log:").And.Not.Contain("requests:"), "only --state was asked for");

        CliOutcome later = PurrKfs.In(ws, "run", "blink.c", "--lights", "2", "--for", "1500ms", "--state", "--frame", "20");
        Assert.That(later.Stdout, Does.Contain("  light 0 \"Light 0\": off, dark, powered;"), "ticks 10..14 switch it off");
        Assert.That(later.Stdout, Does.Contain("0 to 1.5 s simulated, 31 frames at 20 Hz"));
    }

    [Test]
    public void Cli_Run_Requests_Shows_Decoded_Stock_Requests()
    {
        using var ws = new TempWorkspace().Write("stage.c", """
            #include <kfs.h>
            #pragma kfs tick_hz 10
            #pragma kfs task run rate_hz=10
            uint32_t n;
            void run(void)
            {
                if (n == 1u) { kfs_stage(); }
                if (n == 2u) { kfs_set_throttle(0.75f); }
                if (n == 3u) { kfs_set_throttle(1.0f); kfs_logf("full"); }
                n = n + 1u;
            }

            """);

        CliOutcome r = PurrKfs.In(ws, "run", "stage.c", "--for", "1s", "--requests");

        // kfs_stage() is acknowledged after its frame, so the program carries on.
        Assert.That(r.ExitCode, Is.EqualTo(0), r.ToString);
        Assert.That(r.Stdout, Does.EndWith(
            "requests:\n" +
            "  t=0.100 stage\n" +
            "  t=0.200 set_throttle(0.75)\n" +
            "  t=0.300 set_throttle(1.0)\n"), r.ToString);
    }

    [Test]
    public void Cli_Run_Trapping_Program_Exits_1_And_Reports_The_Trap_Location()
    {
        using var ws = new TempWorkspace().Write("t/main.c", """
            #include <kfs.h>
            #pragma kfs tick_hz 10
            #pragma kfs task run rate_hz=10
            uint32_t n;
            uint32_t zero;
            uint32_t q;
            void run(void)
            {
                if (n == 3u) { q = 10u / zero; }
                n = n + 1u;
            }

            """);

        CliOutcome r = PurrKfs.In(ws, "run", "t/main.c", "--for", "1s", "--log");

        // The trap's file name (main.c) is reported at its workspace path.
        Assert.That(r.ExitCode, Is.EqualTo(1), r.ToString);
        Assert.That(r.Stdout, Does.Contain("  k=3 t=0.300 trap DIV_ZERO at "), r.ToString);
        Assert.That(r.Stdout, Does.EndWith("trap: DIV_ZERO in run (t/main.c:9)\n"), r.ToString);

        CliOutcome state = PurrKfs.In(ws, "run", "t/main.c", "--for", "1s", "--state");
        Assert.That(state.Stdout, Does.Contain("  machine: Halted, 11 ticks at 10 Hz, 0 overruns, 0 rejected requests, trap DIV_ZERO in run (t/main.c:9)\n"));
    }

    [Test]
    public void Cli_Run_Build_Failure_Exits_1_Without_Running()
    {
        using var ws = new TempWorkspace().Write("bad.c", "#include <kfs.h>\nvoid run(void) { nope(); }\n");

        CliOutcome r = PurrKfs.In(ws, "run", "bad.c");

        Assert.That(r.ExitCode, Is.EqualTo(1), r.ToString);
        Assert.That(r.Stdout, Does.Contain("build failed: bad.c (kC project \"bad\", 1 file): "));
        Assert.That(r.Stdout, Does.Not.Contain("run: "));
    }

    [Test]
    public void Cli_Run_Log_And_State_Golden_Output()
    {
        using var ws = new TempWorkspace().Write("golden.c", """
            #include <kfs.h>
            #include <purrtty.h>
            #pragma kfs tick_hz 10
            #pragma kfs task run rate_hz=10
            uint32_t n;
            void run(void)
            {
                if (n == 0u) { kfs_logf("lights %u deployables %u", x_light_count(), x_deploy_count()); x_deploy_set(0u, 1u); }
                if (n == 10u) { x_light_set_on(0u, 1u); x_light_set_intensity(0u, 1.5f); kfs_logf("light on at k=%u", n); }
                n = n + 1u;
            }

            """);

        CliOutcome r = PurrKfs.In(ws, "run", "golden.c", "--lights", "1", "--deployables", "1", "--for", "3s", "--log", "--state");

        // Golden: a change here is a change in the game's toolchain or the extension; review it.
        Assert.That(r.ExitCode, Is.EqualTo(0), r.ToString);
        Assert.That(r.Stderr, Is.Empty);
        Assert.That(r.Stdout, Is.EqualTo("""
            build ok: golden.c (kC project "golden", 1 file): 0 errors, 0 warnings; code 107 bytes, RAM 56 bytes
            run: 0 to 3 s simulated, 181 frames at 60 Hz, vehicle "sim" (1 light, 1 deployable, 0 actuators)
            log:
              k=0 t=0.000 loaded 948 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x813232B4
              k=0 t=0.000 start: Running
              k=0 t=0.000 lights 1 deployables 1
              k=10 t=1.000 light on at k=10
            state:
              machine: Running, 31 ticks at 10 Hz, 0 overruns, 0 rejected requests, no trap
              light 0 "Light 0": on, lit, powered, overridden; intensity 1.5, range 30 m, color 1 0.9 0.8, cone 0.3-0.6 rad
              deployable 0 "Deployable 0": Deployed; position 2 s of 2 s, goal 2 s

            """.ReplaceLineEndings("\n")));

        // Deterministic: a second run prints the same.
        Assert.That(PurrKfs.In(ws, "run", "golden.c", "--lights", "1", "--deployables", "1", "--for", "3s", "--log", "--state").Stdout, Is.EqualTo(r.Stdout));
    }
}
