using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// Ordinary kFS programs on the test runtime: the game's own ascent example against scripted
/// telemetry, and a program that mixes telemetry with the mod's part services.
/// </summary>
[TestFixture]
public sealed class StockProgramTests : RuntimeFixture
{
    private const double Km = 1000.0;

    /// <summary>The scripted flight of the ascent test, by simulated time.</summary>
    private static SimTelemetry Ascent(double t, uint? burn)
    {
        var telemetry = new SimTelemetry();
        SimFlight f = telemetry.Flight;
        f.InAtmosphere = true;
        if (t < 1.0)
        {
            // On the pad.
        }
        else if (t < 3.0)
        {
            // Climbing, apoapsis below the 200 km target.
            f.AltitudeM = 40 * Km * (t - 1.0);
            f.ApoapsisM = 60 * Km * (t - 1.0);
        }
        else
        {
            // Apoapsis above the target: MECO. From 4 s out of the atmosphere; the upper stage's
            // engine reports active with propellant from 4.5 s.
            f.AltitudeM = t < 4.0 ? 80 * Km : 170 * Km;
            f.ApoapsisM = 210 * Km;
            f.TimeToApoapsisS = 30;
            f.InAtmosphere = t < 4.0;
            telemetry.Props.Engines = [new SimEngine { Active = t >= 4.5, PropellantOk = t >= 4.5, MaxThrustN = 1.0e5, IspS = 340 }];
        }
        if (t >= 6.0 && burn is uint handle)
        {
            // The game would fly the burn; the test reports it done.
            f.PeriapsisM = 199 * Km;
            telemetry.BurnStatus = new SimBurnStatus { Burn = handle, State = SimBurnState.Done, EndCause = 1 };
        }
        return telemetry;
    }

    [Test]
    public void Stock_Example_Ascent_Runs_Against_Scripted_Telemetry()
    {
        string source = AscentSource();
        using SimVehicle v = Runtime.Vehicle(engines: 1, name: "rocket");
        v.LoadKc(new Dictionary<string, string>(StringComparer.Ordinal) { ["ascent_simple.c"] = source });
        v.TelemetryScript = t => Ascent(t, v.Requests.FirstOrDefault(r => r.Name == "plan_circularize")?.Handle);

        // phase: LAUNCH 0, ASCENT 1, COAST 2, CIRCULARIZE 3, DONE 4.
        v.RunUntil(() => v.ReadGlobal<uint>("phase") == 4, timeoutSeconds: 20, frameDt: 0.1);

        IReadOnlyList<string> requests = v.RequestText;
        string all = string.Join("\n", requests);

        // LAUNCH (tick 0): straight up, full throttle, engines on.
        Assert.That(requests.Take(3), Is.EqualTo(new[] { "hold_heading(1.5707964, 1.5707964, up)", "set_throttle(1.0)", "engines_on" }), all);

        // ASCENT (ticks 1-29): the gravity turn, one heading hold a tick, pitch falling with altitude.
        var turn = v.Requests.Skip(3).TakeWhile(r => r.Name == "hold_heading").ToList();
        Assert.That(turn, Has.Count.EqualTo(29), all);
        Assert.That(turn.Select(r => r.Arguments.Select(a => a.Name)), Has.All.EqualTo(new[] { "heading_rad", "pitch_rad", "roll" }));
        Assert.That(turn.Select(r => r.F(1)), Is.Ordered.Descending);
        Assert.That(turn[0].F(1), Is.EqualTo(90f * Deg).Within(1e-6), "tick 1, on the pad");
        Assert.That(turn[^1].F(1), Is.EqualTo((float)(90 - 80 * 76.0 / 100) * Deg).Within(1e-6), "tick 29: 76 km, pitch 29.2 degrees");

        // MECO (tick 30), COAST, then the circularisation once out of the air with the engine ready (tick 45).
        Assert.That(requests.Skip(3 + turn.Count), Is.EqualTo(new[]
        {
            "engines_off",
            "stage",
            "stage",
            "hold_direction(vlf_prograde, free)",
            "plan_circularize(apoapsis)",
            "burn_start(1)",
        }), all);
        StockRequest plan = v.Requests.Single(r => r.Name == "plan_circularize");
        Assert.That(plan.Handle, Is.EqualTo(1u), "the host issued the handle synchronously");
        Assert.That(plan.TimeS, Is.EqualTo(4.5).Within(1e-9));
        Assert.That(v.Requests.Where(r => r.Name == "stage").Select(r => r.TimeS), Is.All.EqualTo(3.0).Within(1e-9));
        Assert.That(v.LiveBurnPlans, Is.EqualTo(1), "nothing flies the burn: the plan stays live");

        Assert.That(v.LogText.Where(l => !l.StartsWith("loaded ", StringComparison.Ordinal) && !l.StartsWith("start", StringComparison.Ordinal)),
            Is.EqualTo(new[]
            {
                "MECO: altitude 80.0 km, apoapsis 210.0 km",
                "circularization planned: apoapsis 210.0 km in 30 s",
                "circularization ended (state 4): apoapsis 210.0 km, periapsis 199.0 km",
            }), v.Dump);
        Assert.That((v.ReadGlobal<uint>("circ_burn"), v.ReadGlobal<uint>("final_state")), Is.EqualTo((1u, (uint)SimBurnState.Done)));
        Assert.That((v.State, v.RejectedRequests, v.AwaitingStage), Is.EqualTo((SimComputerState.Running, 0u, false)), v.Dump);
        Assert.That(v.TimeS, Is.EqualTo(6.0).Within(1e-9), "DONE in the first tick that saw the scripted burn status");
        Assert.That(v.EndBurn(1), Is.True);
        Assert.That(v.LiveBurnPlans, Is.Zero);
    }

