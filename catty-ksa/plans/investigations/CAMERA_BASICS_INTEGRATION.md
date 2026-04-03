# Camera Basics Integration Test Plan

**Feature**: Camera Basics Panel Integration with Existing Systems  
**Related Components**: 
- `caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs`
- `caTTY.SkunkworksGameMod/Camera/KsaCameraService.cs`
- `caTTY.SkunkworksGameMod/Camera/Animation/CameraAnimationPlayer.cs`
- `caTTY.SkunkworksGameMod/Rpc/Actions/CameraOrbitRpcAction.cs`
- `caTTY.SkunkworksGameMod/SkunkworksMod.cs`

**Date Created**: 2026-01-25

## Overview

This document outlines integration testing procedures for the Camera Basics feature with existing systems including orbit animation, manual follow mode, and RPC commands. It identifies potential issues at integration boundaries and provides mitigation strategies.

---

## Integration Points

### 1. Integration with Orbit Animation

**Location**: `CameraDebugPanel` uses both `CameraAnimationPlayer` and `CameraBasicsPanel`

**Key Interactions**:
- Orbit animation calls `_cameraService.StartManualFollow(double3.Zero)` when starting
- Animation updates camera position each frame via `_cameraService.Position = targetPos + frame.Offset`
- Animation calls `_cameraService.UpdateFollowOffset(frame.Offset)` to keep offset synchronized
- On animation end, `SkunkworksMod` calls `_cameraService.StopManualFollow()` to restore state
- Camera Basics panel can switch modes while animation is running

**State Variables Involved**:
- `_isManualFollowing` (KsaCameraService)
- `_followedObject` (KsaCameraService)
- `_followOffset` (KsaCameraService)
- `IsPlaying` (CameraAnimationPlayer)
- `_wasAnimationPlaying` (SkunkworksMod)

**Expected Behavior**:
- **During Animation**: Camera should be in manual follow mode with animation controlling offset
- **Animation End**: Camera should smoothly transition to keeping current offset without snapping
- **Mode Switch During Animation**: Switching modes should stop animation gracefully

### 2. Integration with Manual Follow Mode

**Location**: `KsaCameraService.StartManualFollow()` / `ExitManualFollow()` / `StopManualFollow()`

**Key Interactions**:
- `StartManualFollow(offset)`: Captures current follow target, calls `camera.Unfollow()`, sets `_isManualFollowing = true`
- `Update(deltaTime)`: Updates camera position each frame when `_isManualFollowing == true`
- `StopManualFollow()`: Does NOT call `SetFollow()` - intentionally keeps camera following at current offset
- `ExitManualFollow(RestoreNativeFollow)`: Attempts to restore KSA native follow via reflection
- Camera Basics panel provides UI buttons for all manual follow operations

**State Transitions**:
```
Native Follow → StartManualFollow() → Manual Follow
Manual Follow → StopManualFollow() → Manual Follow (unchanged, offset preserved)
Manual Follow → ExitManualFollow(KeepOffset) → Manual Follow (unchanged)
Manual Follow → ExitManualFollow(RestoreNative) → Native Follow (may snap)
Manual Follow → EnterFreeCameraMode() → Free Camera
```

**Expected Behavior**:
- Manual follow should maintain smooth tracking without position jumps
- `StopManualFollow()` should NOT trigger KSA's SetFollow (avoids snapping)
- `ExitManualFollow(RestoreNativeFollow)` should only be used when user explicitly requests native behavior
- Switching to Free Camera should clear manual follow state

### 3. Integration with RPC System

**Location**: `CameraOrbitRpcAction` invokes camera service methods via RPC

**Key RPC Actions**:
- **camera-orbit**: JSON RPC action that generates orbit animation and starts playback
  - Calls `_cameraService.StartManualFollow(double3.Zero)` 
  - Loads keyframes into `_animationPlayer`
  - Starts animation playback

**RPC → Camera Service Call Chain**:
```
Terminal OSC Command
  ↓
CameraOrbitRpcAction.Execute()
  ↓
_cameraService.StartManualFollow(double3.Zero)
_animationPlayer.SetKeyframes()
_animationPlayer.Play()
```

**State Consistency Checks**:
- RPC action checks `_cameraService.IsAvailable` before operations
- RPC action checks `_cameraService.FollowTarget != null` before orbit
- RPC action captures original camera state before starting animation
- Animation player state (`IsPlaying`) must align with camera service state

