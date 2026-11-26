using NUnit.Framework;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// What a program sees of its own commands, frame by frame, against the game's order. The
/// decompiled <c>Program.PrepareFrame</c> (called by <c>Program.OnFrame</c>) runs, in this order:
/// <c>Universe.ApplyVehicleSolvers</c>, which commits the part states of the physics job that just
/// finished and applies that job's contact-docking events; <c>KfsIntegration.BeforeInputEvents</c>,
/// where purrTTY's pump runs; <c>InputEvents.ApplyInputEvents</c>, which applies requested
/// activations (staging, decoupling), docking and undocking; and
/// <c>Universe.ExecuteNextVehicleSolvers</c>, which starts the next physics job. In that job each
/// <c>PhysicsBubble</c> dispatches the flight computer (<c>RunFlightComputer</c>: from
/// <c>FullPhysicsPreStep</c> at the start of the frame, at each wake-up in
/// <c>FullPhysicsStepUntil</c>, or in <c>ApplySingleVehicleMotion</c> on rails) before the
/// sub-step's <c>UpdateModulesJob</c> (<c>VehicleUpdateState.UpdateModules</c>) steps the parts.
/// Live fields (a light's switch, an animation's goal, a light's appearance) change at the pump;
/// committed states (POSITION, STATE) one physics job later. LIT reads both
/// (<c>LightModule.IsActive</c>: the live switch and the switch's committed
/// <c>PowerConsumerState.Active</c>), so it follows a switch-off at once and a switch-on one job later.
/// </summary>
[TestFixture]
public sealed class FrameOrderTests : RuntimeFixture
{
    [Test]
    public void Committed_Part_State_Is_One_Frame_Behind_The_Pump()
    {
        // Tick k runs in frame k (10 Hz, frames of 0.1 s). The commands go out in tick 2.
        using SimVehicle v = Runtime.Vehicle(lights: 1, deployables: 1).LoadKc(Kc.Ticked("""
                if (k == 2u) { x_light_set_on(0u, 1u); x_deploy_set(0u, 1u); }
                if (on_at == 0u && x_light_geti(0u, X_LIGHT_ON) != 0u) { on_at = k; }
                if (lit_at == 0u && x_light_geti(0u, X_LIGHT_LIT) != 0u) { lit_at = k; }
                if (target_at == 0u && x_deploy_getf(0u, X_DEPLOY_TARGET) > 0.0f) { target_at = k; }
                if (moving_at == 0u && x_deploy_geti(0u, X_DEPLOY_MOVING) != 0u) { moving_at = k; }
                if (position_at == 0u && x_deploy_getf(0u, X_DEPLOY_POSITION) > 0.0f) { position_at = k; }
                if (state_at == 0u && x_deploy_geti(0u, X_DEPLOY_STATE) == X_STATE_DEPLOYING) { state_at = k; }
            """, "uint32_t on_at; uint32_t lit_at; uint32_t target_at; uint32_t moving_at; uint32_t position_at; uint32_t state_at;\n"));

        v.RunTicks(8);

        string seen = string.Join(" ", new[] { "on_at", "target_at", "moving_at", "lit_at", "position_at", "state_at" }
            .Select(g => $"{g}={v.ReadGlobal<uint>(g)}"));
        // Frame 3's pump applies them: the switch and the goal are live fields (tick 3), MOVING
        // compares the committed position with the live goal (tick 3); LIT, POSITION and STATE are
        // what frame 3's physics job commits, read from tick 4.
        Assert.That(seen, Is.EqualTo("on_at=3 target_at=3 moving_at=3 lit_at=4 position_at=4 state_at=4"), v.Dump);
    }

