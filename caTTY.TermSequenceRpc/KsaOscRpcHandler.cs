using System;
using System.Text.Json;
using caTTY.Core.Rpc;
using KSA;
using Microsoft.Extensions.Logging;

namespace caTTY.TermSequenceRpc;

/// <summary>
/// KSA-specific implementation of OSC RPC handler.
/// Dispatches JSON action commands to KSA game engine for vehicle control.
/// Uses OSC sequences in the private-use range (1000+) which pass through
/// Windows ConPTY, unlike DCS sequences which are filtered.
/// </summary>
public class KsaOscRpcHandler : OscRpcHandler
{
    /// <summary>
    /// Known action names for KSA JSON dispatch.
    /// Supported commands for KSA game vehicle control.
    /// </summary>
    public static class Actions
    {
        #region Engine Control
        /// <summary>Engine ignition action</summary>
        public const string EngineIgnite = "engine_ignite";
        /// <summary>Engine shutdown action</summary>
        public const string EngineShutdown = "engine_shutdown";
        #endregion

        #region Flight Computer Actions
        /// <summary>Delete next planned burn</summary>
        public const string DeleteNextBurn = "fc_delete_next_burn";
        /// <summary>Warp to next planned burn</summary>
        public const string WarpToNextBurn = "fc_warp_to_next_burn";
        #endregion

        #region Attitude Profiles
        /// <summary>Set strict attitude profile</summary>
        public const string AttitudeProfileStrict = "fc_attitude_profile_strict";
        /// <summary>Set balanced attitude profile</summary>
        public const string AttitudeProfileBalanced = "fc_attitude_profile_balanced";
        /// <summary>Set relaxed attitude profile</summary>
        public const string AttitudeProfileRelaxed = "fc_attitude_profile_relaxed";
        #endregion

        #region Track Targets
        /// <summary>Track target: None</summary>
        public const string TrackNone = "fc_track_none";
        /// <summary>Track target: Custom</summary>
        public const string TrackCustom = "fc_track_custom";
        /// <summary>Track target: Forward</summary>
        public const string TrackForward = "fc_track_forward";
        /// <summary>Track target: Backward</summary>
        public const string TrackBackward = "fc_track_backward";
        /// <summary>Track target: Up</summary>
        public const string TrackUp = "fc_track_up";
        /// <summary>Track target: Down</summary>
        public const string TrackDown = "fc_track_down";
        /// <summary>Track target: Ahead</summary>
        public const string TrackAhead = "fc_track_ahead";
        /// <summary>Track target: Behind</summary>
        public const string TrackBehind = "fc_track_behind";
        /// <summary>Track target: Radial Out</summary>
        public const string TrackRadialOut = "fc_track_radial_out";
        /// <summary>Track target: Radial In</summary>
        public const string TrackRadialIn = "fc_track_radial_in";
        /// <summary>Track target: Prograde</summary>
        public const string TrackPrograde = "fc_track_prograde";
        /// <summary>Track target: Retrograde</summary>
        public const string TrackRetrograde = "fc_track_retrograde";
        /// <summary>Track target: Normal</summary>
        public const string TrackNormal = "fc_track_normal";
        /// <summary>Track target: Anti-Normal</summary>
        public const string TrackAntiNormal = "fc_track_antinormal";
        /// <summary>Track target: Outward</summary>
        public const string TrackOutward = "fc_track_outward";
        /// <summary>Track target: Inward</summary>
        public const string TrackInward = "fc_track_inward";
        /// <summary>Track target: Positive DV</summary>
        public const string TrackPositiveDv = "fc_track_positive_dv";
        /// <summary>Track target: Negative DV</summary>
        public const string TrackNegativeDv = "fc_track_negative_dv";
        /// <summary>Track target: Toward</summary>
        public const string TrackToward = "fc_track_toward";
        /// <summary>Track target: Away</summary>
        public const string TrackAway = "fc_track_away";
        /// <summary>Track target: Anti-velocity</summary>
        public const string TrackAntivel = "fc_track_antivel";
        /// <summary>Track target: Align</summary>
        public const string TrackAlign = "fc_track_align";
        #endregion