    private const float Deg = 0.017453292519943295f;

    [Test]
    public void Mixed_Program_Uses_Telemetry_And_Parts()
    {
        using SimVehicle v = Runtime.Vehicle(deployables: 1).LoadKc("""
            #include <kfs.h>
            #include <vehicle.h>
            #include <purrtty.h>
            #pragma kfs tick_hz 10
            #pragma kfs subscribe MSG_FLIGHT_DATA flight
            #pragma kfs task run rate_hz=10
            kfs_msg_flight_data_t flight;
            uint32_t want;
            void run(void)
            {
                uint32_t up = flight.altitude_m > 10000.0f ? 1u : 0u;
                if (up != want) {
                    want = up;
                    x_deploy_set(0u, up);
                    kfs_logf("solar %u at %.0f m", up, flight.altitude_m);
                }
            }
            """);
        // Up at 5 km/s to 20 km over 4 s, then back down.
        v.TelemetryScript = t => new SimTelemetry { Flight = { AltitudeM = t <= 4 ? 5000 * t : Math.Max(0, 20000 - 5000 * (t - 4)) } };
        v.Run(9.0, 0.1);

        Assert.That(v.LogText.Where(l => l.StartsWith("solar ", StringComparison.Ordinal)),
            Is.EqualTo(new[] { "solar 1 at 10500 m", "solar 0 at 10000 m" }), v.Dump);
        // Deploy issued in the tick at 2.1 s and applied by the next frame's pump (after that frame's
        // step: the parts' clock reads 2.2 s), moving from the step of the frame after (2.3 s): 2 s of
        // travel; retract issued at 6.0 s, applied at 6.1 s, moving from 6.2 s.
        var history = v.Parts.DeployHistory;
        Assert.That(history.Select(h => (h.Index, h.State)), Is.EqualTo(new[]
        {
            (0, DeployState.Deploying), (0, DeployState.Deployed), (0, DeployState.Retracting), (0, DeployState.Retracted),
        }));
        Assert.That(history.Select(h => h.TimeS), Is.EqualTo(new[] { 2.3, 4.2, 6.2, 8.1 }).Within(1e-6));
        Assert.That(v.Parts.Applied.Select(a => a.Command.U), Is.EqualTo(new[] { 1u, 0u }));
        Assert.That(v.Parts.Applied.Select(a => a.TimeS), Is.EqualTo(new[] { 2.2, 6.1 }).Within(1e-6));
    }

    // The game's own example, verbatim (KSA.Kfs.Host.Workbench.Examples.Files).
    private static string AscentSource() => KSA.Kfs.Host.Workbench.Examples.Files[KSA.Kfs.Host.Workbench.Examples.ASCENT_SIMPLE];
}
