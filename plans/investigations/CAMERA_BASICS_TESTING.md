# Camera Basics Manual Testing Checklist

**Feature**: Camera Basics Panel - Manual camera control and follow target management  
**Component**: `caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs`  
**Parent UI**: `caTTY.SkunkworksGameMod/UI/CameraDebugPanel.cs`  
**Service**: `caTTY.SkunkworksGameMod/Camera/KsaCameraService.cs`  
**Date Created**: 2026-01-25

## Overview

This document provides manual testing procedures for the camera basics feature, which allows users to control camera modes, movement, and follow target behavior through the Skunkworks debug UI.

## Test Environment Setup

1. Build the solution: `dotnet build caTTY.SkunkworksGameMod`
2. Deploy the mod to KSA mods folder
3. Launch Kitten Space Agency
4. Press **F11** to open the Skunkworks debug window
5. Verify the **Camera Basics** collapsing header is visible and expanded by default

## Testing Scenarios

### 1. Camera Mode Switching

**Purpose**: Verify all three camera modes can be activated and switched between seamlessly.

- [ ] **Initial State**:
  - Start game with camera following a craft
  - Open Skunkworks window (F11)
  - Verify "Current Mode" shows "Following"
  - Verify "Manual Follow Mode" and "Free Camera Mode" buttons are enabled
  - Verify "Native Follow Mode" button is disabled (already in native mode)

- [ ] **Switch to Free Camera**:
  - Click "Free Camera Mode" button
  - Verify "Current Mode" updates to "Free"
  - Verify console shows: `[CameraBasicsPanel] Switched to free camera mode`
  - Verify console shows: `[KsaCameraService] Entered free camera mode`
  - Verify "Free Camera Mode" button is now disabled
  - Verify "Native Follow Mode" and "Manual Follow Mode" buttons are enabled

- [ ] **Switch to Manual Follow**:
  - Click "Manual Follow Mode" button
  - Verify "Current Mode" updates to "Manual"
  - Verify console shows mode change messages
  - Verify "Manual Follow Mode" button is disabled
  - Verify other mode buttons are enabled

- [ ] **Switch to Native Follow**:
  - Click "Native Follow Mode" button
  - Verify "Current Mode" updates to "Following"
  - Verify camera re-follows the craft with native KSA behavior
  - Verify console shows: `[CameraBasicsPanel] Switched to native follow mode`
  - Verify console shows: `[KsaCameraService] Started following in native KSA mode`

- [ ] **Multiple Mode Switches**:
  - Switch between all three modes in different orders (6+ iterations)
  - Verify each transition works correctly
  - Verify button enable/disable states update properly
  - Verify no console errors or exceptions

**Expected Console Output**:
```
[CameraBasicsPanel] Switched to free camera mode
[KsaCameraService] Entered free camera mode
[CameraBasicsPanel] Switched to manual follow mode
[KsaCameraService] Started following in manual mode at distance 150.0m
[CameraBasicsPanel] Switched to native follow mode
[KsaCameraService] Started following in native KSA mode
```

---

### 2. Camera-Relative Movement

**Purpose**: Verify camera moves correctly relative to its current orientation.

- [ ] **Setup**:
  - Enter Free Camera mode
  - Note initial camera position (visible in Camera Info section)
  - Set move distance slider to default (50m)

- [ ] **Forward/Backward Movement**:
  - Click "Forward" button
  - Verify console shows: `[CameraBasics] Moved forward by 50m`
  - Verify camera moved forward in the direction it's facing
  - Click "Back" button
  - Verify console shows: `[CameraBasics] Moved back by 50m`
  - Verify camera moved backward (opposite of facing direction)

- [ ] **Left/Right Movement**:
  - Click "Left" button
  - Verify console shows: `[CameraBasics] Moved left by 50m`
  - Verify camera moved left relative to current view
  - Click "Right" button
  - Verify console shows: `[CameraBasics] Moved right by 50m`
  - Verify camera moved right relative to current view