        #region Vehicle Reference Frames
        /// <summary>Reference frame: Ecliptic Body</summary>
        public const string VehicleFrameEclBody = "vehicle_frame_ecl_body";
        /// <summary>Reference frame: ENU Body</summary>
        public const string VehicleFrameEnuBody = "vehicle_frame_enu_body";
        /// <summary>Reference frame: LVLH</summary>
        public const string VehicleFrameLvlh = "vehicle_frame_lvlh";
        /// <summary>Reference frame: VLF Body</summary>
        public const string VehicleFrameVlfBody = "vehicle_frame_vlf_body";
        /// <summary>Reference frame: Burn Body</summary>
        public const string VehicleFrameBurnBody = "vehicle_frame_burn_body";
        /// <summary>Reference frame: Dock/Target</summary>
        public const string VehicleFrameDock = "vehicle_frame_dock";
        #endregion

        #region Flight Computer Burn Mode
        /// <summary>Burn mode: Manual</summary>
        public const string BurnModeManual = "fc_burn_mode_manual";
        /// <summary>Burn mode: Auto</summary>
        public const string BurnModeAuto = "fc_burn_mode_auto";
        #endregion

        #region Flight Computer Roll Mode
        /// <summary>Roll mode: Decoupled</summary>
        public const string RollModeDecoupled = "fc_roll_mode_decoupled";
        /// <summary>Roll mode: Up</summary>
        public const string RollModeUp = "fc_roll_mode_up";
        /// <summary>Roll mode: Down</summary>
        public const string RollModeDown = "fc_roll_mode_down";
        #endregion

        #region Flight Computer Attitude Mode
        /// <summary>Attitude mode: Manual</summary>
        public const string AttitudeModeManual = "fc_attitude_mode_manual";
        /// <summary>Attitude mode: Auto</summary>
        public const string AttitudeModeAuto = "fc_attitude_mode_auto";
        #endregion

        #region Flight Computer Manual Thrust Mode
        /// <summary>Thrust mode: Direct</summary>
        public const string ThrustModeDirect = "fc_thrust_mode_direct";
        /// <summary>Thrust mode: Pulse</summary>
        public const string ThrustModePulse = "fc_thrust_mode_pulse";
        #endregion

        #region Global UI Commands
        /// <summary>Toggle FPS display</summary>
        public const string ToggleFps = "toggle_fps";
        /// <summary>Toggle UI visibility</summary>
        public const string ToggleUi = "toggle_ui";
        #endregion

        #region Simulation Control
        /// <summary>Increase simulation speed</summary>
        public const string SimSpeedIncrease = "sim_speed_increase";
        /// <summary>Decrease simulation speed</summary>
        public const string SimSpeedDecrease = "sim_speed_decrease";
        /// <summary>Reset simulation speed</summary>
        public const string SimSpeedReset = "sim_speed_reset";
        #endregion

        #region Navigation Commands
        /// <summary>Seek next vehicle</summary>
        public const string SeekNextVehicle = "seek_next_vehicle";
        /// <summary>Seek previous vehicle</summary>
        public const string SeekPrevVehicle = "seek_prev_vehicle";
        /// <summary>Seek next celestial</summary>
        public const string SeekNextCelestial = "seek_next_celestial";
        /// <summary>Seek previous celestial</summary>
        public const string SeekPrevCelestial = "seek_prev_celestial";
        #endregion
    }

    /// <summary>
    /// Initializes a new instance of the KsaOscRpcHandler.
    /// </summary>
    /// <param name="logger">Logger for debugging and error reporting</param>
    public KsaOscRpcHandler(ILogger logger) : base(logger)
    {
    }