**Expected Behavior**:
- RPC commands should fail gracefully if camera unavailable
- RPC commands should fail gracefully if no follow target exists
- Multiple RPC commands should not corrupt camera service state
- Animation started via RPC should behave identically to UI-initiated animation

### 4. State Consistency Management

**Critical State Variables** (all in `KsaCameraService`):

| Variable | Type | Purpose | Set By | Cleared By |
|----------|------|---------|--------|------------|
| `_isManualFollowing` | bool | Manual follow active flag | `StartManualFollow()` | `EnterFreeCameraMode()`, `StartFollowing()`, `ExitManualFollow(RestoreNative)` |
| `_followedObject` | dynamic? | Current follow target | `StartManualFollow()` | `EnterFreeCameraMode()`, `StartFollowing()`, `ExitManualFollow(RestoreNative)` |
| `_followOffset` | double3 | Offset from target | `StartManualFollow()`, `UpdateFollowOffset()` | `EnterFreeCameraMode()`, `StartFollowing()`, `ExitManualFollow(RestoreNative)` |
| `_camera` | Camera? | Cached KSA camera | `GetCamera()` reflection call | Never (cached for lifetime) |

**Property Synchronization**:
- `FollowTarget` property: Returns `_followedObject` if `_isManualFollowing`, else returns `camera.Following`
- `IsFollowing` property: Returns `false` if `_isManualFollowing` (manual != native), else checks `camera.Following != null`
- `Position` property: Direct get/set on `camera.PositionEcl`
- `Rotation` property: Direct get/set on `camera.WorldRotation`

**Consistency Invariants**:
1. If `_isManualFollowing == true`, then `_followedObject != null` and `camera.Following == null`
2. If `_isManualFollowing == false`, then animation system should not be updating camera
3. `_followOffset` should always be synchronized with animation frame offset during playback
4. `EnterFreeCameraMode()` MUST clear all manual follow state
5. `StartFollowing()` MUST clear all manual follow state

---

## Potential Issues and Mitigation Strategies

### Issue 1: Mode Conflicts Between Animation and Manual Controls

**Problem**: User switches camera mode while orbit animation is playing

**Scenarios**:
- User clicks "Free Camera" during orbit → animation continues but camera no longer follows
- User clicks "Native Follow" during orbit → KSA's follow offset overrides animation
- User starts new orbit while previous orbit is running → state corruption possible

**Mitigation Strategies**:
✅ **Implemented**: `EnterFreeCameraMode()` clears `_isManualFollowing` state
✅ **Implemented**: `StartFollowing()` clears `_isManualFollowing` state
❌ **Missing**: Animation player does not stop when mode switches occur

**Recommended Fix**:
```csharp
// In CameraBasicsPanel.RenderCameraModeSection()
if (ImGui.Button("Free Camera"))
{
    if (_animationPlayer?.IsPlaying == true)
    {
        _animationPlayer.Stop();
        Console.WriteLine("[CameraBasicsPanel] Stopped animation due to mode switch");
    }
    _cameraService.EnterFreeCameraMode();
    Console.WriteLine("[CameraBasicsPanel] Switched to free camera mode");
}
```

**Test Scenarios**:
1. Start orbit animation → Switch to Free Camera → Verify animation stops
2. Start orbit animation → Switch to Native Follow → Verify animation stops
3. Start orbit animation → Click "Enter Manual Follow" → Verify graceful handling
4. Start orbit animation → Start second orbit → Verify first stops cleanly

### Issue 2: State Leaks from Rapid Mode Switching

**Problem**: Rapidly clicking mode buttons may leave inconsistent state

**Scenarios**:
- Click "Manual Follow" → Immediately click "Free Camera" → `_followedObject` may not clear properly
- Click "Native Follow" → Immediately click "Manual Follow" → Race condition in SetFollow vs Unfollow
- Spam "Enter Manual Follow" button → Multiple manual follow activations

**Mitigation Strategies**:
✅ **Implemented**: Each mode entry method clears previous mode state
✅ **Implemented**: Mode buttons are disabled based on current mode
⚠️ **Partial**: Button disabling not enforced during transitions