- [ ] **Up/Down Movement**:
  - Click "Up" button
  - Verify console shows: `[CameraBasics] Moved up by 50m`
  - Verify camera moved up relative to current orientation
  - Click "Down" button
  - Verify console shows: `[CameraBasics] Moved down by 50m`
  - Verify camera moved down relative to current orientation

- [ ] **Variable Distance**:
  - Change move distance slider to 10m
  - Test a directional button (e.g., Forward)
  - Verify console shows: `[CameraBasics] Moved forward by 10m`
  - Verify movement is noticeably smaller
  - Change slider to 200m
  - Test same directional button
  - Verify console shows: `[CameraBasics] Moved forward by 200m`
  - Verify movement is noticeably larger

- [ ] **Orientation-Relative Behavior**:
  - Use Camera Rotation sliders to rotate camera significantly (e.g., yaw 90°)
  - Test "Forward" button
  - Verify forward is relative to new orientation (not world axes)
  - Test "Right" button
  - Verify right is relative to new orientation
  - Rotate camera 180°
  - Verify "Forward" now moves in opposite world direction

**Expected Console Output**:
```
[CameraBasics] Moved forward by 50m
[CameraBasics] Moved back by 50m
[CameraBasics] Moved left by 50m
[CameraBasics] Moved right by 50m
[CameraBasics] Moved up by 50m
[CameraBasics] Moved down by 50m
[CameraBasics] Moved forward by 10m
[CameraBasics] Moved forward by 200m
```

---

### 3. World-Space Movement

**Purpose**: Verify ECL axis movement is consistent regardless of camera orientation.

- [ ] **Setup**:
  - Enter Free Camera mode
  - Note initial camera position in Camera Info
  - Rotate camera to arbitrary orientation

- [ ] **X-Axis Movement**:
  - Click "+X" button
  - Verify console shows: `[CameraBasics] Moved +X by 100m`
  - Verify camera moved in +X world direction
  - Rotate camera 90° yaw
  - Click "+X" again
  - Verify movement is still in same world direction (not relative to camera)
  - Click "-X" button
  - Verify console shows: `[CameraBasics] Moved -X by 100m`
  - Verify movement is in opposite world direction

- [ ] **Y-Axis Movement**:
  - Click "+Y" button
  - Verify console shows: `[CameraBasics] Moved +Y by 100m`
  - Verify movement is consistent with ECL Y-axis
  - Click "-Y" button
  - Verify console shows: `[CameraBasics] Moved -Y by 100m`

- [ ] **Z-Axis Movement (Vertical)**:
  - Click "+Z" button
  - Verify console shows: `[CameraBasics] Moved +Z by 100m`
  - Verify camera moved "up" in world space (true vertical)
  - Rotate camera to look sideways or upside down
  - Click "+Z" again
  - Verify movement is still world-vertical (not camera-relative)
  - Click "-Z" button
  - Verify console shows: `[CameraBasics] Moved -Z by 100m`
  - Verify camera moved "down" in world space

- [ ] **Consistency Test**:
  - Perform complete camera rotation (360° yaw, changing pitch)
  - Test each axis button at different orientations
  - Verify all movements remain consistent with world axes
  - Verify no movement is affected by camera orientation

**Expected Console Output**:
```
[CameraBasics] Moved +X by 100m
[CameraBasics] Moved -X by 100m
[CameraBasics] Moved +Y by 100m
[CameraBasics] Moved -Y by 100m
[CameraBasics] Moved +Z by 100m
[CameraBasics] Moved -Z by 100m
```

---

### 4. Follow Target Interaction

**Purpose**: Verify follow target controls work correctly across different camera modes.

- [ ] **Following Mode Movement**:
  - Ensure camera is in Native Follow mode with active follow target
  - Try clicking movement buttons (Forward, Back, etc.)
  - Verify buttons have no effect (follow mode overrides manual movement)
  - Observe follow behavior continues normally