    [Test]
    public void Lit_Follows_A_Switch_Off_At_Once_And_A_Switch_On_One_Frame_Later()
    {
        // Light 0 starts off and is switched on in tick 2 and off in tick 5; light 1 starts on and
        // is switched off in tick 2; light 2 has no switch and no power; light 3 is switched on but
        // unpowered until the test restores its power after tick 6.
        var spec = new SimVehicleSpec
        {
            Lights =
            [
                new SimLightSpec { Name = "Off then on then off" },
                new SimLightSpec { Name = "On then off", On = true },
                new SimLightSpec { Name = "Switchless", HasSwitch = false, Powered = false },
                new SimLightSpec { Name = "Unpowered", On = true, Powered = false },
            ],
        };
        using SimVehicle v = Vehicle(spec).LoadKc(Kc.Ticked("""
                if (k < 10u) {
                    on0[k] = x_light_geti(0u, X_LIGHT_ON);
                    lit0[k] = x_light_geti(0u, X_LIGHT_LIT);
                    on1[k] = x_light_geti(1u, X_LIGHT_ON);
                    lit1[k] = x_light_geti(1u, X_LIGHT_LIT);
                    lit2[k] = x_light_geti(2u, X_LIGHT_LIT);
                    on3[k] = x_light_geti(3u, X_LIGHT_ON);
                    lit3[k] = x_light_geti(3u, X_LIGHT_LIT);
                }
                if (k == 2u) { x_light_set_on(0u, 1u); x_light_set_on(1u, 0u); }
                if (k == 5u) { x_light_set_on(0u, 0u); }
            """, "uint32_t on0[10]; uint32_t lit0[10]; uint32_t on1[10]; uint32_t lit1[10]; uint32_t lit2[10]; uint32_t on3[10]; uint32_t lit3[10];\n"));

        v.RunTicks(7);                         // ticks 0..6
        v.Parts.Light(3).Powered = true;       // power arrives during frame 6's physics job
        v.RunTicks(3);                         // ticks 7..9

        string Row(string g) => g + "=" + string.Join("", Enumerable.Range(0, 10).Select(i => v.ReadGlobal<uint>($"{g}[{i}]")));
        string seen = string.Join(" ", new[] { "on0", "lit0", "on1", "lit1", "lit2", "on3", "lit3" }.Select(Row));
        // The pump of frame 3 applies tick 2's commands: light 0's switch reads on from tick 3 and
        // its LIT, which also needs the switch state frame 3's physics job commits, from tick 4;
        // light 1's switch and LIT both read off from tick 3. Tick 5's switch-off of light 0 shows
        // in both from tick 6. A light without a switch is lit whatever its power; a switched-on
        // light without power is dark until a physics step has committed it powered (tick 7).
        Assert.That(seen, Is.EqualTo(
            "on0=0001110000 lit0=0000110000 on1=1110000000 lit1=1110000000 lit2=1111111111 on3=1111111111 lit3=0000000111"), v.Dump);
    }

    [Test]
    public void Every_Tick_Of_A_Frame_Sees_The_Same_Part_State()
    {
        // Time warp: one 0.5 s frame runs five ticks; the parts move only between frames.
        using SimVehicle v = Runtime.Vehicle(deployables: 1).LoadKc(Kc.Ticked("""
                if (k == 0u) { x_deploy_set(0u, 1u); }
                if (k < 11u) { pos[k] = x_deploy_getf(0u, X_DEPLOY_POSITION); }
            """, "float pos[11];\n"));

        v.Frame(0.1);   // tick 0 at the start time: the command goes out
        v.Frame(0.5);   // the pump applies it, the parts have not stepped with it yet: ticks 1..5 read 0
        v.Frame(0.5);   // stepped 0.5 s of the 2 s travel (one physics job): ticks 6..10 read 0.25

        string pos = string.Join(" ", Enumerable.Range(0, 11).Select(i => v.ReadGlobal<float>($"pos[{i}]").ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(v.TicksPerFrame, Is.EqualTo(new[] { 1, 5, 5 }), v.Dump);
        Assert.That(pos, Is.EqualTo("0 0 0 0 0 0 0.25 0.25 0.25 0.25 0.25"), v.Dump);
    }
}
