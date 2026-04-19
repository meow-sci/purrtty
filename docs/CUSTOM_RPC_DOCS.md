# caTTY Custom RPC Commands Documentation

Complete reference for OSC JSON RPC commands in caTTY terminal emulator for Kitten Space Agency (KSA).

## Table of Contents

1. [Introduction](#introduction)
2. [Quick Start](#quick-start)
3. [Command Reference](#command-reference)
   - [Engine Control](#engine-control)
   - [Flight Computer Actions](#flight-computer-actions)
   - [Attitude Profiles](#attitude-profiles)
   - [Attitude Track Targets](#attitude-track-targets)
   - [Vehicle Reference Frames](#vehicle-reference-frames)
   - [Flight Computer Modes](#flight-computer-modes)
   - [Global UI Commands](#global-ui-commands)
   - [Simulation Control](#simulation-control)
   - [Navigation Commands](#navigation-commands)
4. [Advanced Usage](#advanced-usage)
5. [Technical Details](#technical-details)
6. [Troubleshooting](#troubleshooting)
7. [Complete Command Reference Table](#complete-command-reference-table)

---

## Introduction

### What is OSC JSON RPC?

caTTY implements a custom RPC (Remote Procedure Call) mechanism using **OSC (Operating System Command) sequences** with JSON payloads. This allows you to control the KSA game engine directly from the terminal using simple escape sequences.

### Why OSC?

- **Windows ConPTY Compatible**: OSC sequences in the private-use range (1000+) pass through Windows ConPTY without being filtered, unlike DCS sequences
- **Simple Format**: Easy to generate from scripts, shells, and automation tools
- **JSON Payloads**: Human-readable, self-documenting command structure
- **Bi-directional**: Terminal can send commands and receive responses

### Command Format

All commands follow this format:

```
ESC ] 1010 ; {"action":"action_name"} BEL
```

Where:
- `ESC` = Escape character (ASCII 27 / 0x1B)
- `] 1010 ;` = OSC command identifier (1010 is the JSON action command)
- `{"action":"action_name"}` = JSON payload with action field
- `BEL` = Bell character (ASCII 7 / 0x07) - command terminator

### Prerequisites

- caTTY terminal must be running inside the KSA game mod
- Terminal must be visible (press F12 in KSA to toggle)
- Commands execute immediately upon receipt

---

## Quick Start

### Testing from PowerShell

```powershell
# Ignite main engine
Write-Host "$([char]27)]1010;{`"action`":`"engine_ignite`"}$([char]7)" -NoNewline

# Toggle FPS display
Write-Host "$([char]27)]1010;{`"action`":`"toggle_fps`"}$([char]7)" -NoNewline

# Set prograde tracking
Write-Host "$([char]27)]1010;{`"action`":`"fc_track_prograde`"}$([char]7)" -NoNewline
```

**Note**: PowerShell requires escaping quotes with backticks (`) inside double-quoted strings.

### Testing from Bash (Git Bash, WSL, etc.)

```bash
# Ignite main engine
echo -ne '\e]1010;{"action":"engine_ignite"}\a'

# Toggle FPS display
echo -ne '\e]1010;{"action":"toggle_fps"}\a'

# Set prograde tracking
echo -ne '\e]1010;{"action":"fc_track_prograde"}\a'
```

**Note**: Use `\e` for escape, `\a` for bell in bash escape sequences.

### Verifying Commands Work

1. **Game Behavior**: Watch for immediate effect in KSA (engine ignition, UI changes, etc.)
2. **Terminal Logs**: Check caTTY debug logs for "KSA OSC RPC: ... executed" messages
3. **No Error Messages**: If command is unknown, you'll see "Unknown action" warning in logs

### Common Issues

- **Nothing happens**: Ensure terminal is open (F12) and has focus
- **Syntax errors**: Check JSON formatting - quotes, commas, brackets
- **Command not recognized**: Verify action name matches exactly (case-sensitive)

---

## Command Reference

### Engine Control

Control main vehicle engine thrust.

#### engine_ignite

**Description**: Ignite the main throttle engine
**Requirements**: Active vehicle with main engine
**Category**: Engine Control

**JSON Payload**:
```json
{"action":"engine_ignite"}
```

**Shell Examples**:
```bash
# Bash
echo -ne '\e]1010;{"action":"engine_ignite"}\a'

# PowerShell
Write-Host "$([char]27)]1010;{`"action`":`"engine_ignite`"}$([char]7)" -NoNewline
```

---

#### engine_shutdown

**Description**: Shutdown the main engine
**Requirements**: Active vehicle with running engine
**Category**: Engine Control

**JSON Payload**:
```json
{"action":"engine_shutdown"}
```

**Shell Examples**:
```bash
# Bash
echo -ne '\e]1010;{"action":"engine_shutdown"}\a'

# PowerShell
Write-Host "$([char]27)]1010;{`"action`":`"engine_shutdown`"}$([char]7)" -NoNewline
```

---

### Flight Computer Actions

Manage flight computer burn scheduling and execution.

#### fc_delete_next_burn

**Description**: Delete the next planned burn from the flight computer
**Requirements**: Active vehicle with planned burn
**Category**: Flight Computer

**JSON Payload**:
```json
{"action":"fc_delete_next_burn"}
```

**Shell Examples**:
```bash
# Bash
echo -ne '\e]1010;{"action":"fc_delete_next_burn"}\a'
```

---

#### fc_warp_to_next_burn

**Description**: Time warp to the next planned burn
**Requirements**: Active vehicle with planned burn
**Category**: Flight Computer

**JSON Payload**:
```json
{"action":"fc_warp_to_next_burn"}
```

**Shell Examples**:
```bash
# Bash
echo -ne '\e]1010;{"action":"fc_warp_to_next_burn"}\a'
```

---

### Attitude Profiles

Configure flight computer attitude control precision.

#### fc_attitude_profile_strict

**Description**: Set strict attitude profile (high precision, slower response)
**Requirements**: Active vehicle
**Category**: Flight Computer

**JSON Payload**:
```json
{"action":"fc_attitude_profile_strict"}
```

---

#### fc_attitude_profile_balanced

**Description**: Set balanced attitude profile (medium precision and response)
**Requirements**: Active vehicle
**Category**: Flight Computer

**JSON Payload**:
```json
{"action":"fc_attitude_profile_balanced"}
```

---

#### fc_attitude_profile_relaxed

**Description**: Set relaxed attitude profile (faster response, lower precision)
**Requirements**: Active vehicle
**Category**: Flight Computer

**JSON Payload**:
```json
{"action":"fc_attitude_profile_relaxed"}
```

---

### Attitude Track Targets

Configure flight computer attitude tracking direction.

#### Available Track Targets

All track target commands follow the format `fc_track_{target}` where `{target}` is one of:

| Action Name | Description | KSA Enum Value |
|-------------|-------------|----------------|
| `fc_track_none` | Disable attitude tracking | None |
| `fc_track_custom` | Track custom direction | Custom |
| `fc_track_forward` | Track forward | Forward |
| `fc_track_backward` | Track backward | Backward |
| `fc_track_up` | Track up | Up |
| `fc_track_down` | Track down | Down |
| `fc_track_ahead` | Track ahead | Ahead |
| `fc_track_behind` | Track behind | Behind |
| `fc_track_radial_out` | Track radial out from celestial body | RadialOut |
| `fc_track_radial_in` | Track radial in toward celestial body | RadialIn |
| `fc_track_prograde` | Track prograde (velocity direction) | Prograde |
| `fc_track_retrograde` | Track retrograde (opposite velocity) | Retrograde |
| `fc_track_normal` | Track normal (orbit normal) | Normal |
| `fc_track_antinormal` | Track anti-normal (opposite orbit normal) | AntiNormal |
| `fc_track_outward` | Track outward | Outward |
| `fc_track_inward` | Track inward | Inward |
| `fc_track_positive_dv` | Track positive delta-V | PositiveDv |
| `fc_track_negative_dv` | Track negative delta-V | NegativeDv |
| `fc_track_toward` | Track toward target | Toward |
| `fc_track_away` | Track away from target | Away |
| `fc_track_antivel` | Track anti-velocity | Antivel |
| `fc_track_align` | Track alignment | Align |

**Example: Set Prograde Tracking**:
```bash
# Bash
echo -ne '\e]1010;{"action":"fc_track_prograde"}\a'

# PowerShell
Write-Host "$([char]27)]1010;{`"action`":`"fc_track_prograde`"}$([char]7)" -NoNewline
```

---

### Vehicle Reference Frames

Configure the vehicle's reference frame for attitude control.

#### Available Reference Frames

| Action Name | Description | KSA Enum Value |
|-------------|-------------|----------------|
| `vehicle_frame_ecl_body` | Ecliptic body-centered frame | EclBody |
| `vehicle_frame_enu_body` | East-North-Up body frame | EnuBody |
| `vehicle_frame_lvlh` | Local Vertical Local Horizontal | Lvlh |
| `vehicle_frame_vlf_body` | Velocity-Level-Forward body frame | VlfBody |
| `vehicle_frame_burn_body` | Burn-oriented body frame | BurnBody |
| `vehicle_frame_dock` | Docking/Target-relative frame | Dock |

**Example: Set LVLH Frame**:
```bash
# Bash
echo -ne '\e]1010;{"action":"vehicle_frame_lvlh"}\a'
```

---

### Flight Computer Modes

Configure flight computer operational modes.

#### Burn Mode

| Action Name | Description |
|-------------|-------------|
| `fc_burn_mode_manual` | Manual burn control |
| `fc_burn_mode_auto` | Automatic burn execution |

**Example**:
```bash
echo -ne '\e]1010;{"action":"fc_burn_mode_auto"}\a'
```

---

#### Roll Mode

| Action Name | Description |
|-------------|-------------|
| `fc_roll_mode_decoupled` | Roll decoupled from pitch/yaw |
| `fc_roll_mode_up` | Roll to keep "up" orientation |
| `fc_roll_mode_down` | Roll to keep "down" orientation |

**Example**:
```bash
echo -ne '\e]1010;{"action":"fc_roll_mode_up"}\a'
```

---

#### Attitude Mode

| Action Name | Description |
|-------------|-------------|
| `fc_attitude_mode_manual` | Manual attitude control |
| `fc_attitude_mode_auto` | Automatic attitude control |

**Example**:
```bash
echo -ne '\e]1010;{"action":"fc_attitude_mode_auto"}\a'
```

---

#### Manual Thrust Mode

| Action Name | Description |
|-------------|-------------|
| `fc_thrust_mode_direct` | Direct thrust control |
| `fc_thrust_mode_pulse` | Pulse thrust control (RCS-style) |

**Example**:
```bash
echo -ne '\e]1010;{"action":"fc_thrust_mode_pulse"}\a'
```

---

### Global UI Commands

Control game UI elements.

#### toggle_fps

**Description**: Toggle FPS (frames per second) display on/off
**Requirements**: None
**Category**: Global UI

**JSON Payload**:
```json
{"action":"toggle_fps"}
```

**Shell Example**:
```bash
echo -ne '\e]1010;{"action":"toggle_fps"}\a'
```

---

#### toggle_ui

**Description**: Toggle entire UI visibility on/off
**Requirements**: None
**Category**: Global UI

**JSON Payload**:
```json
{"action":"toggle_ui"}
```

**Shell Example**:
```bash
echo -ne '\e]1010;{"action":"toggle_ui"}\a'
```

---

### Simulation Control

Control time warp and simulation speed.

#### sim_speed_increase

**Description**: Increase simulation/time warp speed
**Requirements**: None (game will limit based on context)
**Category**: Simulation

**JSON Payload**:
```json
{"action":"sim_speed_increase"}
```

---

#### sim_speed_decrease

**Description**: Decrease simulation/time warp speed
**Requirements**: None
**Category**: Simulation

**JSON Payload**:
```json
{"action":"sim_speed_decrease"}
```

---

#### sim_speed_reset

**Description**: Reset simulation speed to real-time (1x)
**Requirements**: None
**Category**: Simulation

**JSON Payload**:
```json
{"action":"sim_speed_reset"}
```

**Shell Example**:
```bash
# Reset to real-time
echo -ne '\e]1010;{"action":"sim_speed_reset"}\a'
```

---

### Navigation Commands

Navigate between vehicles and celestial bodies.

#### seek_next_vehicle

**Description**: Switch focus to next vehicle in mission
**Requirements**: Multiple vehicles in current mission
**Category**: Navigation

**JSON Payload**:
```json
{"action":"seek_next_vehicle"}
```

---

#### seek_prev_vehicle

**Description**: Switch focus to previous vehicle in mission
**Requirements**: Multiple vehicles in current mission
**Category**: Navigation

**JSON Payload**:
```json
{"action":"seek_prev_vehicle"}
```

---

#### seek_next_celestial

**Description**: Focus on next celestial body
**Requirements**: None
**Category**: Navigation

**JSON Payload**:
```json
{"action":"seek_next_celestial"}
```

---

#### seek_prev_celestial

**Description**: Focus on previous celestial body
**Requirements**: None
**Category**: Navigation

**JSON Payload**:
```json
{"action":"seek_prev_celestial"}
```

**Shell Example**:
```bash
# Navigate to next celestial body
echo -ne '\e]1010;{"action":"seek_next_celestial"}\a'
```

---

## Advanced Usage

### PowerShell Functions

Create reusable functions for common operations:

```powershell
function Invoke-KsaCommand {
    param([string]$action)
    Write-Host "$([char]27)]1010;{`"action`":`"$action`"}$([char]7)" -NoNewline
}

# Usage
Invoke-KsaCommand "engine_ignite"
Invoke-KsaCommand "fc_track_prograde"
Invoke-KsaCommand "toggle_fps"
```

### Bash Functions

Create shell functions for cleaner scripts:

```bash
#!/bin/bash

ksa_cmd() {
    echo -ne "\e]1010;{\"action\":\"$1\"}\a"
}

# Usage
ksa_cmd "engine_ignite"
ksa_cmd "fc_track_prograde"
ksa_cmd "toggle_fps"
```

### Scripted Launch Sequence

```bash
#!/bin/bash

# Pre-launch setup
ksa_cmd "fc_attitude_mode_auto"
ksa_cmd "fc_track_prograde"
ksa_cmd "fc_attitude_profile_strict"

# Countdown
for i in 3 2 1; do
    echo "T-$i..."
    sleep 1
done

# Ignition!
ksa_cmd "engine_ignite"
echo "Liftoff!"
```

### Hotkey Integration (AutoHotkey Example)

```autohotkey
; Ignite engine with Ctrl+I
^i::
Send {Esc}]1010;{{}\"action\":\"engine_ignite\"{}}`
return

; Toggle FPS with Ctrl+F
^f::
Send {Esc}]1010;{{}\"action\":\"toggle_fps\"{}}`
return
```

### Creating Mission Profiles

```powershell
# Orbit insertion profile
function Start-OrbitInsertion {
    Invoke-KsaCommand "fc_attitude_profile_balanced"
    Invoke-KsaCommand "fc_track_prograde"
    Invoke-KsaCommand "vehicle_frame_lvlh"
    Invoke-KsaCommand "fc_burn_mode_auto"
    Invoke-KsaCommand "fc_attitude_mode_auto"
    Write-Host "Orbit insertion profile configured"
}

# Docking profile
function Start-DockingApproach {
    Invoke-KsaCommand "fc_attitude_profile_strict"
    Invoke-KsaCommand "vehicle_frame_dock"
    Invoke-KsaCommand "fc_thrust_mode_pulse"
    Invoke-KsaCommand "fc_attitude_mode_manual"
    Write-Host "Docking approach profile configured"
}
```

---

## Technical Details

### OSC Sequence Format

The complete OSC sequence structure:

```
<ESC> ] <command> ; <payload> <BEL>
```

Where:
- **ESC** (0x1B, 27): Escape character initiates sequence
- **]**: OSC sequence identifier
- **1010**: Private-use command number for JSON actions
- **;**: Separator
- **{"action":"..."}**: JSON payload (must be valid JSON)
- **BEL** (0x07, 7): Bell character terminates sequence

### OSC Private-Use Range

caTTY uses OSC command **1010** in the private-use range (1000+):
- Standard OSC commands: 0-999 (reserved by terminal standards)
- Private-use range: 1000+ (application-specific)
- caTTY JSON actions: 1010

This range passes through Windows ConPTY without filtering, unlike DCS (Device Control String) sequences which are often blocked.

### JSON Parsing

The handler performs these steps:
1. Extract JSON payload from OSC sequence
2. Parse JSON and validate structure
3. Extract `action` field (required)
4. Dispatch to appropriate handler based on action name
5. Execute game command or toggle setting
6. Log execution result

**Error Handling**:
- Invalid JSON: Warning logged, command ignored
- Missing `action` field: Warning logged, command ignored
- Unknown action: Warning logged, command ignored
- Null game context: No error, command silently skipped (null-safe)

### Null-Safety Behavior

All commands gracefully handle missing game context:
- **Vehicle commands**: No-op if `Program.ControlledVehicle` is null
- **Global commands**: Try-catch blocks prevent crashes
- **Toggle commands**: Check for null before accessing properties

This means commands never crash the terminal, even if sent at inappropriate times.

### Logging and Debugging

**Enable debug logging**:
1. Set logging level to Debug in caTTY configuration
2. Commands log: `"KSA OSC RPC: {Description} executed"`
3. Unknown actions log: `"KSA OSC RPC: Unknown action '{Action}'"`

**Tracing** (for deep debugging):
```csharp
// In caTTY.Core/Tracing/
TerminalTracer.Enabled = true;
```
This creates SQLite trace database at `%TEMP%\catty_trace.db` with all RPC events.

### Architecture Overview

```
Terminal Input
    ↓
OSC Parser (OscParser.cs)
    ↓
OSC Handler (OscHandler.cs)
    ↓
OSC RPC Handler (KsaOscRpcHandler.cs)
    ↓
JSON Parser (OscRpcHandler.cs base)
    ↓
Action Dispatcher (switch statement)
    ↓
Helper Methods (SetVehicleEnum, ExecuteGlobalAction, ExecuteToggle)
    ↓
KSA Game Engine (Program.ControlledVehicle, Universe, GameSettings)
```

For full architecture details, see [CLAUDE.md](CLAUDE.md) in the repository root.

### Extending with New Commands

To add new custom commands:

1. **Add action constant** in `KsaOscRpcHandler.Actions`:
   ```csharp
   public const string MyNewAction = "my_new_action";
   ```

2. **Add dispatch case** in `DispatchAction()`:
   ```csharp
   case Actions.MyNewAction:
       ExecuteGlobalAction(() => MyKsaFunction(), "My description");
       break;
   ```

3. **Add unit tests** in `KsaOscRpcHandlerTests.cs`:
   - Constant value test
   - Dispatch test (DoesNotThrow)

4. **Update this documentation** with new command details

---

## Troubleshooting

### Command Not Working

**Symptoms**: Nothing happens in game when command is sent

**Checks**:
1. Is caTTY terminal open? (Press F12 to toggle)
2. Is terminal window focused?
3. Is JSON syntax valid? (Use online JSON validator)
4. Is action name spelled correctly? (Case-sensitive!)
5. Check terminal logs for "Unknown action" warnings

**Solutions**:
- Ensure terminal is visible and has focus
- Validate JSON with: `echo '{"action":"engine_ignite"}' | jq .`
- Double-check action name against this documentation
- Try a known-working command (e.g., `toggle_fps`) to verify RPC system

---

### No Effect in Game

**Symptoms**: Command executes but no visible effect

**Possible Causes**:
1. **No active vehicle**: Vehicle commands require `Program.ControlledVehicle`
2. **Wrong mission context**: Some commands only work in specific scenarios
3. **Game state conflict**: Command contradicted by other game logic

**Solutions**:
- Ensure you have an active vehicle in flight
- Try global commands (toggle_fps, sim_speed) to verify RPC is working
- Check that command makes sense in current game state

---

### JSON Syntax Errors

**Symptoms**: Command fails to parse, "invalid JSON" in logs

**Common Mistakes**:
- Missing quotes around action name: `{action:"test"}` ❌ → `{"action":"test"}` ✅
- Single quotes instead of double: `{'action':'test'}` ❌ → `{"action":"test"}` ✅
- Missing closing brace: `{"action":"test"` ❌ → `{"action":"test"}` ✅
- Trailing comma: `{"action":"test",}` ❌ → `{"action":"test"}` ✅

**Solutions**:
- Always use double quotes for JSON strings
- Use online JSON validator to check syntax
- Copy-paste from working examples in this document

---

### Shell Encoding Issues

**Symptoms**: Strange characters appear, command malformed

**PowerShell**:
- Must escape quotes with backticks: `` `" `` not `\"`
- Must escape dollar signs in actions containing `$`
- Example: `` {`"action`":`"test`"} ``

**Bash**:
- Use single quotes for outer string: `'{"action":"test"}'`
- Or escape double quotes: `"{\"action\":\"test\"}"`
- Use `\e` for escape, `\a` for bell

**CMD**:
- CMD has limited escape sequence support
- Recommended: Use PowerShell or Bash instead

---

## Complete Command Reference Table

Quick reference table of all 53 available commands:

| Action Name | Category | Description |
|-------------|----------|-------------|
| `engine_ignite` | Engine | Ignite main throttle |
| `engine_shutdown` | Engine | Shutdown main engine |
| `fc_delete_next_burn` | Flight Computer | Delete next planned burn |
| `fc_warp_to_next_burn` | Flight Computer | Warp to next burn |
| `fc_attitude_profile_strict` | Attitude | Strict attitude profile |
| `fc_attitude_profile_balanced` | Attitude | Balanced attitude profile |
| `fc_attitude_profile_relaxed` | Attitude | Relaxed attitude profile |
| `fc_track_none` | Tracking | Disable tracking |
| `fc_track_custom` | Tracking | Custom direction |
| `fc_track_forward` | Tracking | Track forward |
| `fc_track_backward` | Tracking | Track backward |
| `fc_track_up` | Tracking | Track up |
| `fc_track_down` | Tracking | Track down |
| `fc_track_ahead` | Tracking | Track ahead |
| `fc_track_behind` | Tracking | Track behind |
| `fc_track_radial_out` | Tracking | Track radial out |
| `fc_track_radial_in` | Tracking | Track radial in |
| `fc_track_prograde` | Tracking | Track prograde |
| `fc_track_retrograde` | Tracking | Track retrograde |
| `fc_track_normal` | Tracking | Track normal |
| `fc_track_antinormal` | Tracking | Track anti-normal |
| `fc_track_outward` | Tracking | Track outward |
| `fc_track_inward` | Tracking | Track inward |
| `fc_track_positive_dv` | Tracking | Track positive delta-V |
| `fc_track_negative_dv` | Tracking | Track negative delta-V |
| `fc_track_toward` | Tracking | Track toward target |
| `fc_track_away` | Tracking | Track away from target |
| `fc_track_antivel` | Tracking | Track anti-velocity |
| `fc_track_align` | Tracking | Track alignment |
| `vehicle_frame_ecl_body` | Reference Frame | Ecliptic body frame |
| `vehicle_frame_enu_body` | Reference Frame | ENU body frame |
| `vehicle_frame_lvlh` | Reference Frame | LVLH frame |
| `vehicle_frame_vlf_body` | Reference Frame | VLF body frame |
| `vehicle_frame_burn_body` | Reference Frame | Burn body frame |
| `vehicle_frame_dock` | Reference Frame | Docking frame |
| `fc_burn_mode_manual` | Mode | Manual burn mode |
| `fc_burn_mode_auto` | Mode | Auto burn mode |
| `fc_roll_mode_decoupled` | Mode | Decoupled roll |
| `fc_roll_mode_up` | Mode | Roll up |
| `fc_roll_mode_down` | Mode | Roll down |
| `fc_attitude_mode_manual` | Mode | Manual attitude |
| `fc_attitude_mode_auto` | Mode | Auto attitude |
| `fc_thrust_mode_direct` | Mode | Direct thrust |
| `fc_thrust_mode_pulse` | Mode | Pulse thrust |
| `toggle_fps` | UI | Toggle FPS display |
| `toggle_ui` | UI | Toggle UI visibility |
| `sim_speed_increase` | Simulation | Increase time warp |
| `sim_speed_decrease` | Simulation | Decrease time warp |
| `sim_speed_reset` | Simulation | Reset to real-time |
| `seek_next_vehicle` | Navigation | Next vehicle |
| `seek_prev_vehicle` | Navigation | Previous vehicle |
| `seek_next_celestial` | Navigation | Next celestial |
| `seek_prev_celestial` | Navigation | Previous celestial |

---

## Additional Resources

- **Architecture Documentation**: See [CLAUDE.md](CLAUDE.md) for full system architecture
- **Source Code**: Browse `caTTY.TermSequenceRpc/KsaOscRpcHandler.cs` for implementation
- **Test Examples**: See `caTTY.TermSequenceRpc.Tests/Unit/KsaOscRpcHandlerTests.cs` for usage patterns
- **RPC Infrastructure**: See `caTTY.Core/Rpc/OscRpcHandler.cs` for base implementation

---

**Last Updated**: 2026-01-08
**Version**: 1.0
**Total Commands**: 53