- [ ] **Free Camera Snap Back**:
  - Start in Following mode on a craft
  - Switch to Free Camera mode
  - Move camera far away using multiple movement commands
  - Verify "Snap to Follow Target" button becomes enabled
  - Click "Snap to Follow Target"
  - Verify console shows: `[CameraBasics] Snapped camera to follow target position`
  - Verify camera instantly returns to original follow target location
  - Verify camera orientation may change to face target

- [ ] **No Follow Target State**:
  - Clear follow target (if possible via UI)
  - OR start game in free camera mode with no initial target
  - Verify "Snap to Follow Target" button is disabled
  - Verify all movement buttons still work
  - Verify mode switching works (Free ↔ Manual, but Native Follow may not be meaningful)

- [ ] **Follow Target Dropdown**:
  - Open the follow target dropdown
  - Verify list of available craft/objects
  - Select a different target
  - Click "Start Following" button
  - Verify camera switches to following new target
  - Verify "Current Mode" updates appropriately

**Expected Console Output**:
```
[CameraBasics] Started following target: [VesselName]
[KsaCameraService] Started following target at distance 150.0m
[CameraBasics] Stopped following current target
[KsaCameraService] Stopped following, entering free camera mode
[CameraBasics] Snapped camera to follow target position
```

---

### 5. Camera Rotation Controls

**Purpose**: Verify camera rotation sliders and reset functions work correctly.

- [ ] **Yaw Rotation**:
  - Enter Free Camera mode
  - Adjust Yaw slider from 0° to 90°
  - Verify camera rotates around vertical axis
  - Verify camera view changes accordingly
  - Adjust to -90°
  - Verify rotation in opposite direction

- [ ] **Pitch Rotation**:
  - Adjust Pitch slider from 0° to 45°
  - Verify camera tilts up
  - Adjust to -45°
  - Verify camera tilts down

- [ ] **Roll Rotation**:
  - Adjust Roll slider from 0° to 30°
  - Verify camera rolls (horizon tilts)

- [ ] **Look At Target**:
  - With follow target selected
  - Move camera away or rotate arbitrarily
  - Click "Look At" button
  - Verify camera rotates to face the follow target
  - Verify position doesn't change (only orientation)

- [ ] **Reset Rotation**:
  - Rotate camera to arbitrary angles (yaw/pitch/roll)
  - Click "Reset Rotation" button
  - Verify all rotation sliders return to 0°
  - Verify camera orientation resets to default

**Expected Console Output**:
```
[CameraBasics] Camera rotation set to yaw=90.0°, pitch=45.0°, roll=0.0°
[CameraBasics] Camera looking at follow target
[CameraBasics] Camera rotation reset to default
```

---

### 6. UI Integration

**Purpose**: Verify the Camera Basics panel integrates correctly with the main debug UI.

- [ ] **Collapsing Header Independence**:
  - Verify "Camera Basics" header is expanded by default on first open
  - Click to collapse "Camera Basics"
  - Verify content disappears
  - Expand "Orbit Animation" section
  - Verify both sections operate independently
  - Collapse one, expand the other in various combinations

- [ ] **Default States**:
  - Close and reopen Skunkworks window (F11)
  - Verify "Camera Basics" is expanded by default
  - Verify "Orbit Animation" is collapsed by default

- [ ] **Orbit Animation Compatibility**:
  - With "Camera Basics" collapsed, test orbit animation features
  - Start an orbit animation
  - Verify animation runs correctly
  - Expand "Camera Basics" during animation
  - Verify animation continues running
  - Try switching camera modes during animation
  - Observe interaction behavior

- [ ] **Spacing and Layout**:
  - Verify proper spacing between sections
  - Verify no overlapping UI elements
  - Verify subsection separators in Orbit Animation work correctly
  - Verify all buttons and sliders are readable and accessible