    /// <inheritdoc />
    /// <summary>
    /// Dispatches parsed JSON actions to KSA game engine.
    /// Accesses Program.ControlledVehicle to control the active spacecraft.
    /// </summary>
    protected override void DispatchAction(string action, JsonElement root)
    {
        switch (action)
        {
            // Engine Control
            case Actions.EngineIgnite:
                SetVehicleEnum(VehicleEngine.MainIgnite, "Engine ignite");
                break;
            case Actions.EngineShutdown:
                SetVehicleEnum(VehicleEngine.MainShutdown, "Engine shutdown");
                break;

            // Flight Computer Actions
            case Actions.DeleteNextBurn:
                SetVehicleEnum(FlightComputerAction.DeleteNextBurn, "Delete next burn");
                break;
            case Actions.WarpToNextBurn:
                SetVehicleEnum(FlightComputerAction.WarpToNextBurn, "Warp to next burn");
                break;

            // Attitude Profiles
            case Actions.AttitudeProfileStrict:
                SetVehicleEnum(FlightComputerAttitudeProfile.Strict, "Attitude profile: Strict");
                break;
            case Actions.AttitudeProfileBalanced:
                SetVehicleEnum(FlightComputerAttitudeProfile.Balanced, "Attitude profile: Balanced");
                break;
            case Actions.AttitudeProfileRelaxed:
                SetVehicleEnum(FlightComputerAttitudeProfile.Relaxed, "Attitude profile: Relaxed");
                break;

            // Track Targets
            case Actions.TrackNone:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.None, "Track target: None");
                break;
            case Actions.TrackCustom:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Custom, "Track target: Custom");
                break;
            case Actions.TrackForward:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Forward, "Track target: Forward");
                break;
            case Actions.TrackBackward:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Backward, "Track target: Backward");
                break;
            case Actions.TrackUp:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Up, "Track target: Up");
                break;
            case Actions.TrackDown:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Down, "Track target: Down");
                break;
            case Actions.TrackAhead:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Ahead, "Track target: Ahead");
                break;
            case Actions.TrackBehind:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Behind, "Track target: Behind");
                break;
            case Actions.TrackRadialOut:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.RadialOut, "Track target: Radial Out");
                break;
            case Actions.TrackRadialIn:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.RadialIn, "Track target: Radial In");
                break;
            case Actions.TrackPrograde:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Prograde, "Track target: Prograde");
                break;
            case Actions.TrackRetrograde:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Retrograde, "Track target: Retrograde");
                break;
            case Actions.TrackNormal:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Normal, "Track target: Normal");
                break;
            case Actions.TrackAntiNormal:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.AntiNormal, "Track target: Anti-Normal");
                break;
            case Actions.TrackOutward:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Outward, "Track target: Outward");
                break;
            case Actions.TrackInward:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Inward, "Track target: Inward");
                break;
            case Actions.TrackPositiveDv:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.PositiveDv, "Track target: Positive DV");
                break;
            case Actions.TrackNegativeDv:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.NegativeDv, "Track target: Negative DV");
                break;
            case Actions.TrackToward:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Toward, "Track target: Toward");
                break;
            case Actions.TrackAway:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Away, "Track target: Away");
                break;
            case Actions.TrackAntivel:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Antivel, "Track target: Anti-velocity");
                break;
            case Actions.TrackAlign:
                SetVehicleEnum(FlightComputerAttitudeTrackTarget.Align, "Track target: Align");
                break;

            // Vehicle Reference Frames
            case Actions.VehicleFrameEclBody:
                SetVehicleEnum(VehicleReferenceFrame.EclBody, "Reference frame: Ecliptic Body");
                break;
            case Actions.VehicleFrameEnuBody:
                SetVehicleEnum(VehicleReferenceFrame.EnuBody, "Reference frame: ENU Body");
                break;
            case Actions.VehicleFrameLvlh:
                SetVehicleEnum(VehicleReferenceFrame.Lvlh, "Reference frame: LVLH");
                break;
            case Actions.VehicleFrameVlfBody:
                SetVehicleEnum(VehicleReferenceFrame.VlfBody, "Reference frame: VLF Body");
                break;
            case Actions.VehicleFrameBurnBody:
                SetVehicleEnum(VehicleReferenceFrame.BurnBody, "Reference frame: Burn Body");
                break;
            case Actions.VehicleFrameDock:
                SetVehicleEnum(VehicleReferenceFrame.Dock, "Reference frame: Dock/Target");
                break;

            // Flight Computer Burn Mode
            case Actions.BurnModeManual:
                SetVehicleEnum(FlightComputerBurnMode.Manual, "Burn mode: Manual");
                break;
            case Actions.BurnModeAuto:
                SetVehicleEnum(FlightComputerBurnMode.Auto, "Burn mode: Auto");
                break;

            // Flight Computer Roll Mode
            case Actions.RollModeDecoupled:
                SetVehicleEnum(FlightComputerRollMode.Decoupled, "Roll mode: Decoupled");
                break;
            case Actions.RollModeUp:
                SetVehicleEnum(FlightComputerRollMode.Up, "Roll mode: Up");
                break;
            case Actions.RollModeDown:
                SetVehicleEnum(FlightComputerRollMode.Down, "Roll mode: Down");
                break;

            // Flight Computer Attitude Mode
            case Actions.AttitudeModeManual:
                SetVehicleEnum(FlightComputerAttitudeMode.Manual, "Attitude mode: Manual");
                break;
            case Actions.AttitudeModeAuto:
                SetVehicleEnum(FlightComputerAttitudeMode.Auto, "Attitude mode: Auto");
                break;

            // Flight Computer Manual Thrust Mode
            case Actions.ThrustModeDirect:
                SetVehicleEnum(FlightComputerManualThrustMode.Direct, "Thrust mode: Direct");
                break;
            case Actions.ThrustModePulse:
                SetVehicleEnum(FlightComputerManualThrustMode.Pulse, "Thrust mode: Pulse");
                break;

            // Global UI Commands
            case Actions.ToggleFps:
                ExecuteToggle(
                    () => GameSettings.Current.Interface.FrameStatistics,
                    v => GameSettings.Current.Interface.FrameStatistics = v,
                    "FPS display");
                break;
            case Actions.ToggleUi:
                ExecuteToggle(
                    () => Program.DrawUI,
                    v => Program.DrawUI = v,
                    "UI visibility");
                break;

            // Simulation Control
            case Actions.SimSpeedIncrease:
                ExecuteGlobalAction(() => Universe.IncreaseSimulationSpeed(), "Simulation speed increase");
                break;
            case Actions.SimSpeedDecrease:
                ExecuteGlobalAction(() => Universe.DecreaseSimulationSpeed(), "Simulation speed decrease");
                break;
            case Actions.SimSpeedReset:
                ExecuteGlobalAction(() => Universe.ResetSimulationSpeed(), "Simulation speed reset");
                break;

            // Navigation Commands
            case Actions.SeekNextVehicle:
                ExecuteGlobalAction(() => Universe.SeekNextVehicle(SeekDirection.Forward), "Seek next vehicle");
                break;
            case Actions.SeekPrevVehicle:
                ExecuteGlobalAction(() => Universe.SeekNextVehicle(SeekDirection.Backward), "Seek previous vehicle");
                break;
            case Actions.SeekNextCelestial:
                ExecuteGlobalAction(() => Universe.SeekNextCelestial(SeekDirection.Forward), "Seek next celestial");
                break;
            case Actions.SeekPrevCelestial:
                ExecuteGlobalAction(() => Universe.SeekNextCelestial(SeekDirection.Backward), "Seek previous celestial");
                break;

            default:
                Logger.LogWarning("KSA OSC RPC: Unknown action '{Action}'", action);
                break;
        }
    }

    /// <summary>
    /// Sets a vehicle enum value with null-safety and logging.
    /// </summary>
    /// <param name="enumValue">The enum value to set (VehicleEngine, FlightComputerAction, etc.)</param>
    /// <param name="description">Human-readable description for logging</param>
    private void SetVehicleEnum(Enum enumValue, string description)
    {
        Program.ControlledVehicle?.SetEnum(enumValue);
        Logger.LogDebug("KSA OSC RPC: {Description} executed", description);
    }

    /// <summary>
    /// Executes a global game action with error handling and logging.
    /// </summary>
    /// <param name="action">The action to execute</param>
    /// <param name="description">Human-readable description for logging</param>
    private void ExecuteGlobalAction(Action action, string description)
    {
        try
        {
            action();
            Logger.LogDebug("KSA OSC RPC: {Description} executed", description);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "KSA OSC RPC: Failed to execute {Description}", description);
        }
    }

    /// <summary>
    /// Executes a toggle action (boolean property flip) with logging.
    /// </summary>
    /// <param name="getter">Function to get current value</param>
    /// <param name="setter">Action to set new value</param>
    /// <param name="description">Human-readable description for logging</param>
    private void ExecuteToggle(Func<bool> getter, Action<bool> setter, string description)
    {
        try
        {
            bool newValue = !getter();
            setter(newValue);
            Logger.LogDebug("KSA OSC RPC: {Description} toggled to {Value}", description, newValue);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "KSA OSC RPC: Failed to toggle {Description}", description);
        }
    }
}