**Recommended Fix**:
- Add debouncing to mode switch buttons (minimum 100ms between clicks)
- Add `_isModeTransitioning` flag during mode changes
- Disable all mode buttons while `_isModeTransitioning == true`

**Test Scenarios**:
1. Rapidly alternate between Free Camera and Native Follow (10 times in 2 seconds)
2. Spam "Enter Manual Follow" button repeatedly
3. Hold down key that triggers mode switch (if keyboard shortcut exists)
4. Verify `_isManualFollowing`, `_followedObject`, `_followOffset` consistency after spam

### Issue 3: Position Snapping When Transitioning Between Modes

**Problem**: Camera position jumps when switching from manual follow to native follow

**Root Cause**: KSA's `SetFollow()` applies its own default offset, overriding current position

**Scenarios**:
- Manual follow at custom offset → Switch to Native Follow → Camera snaps to KSA's default offset
- Animation ends at custom offset → `ExitManualFollow(RestoreNative)` → Camera snaps
- User manually moves camera → Enter Manual Follow → Exit to Native → Position lost

**Mitigation Strategies**:
✅ **Implemented**: `StopManualFollow()` does NOT call `SetFollow()` - preserves offset
✅ **Implemented**: `ExitManualFollow()` has two modes: `KeepCurrentOffset` and `RestoreNativeFollow`
✅ **Implemented**: UI buttons clearly labeled for expected behavior ("Exit Manual Follow (keep offset)" vs "Exit Manual Follow (restore native)")
✅ **Implemented**: Console warnings explain potential snapping behavior

**Current Design (Correct)**:
- Default behavior (`StopManualFollow()`) = no snapping, camera stays at current offset
- Explicit user action required to trigger native SetFollow (via "restore native" button)
- Animation system uses `StopManualFollow()` to avoid snapping on animation end

**Test Scenarios**:
1. Enter manual follow → Move camera 500m away → Click "Exit (keep offset)" → Verify no snap
2. Enter manual follow → Move camera 500m away → Click "Exit (restore native)" → Verify snap to default offset
3. Start orbit animation → Let it complete → Verify no snapping on animation end
4. Start orbit animation → Switch to Free Camera mid-animation → Verify smooth transition

### Issue 4: Console Spam from Per-Frame Updates

**Problem**: Original implementation logged every frame update, causing massive console spam

**Root Cause**: `Update(deltaTime)` called every frame (~60 FPS) with console output

**Mitigation Strategies**:
✅ **Implemented**: Removed per-frame logging from `KsaCameraService.Update()`
✅ **Implemented**: Removed per-frame logging from `UpdateFollowOffset()`
⚠️ **Potential**: Animation frame updates still log to console during playback

**Original Code (Removed)**:
```csharp
// OLD - caused spam:
public void UpdateFollowOffset(double3 offset)
{
    if (_isManualFollowing)
    {
        _followOffset = offset;
        Console.WriteLine($"[CameraService] Updated offset: {offset}"); // SPAM!
    }
}
```

**Current Code (Fixed)**:
```csharp
// NEW - no logging:
public void UpdateFollowOffset(double3 offset)
{
    if (_isManualFollowing)
    {
        _followOffset = offset;
        // Removed per-frame logging to reduce verbosity
    }
}
```

**Remaining Console Output**:
- Mode switching: One message per mode change (acceptable)
- Animation start/stop: One message per animation (acceptable)
- RPC commands: One message per command (acceptable)
- Frame updates: None (fixed)

**Test Scenarios**:
1. Enter manual follow → Let run for 10 seconds → Count console messages (should be ≤3)
2. Start orbit animation → Let run to completion → Verify console messages are reasonable (<50 lines)
3. Rapidly switch modes 10 times → Verify only mode change messages, no spam
4. Move camera manually → Verify no per-frame position logging

### Issue 5: Follow Target Destruction/Loss Handling

**Problem**: Follow target object may be destroyed while camera is following it

**Scenarios**:
- Craft explodes while in manual follow mode → `_followedObject` references destroyed object
- User switches craft focus → Follow target changes unexpectedly
- Target goes out of physics range → Position updates may fail

**Current Error Handling**:
```csharp
// In Update(deltaTime)
if (_isManualFollowing && _followedObject != null)
{
    try
    {
        dynamic? dynTarget = _followedObject;
        double3 targetPos = dynTarget?.GetPositionEcl() ?? double3.Zero;
        // ...
    }
    catch (Exception ex)
    {
        Console.WriteLine($"KsaCameraService: Error updating manual follow: {ex.Message}");
        _isManualFollowing = false;
        _followedObject = null;
    }
}
```