- [ ] **Camera Info Display**:
  - Verify "Camera Info" section (non-collapsing text separator) displays correctly
  - Verify it shows current position, FOV, and follow target status
  - Move camera and verify values update in real-time

**Expected Behavior**:
- Clean, organized UI layout
- No visual glitches or overlapping text
- All interactive elements respond correctly
- Console output is readable and informative

---

### 7. Edge Cases and Error Conditions

**Purpose**: Identify and test boundary conditions and potential failure modes.

- [ ] **No Follow Target Scenarios**:
  - Start game with no craft spawned (if possible)
  - Open Skunkworks window
  - Verify "No follow target" or similar message displays
  - Verify "Snap to Follow Target" is disabled
  - Verify "Native Follow Mode" button behavior (may be disabled)
  - Verify movement buttons still work in Free Camera mode

- [ ] **Rapid Mode Switching**:
  - Rapidly click mode buttons back and forth
  - Switch modes 10+ times quickly
  - Verify no crashes or hung states
  - Verify console output completes for each switch
  - Verify camera state remains consistent

- [ ] **Extreme Movement Distances**:
  - Set move distance slider to maximum (500m or higher if possible)
  - Test directional movements
  - Verify camera doesn't glitch or teleport to invalid positions
  - Verify large movements are smooth (or instant, depending on implementation)

- [ ] **Extreme Rotation Angles**:
  - Set rotation sliders to extremes (e.g., yaw 180°, pitch 89°, roll 180°)
  - Verify camera doesn't encounter gimbal lock
  - Verify movement directions remain correct relative to orientation
  - Test "Reset Rotation" works from extreme angles

- [ ] **Mode Switching During Animation**:
  - Start an orbit animation (from Orbit Animation section)
  - While animation is playing, click "Free Camera Mode"
  - Verify animation stops or behavior is documented
  - Try switching to Manual Follow mode during animation
  - Verify consistent behavior across mode changes

- [ ] **Mode Switching During Movement**:
  - In Free Camera mode, click a movement button
  - Immediately (during movement execution) switch to following mode
  - Verify no crashes or inconsistent states
  - Verify mode switch completes correctly

- [ ] **Follow Target Destruction**:
  - Start following a craft
  - Destroy or despawn the craft (if possible in test environment)
  - Verify camera handles loss of follow target gracefully
  - Verify no null reference exceptions in console
  - Verify camera switches to Free mode or displays appropriate message

- [ ] **Window Resize and Re-open**:
  - Resize Skunkworks window to very small size
  - Verify UI elements don't break or overlap
  - Close window (F11) and reopen
  - Verify all settings persist or reset to defaults appropriately
  - Verify camera mode is maintained or reset as expected

- [ ] **Concurrent Movement Commands**:
  - Rapidly click multiple movement buttons (Forward, then Right, then Up)
  - Verify all movements execute or queue properly
  - Verify no movement commands are lost
  - Verify console output matches expected number of movements

- [ ] **Rotation + Movement Interaction**:
  - Adjust rotation sliders while simultaneously clicking movement buttons
  - Verify movements use current rotation state
  - Verify no race conditions or inconsistent movement directions

**Expected Behavior**:
- No crashes or exceptions under any condition
- Graceful handling of invalid states (disabled buttons, error messages)
- Consistent console logging for all actions
- Clear user feedback for problematic operations

---

## Expected Console Output Reference

### Mode Switching
```
[CameraBasicsPanel] Switched to free camera mode
[KsaCameraService] Entered free camera mode
[CameraBasicsPanel] Switched to manual follow mode
[KsaCameraService] Started following in manual mode at distance 150.0m
[CameraBasicsPanel] Switched to native follow mode
[KsaCameraService] Started following in native KSA mode
```

### Camera-Relative Movement
```
[CameraBasics] Moved forward by 50m
[CameraBasics] Moved back by 50m
[CameraBasics] Moved left by 50m
[CameraBasics] Moved right by 50m
[CameraBasics] Moved up by 50m
[CameraBasics] Moved down by 50m
```

