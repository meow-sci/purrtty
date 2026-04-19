using caTTY.Core.Rpc;
using caTTY.TermSequenceRpc;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.TermSequenceRpc.Tests.Unit;

/// <summary>
/// Unit tests for KSA-specific OSC RPC handler.
/// Note: Full integration testing requires actual KSA game context
/// or sophisticated mocking of Program.ControlledVehicle.
/// </summary>
[TestFixture]
[Category("Unit")]
public class KsaOscRpcHandlerTests
{
    private KsaOscRpcHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new KsaOscRpcHandler(NullLogger.Instance);
    }

    #region Instantiation Tests

    [Test]
    public void Constructor_WithLogger_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => new KsaOscRpcHandler(NullLogger.Instance));
    }

    [Test]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new KsaOscRpcHandler(null!));
    }

    #endregion

    #region Actions Constants Tests - Engine Control

    [Test]
    public void Actions_EngineIgnite_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.EngineIgnite, Is.EqualTo("engine_ignite"));
    }

    [Test]
    public void Actions_EngineShutdown_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.EngineShutdown, Is.EqualTo("engine_shutdown"));
    }

    #endregion

    #region Actions Constants Tests - Flight Computer Actions

    [Test]
    public void Actions_DeleteNextBurn_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.DeleteNextBurn, Is.EqualTo("fc_delete_next_burn"));
    }

    [Test]
    public void Actions_WarpToNextBurn_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.WarpToNextBurn, Is.EqualTo("fc_warp_to_next_burn"));
    }

    #endregion

    #region Actions Constants Tests - Attitude Profiles

    [Test]
    public void Actions_AttitudeProfileStrict_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.AttitudeProfileStrict, Is.EqualTo("fc_attitude_profile_strict"));
    }

    [Test]
    public void Actions_AttitudeProfileBalanced_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.AttitudeProfileBalanced, Is.EqualTo("fc_attitude_profile_balanced"));
    }

    [Test]
    public void Actions_AttitudeProfileRelaxed_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.AttitudeProfileRelaxed, Is.EqualTo("fc_attitude_profile_relaxed"));
    }

    #endregion

    #region Actions Constants Tests - Track Targets

    [Test]
    public void Actions_TrackNone_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackNone, Is.EqualTo("fc_track_none"));
    }

    [Test]
    public void Actions_TrackCustom_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackCustom, Is.EqualTo("fc_track_custom"));
    }

    [Test]
    public void Actions_TrackForward_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackForward, Is.EqualTo("fc_track_forward"));
    }

    [Test]
    public void Actions_TrackBackward_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackBackward, Is.EqualTo("fc_track_backward"));
    }

    [Test]
    public void Actions_TrackUp_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackUp, Is.EqualTo("fc_track_up"));
    }

    [Test]
    public void Actions_TrackDown_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackDown, Is.EqualTo("fc_track_down"));
    }

    [Test]
    public void Actions_TrackAhead_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackAhead, Is.EqualTo("fc_track_ahead"));
    }

    [Test]
    public void Actions_TrackBehind_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackBehind, Is.EqualTo("fc_track_behind"));
    }

    [Test]
    public void Actions_TrackRadialOut_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackRadialOut, Is.EqualTo("fc_track_radial_out"));
    }

    [Test]
    public void Actions_TrackRadialIn_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackRadialIn, Is.EqualTo("fc_track_radial_in"));
    }

    [Test]
    public void Actions_TrackPrograde_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackPrograde, Is.EqualTo("fc_track_prograde"));
    }

    [Test]
    public void Actions_TrackRetrograde_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackRetrograde, Is.EqualTo("fc_track_retrograde"));
    }

    [Test]
    public void Actions_TrackNormal_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackNormal, Is.EqualTo("fc_track_normal"));
    }

    [Test]
    public void Actions_TrackAntiNormal_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackAntiNormal, Is.EqualTo("fc_track_antinormal"));
    }

    [Test]
    public void Actions_TrackOutward_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackOutward, Is.EqualTo("fc_track_outward"));
    }

    [Test]
    public void Actions_TrackInward_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackInward, Is.EqualTo("fc_track_inward"));
    }

    [Test]
    public void Actions_TrackPositiveDv_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackPositiveDv, Is.EqualTo("fc_track_positive_dv"));
    }

    [Test]
    public void Actions_TrackNegativeDv_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackNegativeDv, Is.EqualTo("fc_track_negative_dv"));
    }

    [Test]
    public void Actions_TrackToward_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackToward, Is.EqualTo("fc_track_toward"));
    }

    [Test]
    public void Actions_TrackAway_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackAway, Is.EqualTo("fc_track_away"));
    }

    [Test]
    public void Actions_TrackAntivel_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackAntivel, Is.EqualTo("fc_track_antivel"));
    }

    [Test]
    public void Actions_TrackAlign_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.TrackAlign, Is.EqualTo("fc_track_align"));
    }

    #endregion

    #region Actions Constants Tests - Vehicle Reference Frames

    [Test]
    public void Actions_VehicleFrameEclBody_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.VehicleFrameEclBody, Is.EqualTo("vehicle_frame_ecl_body"));
    }

    [Test]
    public void Actions_VehicleFrameEnuBody_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.VehicleFrameEnuBody, Is.EqualTo("vehicle_frame_enu_body"));
    }

    [Test]
    public void Actions_VehicleFrameLvlh_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.VehicleFrameLvlh, Is.EqualTo("vehicle_frame_lvlh"));
    }

    [Test]
    public void Actions_VehicleFrameVlfBody_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.VehicleFrameVlfBody, Is.EqualTo("vehicle_frame_vlf_body"));
    }

    [Test]
    public void Actions_VehicleFrameBurnBody_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.VehicleFrameBurnBody, Is.EqualTo("vehicle_frame_burn_body"));
    }

    [Test]
    public void Actions_VehicleFrameDock_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.VehicleFrameDock, Is.EqualTo("vehicle_frame_dock"));
    }

    #endregion

    #region Actions Constants Tests - Flight Computer Modes

    [Test]
    public void Actions_BurnModeManual_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.BurnModeManual, Is.EqualTo("fc_burn_mode_manual"));
    }

    [Test]
    public void Actions_BurnModeAuto_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.BurnModeAuto, Is.EqualTo("fc_burn_mode_auto"));
    }

    [Test]
    public void Actions_RollModeDecoupled_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.RollModeDecoupled, Is.EqualTo("fc_roll_mode_decoupled"));
    }

    [Test]
    public void Actions_RollModeUp_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.RollModeUp, Is.EqualTo("fc_roll_mode_up"));
    }

    [Test]
    public void Actions_RollModeDown_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.RollModeDown, Is.EqualTo("fc_roll_mode_down"));
    }

    [Test]
    public void Actions_AttitudeModeManual_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.AttitudeModeManual, Is.EqualTo("fc_attitude_mode_manual"));
    }

    [Test]
    public void Actions_AttitudeModeAuto_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.AttitudeModeAuto, Is.EqualTo("fc_attitude_mode_auto"));
    }

    [Test]
    public void Actions_ThrustModeDirect_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.ThrustModeDirect, Is.EqualTo("fc_thrust_mode_direct"));
    }

    [Test]
    public void Actions_ThrustModePulse_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.ThrustModePulse, Is.EqualTo("fc_thrust_mode_pulse"));
    }

    #endregion

    #region Actions Constants Tests - Global Commands

    [Test]
    public void Actions_ToggleFps_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.ToggleFps, Is.EqualTo("toggle_fps"));
    }

    [Test]
    public void Actions_ToggleUi_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.ToggleUi, Is.EqualTo("toggle_ui"));
    }

    [Test]
    public void Actions_SimSpeedIncrease_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.SimSpeedIncrease, Is.EqualTo("sim_speed_increase"));
    }

    [Test]
    public void Actions_SimSpeedDecrease_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.SimSpeedDecrease, Is.EqualTo("sim_speed_decrease"));
    }

    [Test]
    public void Actions_SimSpeedReset_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.SimSpeedReset, Is.EqualTo("sim_speed_reset"));
    }

    [Test]
    public void Actions_SeekNextVehicle_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.SeekNextVehicle, Is.EqualTo("seek_next_vehicle"));
    }

    [Test]
    public void Actions_SeekPrevVehicle_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.SeekPrevVehicle, Is.EqualTo("seek_prev_vehicle"));
    }

    [Test]
    public void Actions_SeekNextCelestial_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.SeekNextCelestial, Is.EqualTo("seek_next_celestial"));
    }

    [Test]
    public void Actions_SeekPrevCelestial_HasCorrectValue()
    {
        Assert.That(KsaOscRpcHandler.Actions.SeekPrevCelestial, Is.EqualTo("seek_prev_celestial"));
    }

    #endregion

    #region HandleCommand Tests - Engine Control

    // NOTE: These tests verify the handler doesn't crash when called.
    // Full integration testing would require mocking Program.ControlledVehicle,
    // which is a global static from the KSA game engine.

    [Test]
    public void HandleCommand_EngineIgnite_DoesNotThrow()
    {
        string payload = "{\"action\":\"engine_ignite\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_EngineShutdown_DoesNotThrow()
    {
        string payload = "{\"action\":\"engine_shutdown\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    #region HandleCommand Tests - Flight Computer Actions

    [Test]
    public void HandleCommand_DeleteNextBurn_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_delete_next_burn\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_WarpToNextBurn_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_warp_to_next_burn\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    #region HandleCommand Tests - Attitude Profiles

    [Test]
    public void HandleCommand_AttitudeProfileStrict_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_attitude_profile_strict\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_AttitudeProfileBalanced_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_attitude_profile_balanced\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_AttitudeProfileRelaxed_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_attitude_profile_relaxed\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    #region HandleCommand Tests - Track Targets

    [Test]
    public void HandleCommand_TrackNone_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_none\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackCustom_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_custom\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackForward_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_forward\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackBackward_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_backward\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackUp_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_up\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackDown_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_down\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackAhead_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_ahead\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackBehind_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_behind\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackRadialOut_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_radial_out\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackRadialIn_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_radial_in\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackPrograde_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_prograde\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackRetrograde_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_retrograde\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackNormal_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_normal\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackAntiNormal_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_antinormal\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackOutward_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_outward\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackInward_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_inward\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackPositiveDv_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_positive_dv\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackNegativeDv_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_negative_dv\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackToward_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_toward\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackAway_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_away\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackAntivel_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_antivel\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_TrackAlign_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_track_align\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    #region HandleCommand Tests - Vehicle Reference Frames

    [Test]
    public void HandleCommand_VehicleFrameEclBody_DoesNotThrow()
    {
        string payload = "{\"action\":\"vehicle_frame_ecl_body\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_VehicleFrameEnuBody_DoesNotThrow()
    {
        string payload = "{\"action\":\"vehicle_frame_enu_body\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_VehicleFrameLvlh_DoesNotThrow()
    {
        string payload = "{\"action\":\"vehicle_frame_lvlh\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_VehicleFrameVlfBody_DoesNotThrow()
    {
        string payload = "{\"action\":\"vehicle_frame_vlf_body\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_VehicleFrameBurnBody_DoesNotThrow()
    {
        string payload = "{\"action\":\"vehicle_frame_burn_body\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_VehicleFrameDock_DoesNotThrow()
    {
        string payload = "{\"action\":\"vehicle_frame_dock\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    #region HandleCommand Tests - Flight Computer Modes

    [Test]
    public void HandleCommand_BurnModeManual_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_burn_mode_manual\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_BurnModeAuto_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_burn_mode_auto\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_RollModeDecoupled_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_roll_mode_decoupled\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_RollModeUp_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_roll_mode_up\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_RollModeDown_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_roll_mode_down\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_AttitudeModeManual_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_attitude_mode_manual\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_AttitudeModeAuto_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_attitude_mode_auto\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_ThrustModeDirect_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_thrust_mode_direct\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_ThrustModePulse_DoesNotThrow()
    {
        string payload = "{\"action\":\"fc_thrust_mode_pulse\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    #region HandleCommand Tests - Global Commands

    [Test]
    public void HandleCommand_ToggleFps_DoesNotThrow()
    {
        string payload = "{\"action\":\"toggle_fps\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_ToggleUi_DoesNotThrow()
    {
        string payload = "{\"action\":\"toggle_ui\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_SimSpeedIncrease_DoesNotThrow()
    {
        string payload = "{\"action\":\"sim_speed_increase\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_SimSpeedDecrease_DoesNotThrow()
    {
        string payload = "{\"action\":\"sim_speed_decrease\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_SimSpeedReset_DoesNotThrow()
    {
        string payload = "{\"action\":\"sim_speed_reset\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_SeekNextVehicle_DoesNotThrow()
    {
        string payload = "{\"action\":\"seek_next_vehicle\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_SeekPrevVehicle_DoesNotThrow()
    {
        string payload = "{\"action\":\"seek_prev_vehicle\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_SeekNextCelestial_DoesNotThrow()
    {
        string payload = "{\"action\":\"seek_next_celestial\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    [Test]
    public void HandleCommand_SeekPrevCelestial_DoesNotThrow()
    {
        string payload = "{\"action\":\"seek_prev_celestial\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    #region HandleCommand Tests - Unknown Actions

    [Test]
    public void HandleCommand_UnknownAction_DoesNotThrow()
    {
        string payload = "{\"action\":\"unknown_action\"}";
        Assert.DoesNotThrow(() => _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload));
    }

    #endregion

    // TODO: Integration tests
    // To fully test KSA game integration:
    // 1. Create mock IVehicleController interface
    // 2. Inject it into command handlers instead of using Program.ControlledVehicle
    // 3. Verify SetEnum calls with expected VehicleEngine values
    // 4. OR run tests in actual KSA game context (more complex setup)
}