**Mitigation Strategies**:
✅ **Implemented**: Try-catch in `Update()` with state cleanup on error
✅ **Implemented**: Null-coalescing operator for safety (`dynTarget?.GetPositionEcl() ?? double3.Zero`)
⚠️ **Missing**: No UI notification when follow target is lost
⚠️ **Missing**: No automatic fallback behavior (e.g., switch to free camera)

**Recommended Enhancements**:
- Add `FollowTargetValid` property that checks target validity
- UI shows warning icon when follow target is invalid
- Option to automatically switch to Free Camera when target is lost
- Periodic validation check (every 2 seconds) rather than waiting for exception

**Test Scenarios**:
1. Follow a craft → Delete it via debug console → Verify graceful handling
2. Follow a craft → Switch active vessel → Verify follow target updates or errors gracefully
3. Follow a craft → Simulate exception in GetPositionEcl() → Verify state cleanup
4. Enter manual follow → Set `_followedObject = null` via reflection → Verify no crash

### Issue 6: Animation System and Manual Follow State Desynchronization

**Problem**: Animation player and camera service states may desync

**Key State Pair**:
- `_animationPlayer.IsPlaying == true` should imply `_cameraService.IsManualFollowing == true`
- `_isManualFollowing == true` does NOT necessarily mean animation is playing (manual mode can be used standalone)

**Desync Scenarios**:
1. Animation player stops (`IsPlaying = false`) but `_isManualFollowing` remains `true`
   - **This is CORRECT behavior** - manual follow persists after animation
2. `_isManualFollowing = false` but animation is still playing
   - **This is INCORRECT** - mode switch should stop animation
3. Multiple animations queued without stopping previous
   - **May cause corruption** - animation player needs explicit stop before new animation

**Sync Points** (where states must align):
- **Animation Start**: MUST set manual follow (`_isManualFollowing = true`)
- **Mode Switch to Free Camera**: MUST stop animation and clear manual follow
- **Mode Switch to Native Follow**: MUST stop animation and clear manual follow
- **Animation Natural End**: Leaves manual follow active (CORRECT)
- **Manual Stop Button**: Stops animation and preserves manual follow (CORRECT)

**Recommended Sync Checks**:
```csharp
// In SkunkworksMod.OnAfterUi() - add assertion
if (_animationPlayer?.IsPlaying == true)
{
    if (!_cameraService.IsManualFollowing)
    {
        Console.WriteLine("[ERROR] Animation playing but not in manual follow mode! Stopping animation.");
        _animationPlayer.Stop();
    }
}
```

**Test Scenarios**:
1. Start animation → Switch to Free Camera → Verify `IsPlaying == false` AND `IsManualFollowing == false`
2. Start animation → Let it complete → Verify `IsPlaying == false` AND `IsManualFollowing == true`
3. Start animation → Click Stop button → Verify correct state (both should be false per current `StopAnimation()` impl)
4. Enter manual follow via button (no animation) → Verify `IsManualFollowing == true` AND `IsPlaying == false`

---

## Integration Test Scenarios

### Scenario 1: Orbit Animation Lifecycle

**Setup**:
1. Launch KSA with deployed mod
2. Open Skunkworks window (F11)
3. Ensure camera is following an active craft

**Test Steps**:
1. **Start Orbit**:
   - [ ] Set orbit duration to 5 seconds
   - [ ] Set orbit distance to 100m
   - [ ] Click "Play Orbit" button
   - [ ] Verify console shows: `[CameraOrbit] Captured original state`
   - [ ] Verify console shows: `[KsaCameraService] Started manual follow`
   - [ ] Verify Camera Info shows "Following target (manual)"
   - [ ] Verify animation status shows "Playing"
   - [ ] Verify camera orbits smoothly around craft

2. **During Orbit**:
   - [ ] Verify Camera Basics "Current Mode" shows "Manual Follow"
   - [ ] Verify position updates smoothly (no stuttering)
   - [ ] Verify no console spam during playback
   - [ ] Camera should look at target throughout orbit