### World-Space Movement
```
[CameraBasics] Moved +X by 100m
[CameraBasics] Moved -X by 100m
[CameraBasics] Moved +Y by 100m
[CameraBasics] Moved -Y by 100m
[CameraBasics] Moved +Z by 100m
[CameraBasics] Moved -Z by 100m
```

### Follow Target Operations
```
[CameraBasics] Started following target: [VesselName]
[KsaCameraService] Started following target at distance 150.0m
[CameraBasics] Stopped following current target
[KsaCameraService] Stopped following, entering free camera mode
[CameraBasics] Snapped camera to follow target position
[CameraBasics] Cleared follow target
```

### Rotation Operations
```
[CameraBasics] Camera rotation set to yaw=90.0°, pitch=45.0°, roll=0.0°
[CameraBasics] Camera looking at follow target
[CameraBasics] Camera rotation reset to default
```

### Error Conditions
```
[CameraBasics] Warning: No follow target available
[CameraBasics] Error: Cannot snap to target - no target selected
[KsaCameraService] Warning: Follow target lost, switching to free camera mode
```

---

## Acceptance Criteria

- [ ] All test scenarios above are documented with checkboxes
- [ ] Expected behaviors are clearly described for each test
- [ ] Console output patterns are documented with examples
- [ ] Edge cases and error conditions are identified and testable
- [ ] Testing checklist can be used by QA or other developers
- [ ] Document is comprehensive enough to catch regression bugs

---

## Additional Test Scenarios (Implementation-Specific)

Based on the code implementation, these additional scenarios should be tested:

### Distance-Based Snap Behavior
- [ ] Verify "Snap to Follow Target" button enable logic:
  - When camera is close to target (< threshold), button should be disabled
  - When camera moves far from target (> threshold), button should enable
  - Test the threshold boundary (exact distance may vary)

### Follow Distance Control
- [ ] If follow distance slider is present:
  - Verify slider adjusts distance in Manual Follow mode
  - Verify distance changes are smooth
  - Test minimum and maximum distance values
  - Verify Native Follow mode may ignore manual distance setting

### Rotation Slider Ranges
- [ ] Verify rotation slider limits:
  - Yaw: typically -180° to +180°
  - Pitch: typically -89° to +89° (prevent gimbal lock)
  - Roll: typically -180° to +180°
  - Verify sliders can't exceed these ranges

### Button Disabling Logic
- [ ] Verify correct button states in each mode:
  - **Free Camera**: Movement buttons enabled, current mode button disabled
  - **Manual Follow**: Movement buttons behavior (may be enabled with modified behavior)
  - **Native Follow**: Movement buttons disabled or ignored
  - Verify dropdown and follow control buttons update appropriately

---

## Notes for Testers

1. **Console Monitoring**: Keep the game console visible (or check logs) to verify expected output
2. **Visual Verification**: Some behaviors require visual confirmation of camera movement/rotation
3. **Timing**: Some movements may be animated; allow time for completion before next action
4. **Reproducibility**: If a bug is found, document exact steps to reproduce
5. **Cross-Feature Testing**: Test camera basics alongside orbit animation to verify integration
6. **Performance**: Monitor for frame rate drops or stuttering during camera operations

---

## Known Limitations

Document any known limitations discovered during implementation:

- Manual Follow mode behavior may differ from Native Follow (by design)
- Extreme camera distances may cause rendering issues (game engine limitation)
- ECL coordinate system orientation depends on game world setup
- Follow target list may be limited to certain object types (craft only, not celestial bodies)

---

## Test Completion Sign-Off

When all tests are completed, fill in:

- **Tester Name**: ___________________________
- **Test Date**: ___________________________
- **Build Version**: ___________________________
- **Total Tests**: _________  **Passed**: _________  **Failed**: _________
- **Critical Issues Found**: ___________________________
- **Notes**: ___________________________