3. **Animation End**:
   - [ ] Wait for 5-second orbit to complete
   - [ ] Verify console shows: `[AnimationEnd] Animation completed`
   - [ ] Verify console shows: `[Skunkworks] Animation state transitioned from playing to stopped`
   - [ ] Verify console shows: `[Skunkworks] Calling StopManualFollow`
   - [ ] **CRITICAL**: Verify NO position snap (camera stays at final orbit position)
   - [ ] Verify Camera Basics "Current Mode" still shows "Manual Follow"
   - [ ] Verify camera continues to track craft smoothly

4. **Post-Animation State**:
   - [ ] Verify `_isManualFollowing == true` (use native debug mode string if available)
   - [ ] Verify camera continues following at current offset
   - [ ] Verify no error messages in subsequent frames
   - [ ] Verify camera position stability (log 5 frames post-animation, verify stable offset)

**Expected Outcome**: Animation completes smoothly without position snapping, camera remains in manual follow mode tracking at final animation offset.

### Scenario 2: Mode Switch During Animation

**Setup**:
1. Start orbit animation (5 seconds, 100m distance)
2. Wait 2 seconds (animation is mid-orbit)

**Test Steps**:
1. **Switch to Free Camera Mid-Animation**:
   - [ ] Click "Free Camera" button during orbit
   - [ ] Verify animation does NOT automatically stop (current limitation)
   - [ ] Verify `_isManualFollowing` changes to `false`
   - [ ] Verify camera position freezes (no longer tracks target)
   - [ ] Verify animation player still shows "Playing" (desync state - see Issue #1)
   - [ ] Click "Stop Animation" manually to clean up

2. **Switch to Native Follow Mid-Animation**:
   - [ ] Start new orbit animation
   - [ ] Wait 2 seconds
   - [ ] Click "Native Follow" button
   - [ ] Verify similar desync behavior (animation continues, follow mode changes)
   - [ ] Verify potential position snap if KSA's SetFollow activates
   - [ ] Clean up by stopping animation

3. **Start Second Animation During First**:
   - [ ] Start orbit animation (10 seconds)
   - [ ] Immediately start second orbit animation (5 seconds)
   - [ ] Verify first animation is replaced (ClearKeyframes called)
   - [ ] Verify no state corruption
   - [ ] Verify console logs show sequential animation starts

**Expected Outcome**: Identifies desync issues that need fixing (Issue #1 and Issue #6). Animation should stop when mode switches.

**Known Limitation**: Animation does not auto-stop on mode switch (Issue #1). Requires manual "Stop Animation" button click.

### Scenario 3: Manual Follow Offset Preservation

**Setup**:
1. Ensure camera following a craft
2. Open Camera Basics panel

**Test Steps**:
1. **Enter Manual Follow and Move Camera**:
   - [ ] Note initial camera position (console or Camera Info display)
   - [ ] Click "Enter Manual Follow" button
   - [ ] Verify "Current Mode" shows "Manual Follow"
   - [ ] Verify console: `[CameraBasicsPanel] Entered manual follow mode`
   - [ ] Use "Forward" button to move camera 50m forward
   - [ ] Use "Right" button to move camera 50m right
   - [ ] Note new camera position (should be ~70m offset from initial)

2. **Exit Manual Follow (Keep Offset)**:
   - [ ] Click "Exit Manual Follow (keep offset)" button
   - [ ] **CRITICAL**: Verify camera position does NOT change
   - [ ] Verify "Current Mode" still shows "Manual Follow" (expected - mode doesn't change)
   - [ ] Verify camera continues to track craft at custom offset
   - [ ] Verify console shows: `[CameraExit] Exiting manual follow (keeping current offset)`

3. **Exit Manual Follow (Restore Native)**:
   - [ ] From manual follow state, click "Exit Manual Follow (restore native)" button
   - [ ] Verify "Current Mode" changes to "Following"
   - [ ] **EXPECT POSITION SNAP**: Camera should jump to KSA's default follow offset
   - [ ] Verify console shows: `[CameraExit] Restoring native follow via SetFollow`
   - [ ] Verify console shows SetFollow flags used
   - [ ] Verify camera now follows using KSA's native behavior

**Expected Outcome**: "Keep offset" preserves position perfectly. "Restore native" intentionally snaps to KSA default offset.

### Scenario 4: RPC Command Integration

**Setup**:
1. Ensure terminal emulator is running with RPC enabled
2. Camera following a craft

**Test Steps**:
1. **Send Orbit RPC Command**:
   ```bash
   echo '{"action":"camera-orbit","params":{"time":5.0,"distance":100}}' | nc localhost 8089
   ```
   - [ ] Verify RPC response: `{"success":true,"result":{"status":"playing",...}}`
   - [ ] Verify orbit animation starts (identical to UI button behavior)
   - [ ] Verify console shows: `[CameraOrbit] Captured original state`
   - [ ] Verify animation plays to completion
   - [ ] Verify smooth restoration after animation ends

2. **RPC Command Error Handling**:
   ```bash
   # No follow target (switch to free camera first)
   echo '{"action":"camera-orbit","params":{"time":5.0}}' | nc localhost 8089
   ```
   - [ ] Verify RPC response: `{"success":false,"error":"No follow target - camera must be following an object"}`
   - [ ] Verify no state corruption
   - [ ] Verify graceful error message in console

3. **RPC Command During Active Animation**:
   - [ ] Start orbit via UI button (10 seconds)
   - [ ] Send RPC command for new orbit (5 seconds)
   - [ ] Verify first animation stops cleanly
   - [ ] Verify second animation starts
   - [ ] Verify no state corruption or crashes

**Expected Outcome**: RPC commands behave identically to UI button clicks. Error handling is robust. State consistency maintained.

### Scenario 5: Follow Target Loss Handling

**Setup**:
1. Camera following a craft in manual follow mode
2. Have ability to destroy or despawn craft (debug console or scenario scripting)

**Test Steps**:
1. **Target Destruction During Manual Follow**:
   - [ ] Enter manual follow mode on a craft
   - [ ] Destroy craft (via debug command or explosion)
   - [ ] Verify console shows: `KsaCameraService: Error updating manual follow: [error message]`
   - [ ] Verify `_isManualFollowing` becomes `false`
   - [ ] Verify `_followedObject` becomes `null`
   - [ ] Verify no crash or exception propagation
   - [ ] Verify camera mode updates to reflect target loss

2. **Target Destruction During Orbit Animation**:
   - [ ] Start orbit animation on a craft
   - [ ] Destroy craft mid-animation
   - [ ] Verify animation continues (may orbit previous position)
   - [ ] Verify error handling in `Update()` catches target loss
   - [ ] Verify animation stops or fails gracefully
   - [ ] Verify no crash

**Expected Outcome**: Target loss is caught by exception handling. Manual follow state clears. No crashes. Console logs error message.

### Scenario 6: Rapid Mode Switching Stress Test

**Setup**:
1. Camera following craft
2. Prepare to rapidly click UI buttons

**Test Steps**:
1. **Rapid Sequential Mode Switches**:
   - [ ] Click buttons in rapid succession: Free Camera → Native Follow → Manual Follow → Free Camera → Native Follow
   - [ ] Perform sequence 10 times in 10 seconds
   - [ ] Verify no exceptions or crashes
   - [ ] Verify final state is consistent (camera mode matches UI state)
   - [ ] Check that `_isManualFollowing`, `_followedObject`, `_followOffset` are consistent with final mode
   - [ ] Review console logs for unexpected errors

2. **Spam Single Button**:
   - [ ] Enter manual follow mode
   - [ ] Rapidly click "Enter Manual Follow" button 20 times
   - [ ] Verify no state corruption (button should be disabled, but test anyway)
   - [ ] Spam "Exit Manual Follow (keep offset)" 20 times
   - [ ] Verify consistent state after spam

3. **Mode Switch + Animation Start**:
   - [ ] Start orbit animation
   - [ ] Immediately switch to Free Camera
   - [ ] Immediately start new orbit animation
   - [ ] Immediately switch to Native Follow
   - [ ] Verify final state is consistent
   - [ ] Verify no memory leaks or dangling references

**Expected Outcome**: System remains stable despite rapid input. State consistency maintained. No crashes or exceptions.

### Scenario 7: Camera Position Property Synchronization

**Setup**:
1. Camera following craft in manual follow mode

**Test Steps**:
1. **Monitor Position Property**:
   - [ ] Enter manual follow mode
   - [ ] Query `_cameraService.Position` property each frame for 5 seconds
   - [ ] Verify it updates smoothly (camera follows craft movement)
   - [ ] Verify position matches craft position + `_followOffset`

2. **Direct Position Assignment**:
   - [ ] Enter manual follow mode
   - [ ] Use movement buttons to change camera position
   - [ ] Verify `camera.PositionEcl` property is updated
   - [ ] Verify visual camera position matches property value
   - [ ] Verify offset calculation is correct (position - target position)

3. **Position Consistency After Mode Switch**:
   - [ ] Note camera position in manual follow mode
   - [ ] Switch to Free Camera mode
   - [ ] Verify camera position property unchanged
   - [ ] Switch to Native Follow mode
   - [ ] Verify position changes to KSA's native offset (expected snap)
   - [ ] Switch back to Manual Follow
   - [ ] Verify position returns to captured offset

**Expected Outcome**: Position property always reflects actual camera position. Mode switches predictably affect position. No desyncs.

---

## State Consistency Validation Checklist

Use this checklist to validate state consistency after any integration test:

### After Animation Completion
- [ ] `_animationPlayer.IsPlaying == false`
- [ ] `_cameraService.IsManualFollowing == true` (or false if explicitly exited)
- [ ] `_followedObject != null` (if still in manual follow)
- [ ] `camera.Following == null` (if in manual follow)
- [ ] Camera position stable across frames (no jitter/snap)
- [ ] `_followOffset` matches actual camera offset from target

### After Mode Switch to Free Camera
- [ ] `_isManualFollowing == false`
- [ ] `_followedObject == null`
- [ ] `_followOffset == double3.Zero` or cleared
- [ ] `camera.Following == null`
- [ ] Camera position unchanged from pre-switch position
- [ ] Animation stopped (if was playing) - **KNOWN ISSUE: not auto-stopped**

### After Mode Switch to Native Follow
- [ ] `_isManualFollowing == false`
- [ ] `_followedObject == null`
- [ ] `camera.Following != null`
- [ ] Camera position MAY snap (expected KSA SetFollow behavior)
- [ ] Animation stopped (if was playing) - **KNOWN ISSUE: not auto-stopped**

### After Entering Manual Follow
- [ ] `_isManualFollowing == true`
- [ ] `_followedObject == [previous follow target]`
- [ ] `_followOffset == [captured offset at entry time]`
- [ ] `camera.Following == null` (unfollowed in KSA)
- [ ] Camera position unchanged from pre-switch position

---

## Performance and Logging Checks

### Console Output Validation
**Acceptable Output**:
- Mode changes: 1 message per change
- Animation start/stop: 2-3 messages per event
- RPC commands: 1-2 messages per command
- Errors: As needed

**Unacceptable Output**:
- Per-frame position updates (60+ messages/second)
- Per-frame offset updates
- Repeated "camera not available" warnings
- Duplicate mode change messages

**Test**:
- [ ] Enter manual follow mode
- [ ] Let run for 30 seconds
- [ ] Count console messages (should be <10 total)

### Performance Monitoring
**Metrics**:
- Frame time increase during manual follow: <0.2ms
- Memory allocation per frame: Minimal (no new objects in Update loop)
- Animation playback smoothness: 60 FPS with no stutters

**Tools**:
- KSA frame time display
- Console timestamp gaps (should be ~16ms between frames at 60 FPS)

**Test**:
- [ ] Check frame time before entering manual follow
- [ ] Enter manual follow, start orbit animation
- [ ] Monitor frame time during animation (should not increase >0.5ms)
- [ ] Check for memory leaks after 10 minutes runtime

---

## Known Issues and Workarounds

### Issue #1: Animation Does Not Auto-Stop on Mode Switch
**Status**: ❗ **NEEDS FIX**  
**Workaround**: Manually click "Stop Animation" button before switching modes  
**Recommended Fix**: See Issue 1 section above (add animation stop to mode switch handlers)

### Issue #2: Manual Follow Button Not Disabled During Manual Follow
**Status**: ⚠️ **MINOR**  
**Workaround**: None needed, duplicate clicks are harmless  
**Recommended Fix**: Add button disabling logic based on `_cameraService.IsManualFollowing`

### Issue #3: No UI Notification When Follow Target Lost
**Status**: ⚠️ **MINOR**  
**Workaround**: Check console for error messages  
**Recommended Fix**: Add red warning text in Camera Info section when follow target becomes invalid

### Issue #4: RPC Command Error Messages Not User-Friendly
**Status**: ⚠️ **MINOR**  
**Workaround**: Parse JSON response for error field  
**Recommended Fix**: Enhance error messages with actionable guidance (e.g., "Switch to a following mode first")

### Issue #5: Native Follow Mode Snapping Behavior Unpredictable
**Status**: ⚠️ **EXPECTED BEHAVIOR**  
**Explanation**: KSA's SetFollow() applies its own offset logic, causing position changes  
**Workaround**: Use "Exit Manual Follow (keep offset)" to avoid snapping  
**Not a Bug**: This is expected KSA camera behavior

---

## Test Sign-Off

**Test Date**: ___________  
**Tester**: ___________  
**Build Version**: ___________  

**Integration Test Results**:
- [ ] Scenario 1: Orbit Animation Lifecycle — ❏ Pass ❏ Fail
- [ ] Scenario 2: Mode Switch During Animation — ❏ Pass ❏ Fail
- [ ] Scenario 3: Manual Follow Offset Preservation — ❏ Pass ❏ Fail
- [ ] Scenario 4: RPC Command Integration — ❏ Pass ❏ Fail
- [ ] Scenario 5: Follow Target Loss Handling — ❏ Pass ❏ Fail
- [ ] Scenario 6: Rapid Mode Switching Stress Test — ❏ Pass ❏ Fail
- [ ] Scenario 7: Camera Position Property Synchronization — ❏ Pass ❏ Fail

**Critical Issues Found**: ___________

**Non-Critical Issues Found**: ___________

**Recommendations**: ___________

---

## Appendix: Debugging Tips

### Inspecting State at Runtime
Add temporary debug UI to `CameraBasicsPanel.Render()`:
```csharp
ImGui.SeparatorText("Debug State");
ImGui.TextDisabled($"_isManualFollowing: {_cameraService.IsManualFollowing}");
ImGui.TextDisabled($"_animationPlayer.IsPlaying: {_animationPlayer?.IsPlaying}");
ImGui.TextDisabled($"_followedObject: {(_cameraService.FollowTarget != null ? "not null" : "null")}");
var offset = _cameraService.Position - _cameraService.GetTargetPosition();
ImGui.TextDisabled($"Current Offset: {offset}");
```

### Console Logging Pattern
Use prefix tags for easy filtering:
- `[CameraBasicsPanel]` - UI interactions
- `[KsaCameraService]` - Service operations
- `[CameraOrbit]` - Orbit animation RPC
- `[AnimationEnd]` - Animation completion
- `[Skunkworks]` - Main mod lifecycle
- `[CameraExit]` - Manual follow exit operations

### Breakpoint Locations for Debugging
- `KsaCameraService.StartManualFollow()` - Manual follow entry
- `KsaCameraService.StopManualFollow()` - Manual follow exit
- `KsaCameraService.EnterFreeCameraMode()` - Free camera mode
- `KsaCameraService.Update()` - Per-frame position updates
- `CameraAnimationPlayer.Update()` - Animation frame generation
- `SkunkworksMod.OnAfterUi()` - Animation state transition detection

### Reproducing State Corruption
If state corruption occurs:
1. Note exact sequence of UI interactions
2. Check console logs for timing (timestamps)
3. Verify invariants (see State Consistency Validation Checklist)
4. Add assertions to detect invariant violations early
5. Use breakpoints to step through mode transitions

---

## Related Documents

- **[CAMERA_BASICS_TESTING.md](CAMERA_BASICS_TESTING.md)**: Manual testing checklist for Camera Basics UI and functionality
- **[CAMERA_BEHAVIOR.md](../CAMERA_BEHAVIOR.md)**: Overall camera feature plan and task breakdown
- **Source Code**:
  - [KsaCameraService.cs](../../caTTY.SkunkworksGameMod/Camera/KsaCameraService.cs)
  - [CameraBasicsPanel.cs](../../caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs)
  - [CameraAnimationPlayer.cs](../../caTTY.SkunkworksGameMod/Camera/Animation/CameraAnimationPlayer.cs)
  - [SkunkworksMod.cs](../../caTTY.SkunkworksGameMod/SkunkworksMod.cs)
  - [CameraOrbitRpcAction.cs](../../caTTY.SkunkworksGameMod/Rpc/Actions/CameraOrbitRpcAction.cs)
