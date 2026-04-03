# Camera Control Modes

This document describes the camera control modes available in KSA (Kitten Space Agency) and how they are exposed through the Camera API and our custom implementation.

## Table of Contents

1. [Native KSA Camera Modes](#native-ksa-camera-modes)
2. [Custom Implementation Modes](#custom-implementation-modes)
3. [Camera Control Methods](#camera-control-methods)
4. [State Transitions](#state-transitions)
5. [Current Implementation Notes](#current-implementation-notes)
6. [Open Questions](#open-questions)

---

## Native KSA Camera Modes

KSA provides two primary camera modes that control how the camera's position and rotation are managed:

### 1. Following Mode

**Description**: The camera automatically tracks an astronomical object, maintaining a default offset and orientation relative to that object.

**Characteristics**:
- Camera position is updated each frame to follow the target object
- KSA manages the camera offset and rotation automatically
- The followed object is accessible via `Camera.Following` property
- This is the default mode when a craft or celestial body is active

**API Surface**:
```csharp
// Check if camera is following something
dynamic? followedObject = camera.Following;  // null if not following
bool isFollowing = followedObject != null;

// Start following an astronomical object
camera.SetFollow(
    Astronomical target,
    bool unknown0,           // Purpose unclear - see Open Questions
    bool changeControl,      // Likely controls camera controller mode
    bool alert               // Possibly shows UI notification
);
```

**Example Usage**:
```csharp
var camera = GetCamera();
var craft = GetPlayerCraft();  // Some Astronomical object

// Start following with default parameters
camera.SetFollow(craft, false, false, false);

// Verify follow state
if (camera.Following != null)
{
    Console.WriteLine($"Now following: {camera.Following}");
}
```

**When to Use**:
- Default gameplay camera behavior
- When you want KSA to manage camera tracking automatically
- After exiting custom camera modes

---

### 2. Free Camera Mode

**Description**: The camera is not following any object and can be positioned/rotated arbitrarily. All position and rotation updates are manual.

**Characteristics**:
- `Camera.Following` returns `null`
- Camera position and rotation are fully under programmer control
- KSA does not override position or rotation each frame
- Useful for cinematic shots, free exploration, or manual camera control

**API Surface**:
```csharp
// Stop following any object
camera.Unfollow();

// Verify free camera state
bool isFreeCamera = camera.Following == null;
```

**Example Usage**:
```csharp
var camera = GetCamera();

// Enter free camera mode
camera.Unfollow();

// Now you can freely manipulate camera
camera.PositionEcl = new double3(1000, 2000, 3000);
camera.WorldRotation = customQuaternion;
```

**When to Use**:
- When implementing custom camera movement (WASD controls, etc.)
- For static camera positions (e.g., fixed observation points)
- During camera animations where you control every frame
- Testing camera positioning without follow interference

---

## Custom Implementation Modes

Our `KsaCameraService` implements an additional camera mode that works alongside KSA's native modes:

### Manual Follow Mode

**Description**: A custom tracking system that unfollows in KSA but manually updates the camera position each frame to maintain follow behavior with a specific offset.

**Purpose**: Provides smooth camera transitions and offset control without triggering KSA's default snap/offset behavior when transitioning between modes.

**Characteristics**:
- Camera is unfollowed in KSA (`Camera.Following == null`)
- Camera position is manually updated each frame in `Update()` method
- Custom offset from target is maintained
- Smooth transitions are possible by changing offset over time (used for animations)

**Implementation Details**:
```csharp
// Internal state
private dynamic? _followedObject;     // The target we're tracking
private double3 _followOffset;        // Offset from target in ECL coordinates
private bool _isManualFollowing;      // Manual follow active flag

// Start manual follow mode
public void StartManualFollow(double3 offset)
{
    var camera = GetCamera();
    var currentFollowing = camera.Following;
    
    if (currentFollowing != null)
    {
        _followedObject = currentFollowing;  // Store target
        _followOffset = offset;              // Store offset
        _isManualFollowing = true;           // Enable flag
        camera.Unfollow();                   // Unfollow in KSA
    }
}

// Update camera position each frame
public void Update(double deltaTime)
{
    if (_isManualFollowing && _followedObject != null)
    {
        dynamic dynTarget = _followedObject;
        double3 targetPos = dynTarget.GetPositionEcl();
        camera.PositionEcl = targetPos + _followOffset;
    }
}
```

**API Surface** (via `ICameraService`):
```csharp
// Start manual follow with specific offset
void StartManualFollow(double3 offset);

// Update the follow offset (for animations)
void UpdateFollowOffset(double3 offset);

// Check if manual follow is active
bool IsManualFollowing { get; }

// Get the follow target (works in both native and manual follow)
object? FollowTarget { get; }
```

**Example Usage**:
```csharp
// Assume we're currently following a craft in KSA
var cameraService = GetCameraService();

// Calculate current offset from target
var currentOffset = cameraService.Position - cameraService.GetTargetPosition();

// Enter manual follow mode preserving current offset
cameraService.StartManualFollow(currentOffset);

// Now we can smoothly animate the offset
for (int i = 0; i < 100; i++)
{
    var newOffset = LerpOffset(currentOffset, targetOffset, i / 100.0);
    cameraService.UpdateFollowOffset(newOffset);
    await Task.Delay(16);  // ~60 fps
}
```

**When to Use**:
- During camera animations that need to maintain follow relationship
- When you need precise control over camera offset from target
- To avoid position snapping when transitioning between modes
- For orbit animations or other custom follow behaviors

---

## Camera Control Methods

The KSA Camera API provides several methods for direct position and rotation manipulation. All coordinates use the ECL (Ecliptic) coordinate frame.

### Position Properties

```csharp
// Position in Ecliptic coordinates (meters)
double3 PositionEcl { get; set; }

// Local position (relative to parent, if any)
double3 LocalPosition { get; set; }
```

**Example**:
```csharp
// Direct position manipulation (works best in Free Camera or Manual Follow mode)
camera.PositionEcl = new double3(x: 10000, y: 20000, z: 5000);

// In native Following mode, KSA may override this each frame
```

---

### Rotation Properties and Methods

```csharp
// World rotation quaternion
doubleQuat WorldRotation { get; set; }

// Set rotation via 4x4 matrix (also affects local position)
void SetMatrix(float4x4 matrix);
```

**Example**:
```csharp
// Direct rotation via quaternion
camera.WorldRotation = new doubleQuat(x, y, z, w);

// Using SetMatrix (requires position preservation)
var rotMatrix = float4x4.CreateFromQuaternion(myQuaternion);
var savedPos = camera.PositionEcl;
camera.SetMatrix(rotMatrix);
camera.PositionEcl = savedPos;  // Restore position
```

**⚠️ Warning**: `SetMatrix()` can affect `LocalPosition`, so always save and restore `PositionEcl` after calling it.

---

### Orientation Methods

```csharp
// Direction vectors (return normalized double3)
double3 GetForward();  // Forward direction
double3 GetRight();    // Right direction
double3 GetUp();       // Up direction

// Orient camera to look at target
void LookAt(double3 target, double3 up);

// Static method to compute look-at rotation
static doubleQuat LookAtRotation(double3 direction, double3 up);
```

**Example**:
```csharp
// Orient camera to look at Earth
var earthPos = earth.GetPositionEcl();
var upVector = new double3(0, 0, 1);  // ECL Z-axis is up
camera.LookAt(earthPos, upVector);

// Get camera-relative movement direction
var forward = camera.GetForward();
camera.PositionEcl += forward * 1000;  // Move 1000m forward
```

---

### Translation Method

```csharp
// Move camera by offset vector (in ECL coordinates)
void Translate(double3 offset);
```

**Example**:
```csharp
// Move camera 1000m in the +X direction
camera.Translate(new double3(1000, 0, 0));
```

---

### Field of View Methods

```csharp
// Get FOV in radians
float GetFieldOfView();

// Set FOV (input in degrees, despite GetFieldOfView returning radians)
void SetFieldOfView(float degrees);
```

**Example**:
```csharp
// Get current FOV in radians
float fovRadians = camera.GetFieldOfView();
float fovDegrees = fovRadians * (180.0f / Math.PI);

// Set FOV to 90 degrees
camera.SetFieldOfView(90.0f);
```

**⚠️ Note**: API inconsistency - `GetFieldOfView()` returns radians but `SetFieldOfView()` accepts degrees.

---

### Follow/Unfollow Methods

```csharp
// Currently followed object (dynamic/Astronomical or null)
dynamic Following { get; }

// Start following an astronomical object
void SetFollow(
    Astronomical target,
    bool unknown0,        // Purpose unclear
    bool changeControl,   // Likely controls camera controller mode
    bool alert            // Possibly UI notification
);

// Stop following
void Unfollow();
```

**Example**:
```csharp
// Check follow state
if (camera.Following != null)
{
    Console.WriteLine($"Following: {camera.Following}");
}

// Start following
camera.SetFollow(craft, false, false, false);

// Stop following
camera.Unfollow();
```

---

## State Transitions

This section describes how to transition between different camera modes.

### Following → Free Camera

**Method**: Call `Unfollow()`

```csharp
var camera = GetCamera();

// We're currently following something
if (camera.Following != null)
{
    // Enter free camera mode
    camera.Unfollow();
    
    // Verify transition
    Console.WriteLine($"Free camera: {camera.Following == null}");
}
```

**Effect**:
- `Camera.Following` becomes `null`
- Camera position and rotation freeze at current values
- No more automatic tracking

---

### Free Camera → Following

**Method**: Call `SetFollow(target, ...)`

```csharp
var camera = GetCamera();
var targetCraft = GetCurrentCraft();

// We're in free camera mode
if (camera.Following == null)
{
    // Start following with default parameters
    camera.SetFollow(targetCraft, false, false, false);
    
    // Verify transition
    Console.WriteLine($"Now following: {camera.Following}");
}
```

**Effect**:
- `Camera.Following` is set to `target`
- Camera may snap to default offset from target (behavior depends on boolean parameters)
- KSA takes over position/rotation updates

**⚠️ Note**: The exact behavior (snap vs smooth transition, offset applied, etc.) likely depends on the boolean parameters - see [Open Questions](#open-questions).

---

### Following → Manual Follow

**Method**: Call `StartManualFollow(offset)` (custom implementation)

```csharp
var cameraService = GetCameraService();

// Currently following in native KSA mode
if (cameraService.FollowTarget != null && !cameraService.IsManualFollowing)
{
    // Calculate current offset to avoid position jump
    var currentOffset = cameraService.Position - cameraService.GetTargetPosition();
    
    // Enter manual follow mode
    cameraService.StartManualFollow(currentOffset);
    
    // Verify transition
    Console.WriteLine($"Manual following: {cameraService.IsManualFollowing}");
}
```

**Effect**:
- Camera unfollows in KSA (`Camera.Following == null`)
- `IsManualFollowing` flag becomes `true`
- Target and offset are stored in `_followedObject` and `_followOffset`
- Camera position is manually updated each frame in `Update()`

**Important**: Use current offset to avoid position snapping.

---

### Manual Follow → Exit (Keep Current Offset)

**Status**: ⚠️ **Current Implementation Issue**

**Current Method**: Call `StopManualFollow()` (but see note below)

```csharp
var cameraService = GetCameraService();

if (cameraService.IsManualFollowing)
{
    // Current implementation comment:
    // "DON'T call SetFollow() - it re-applies KSA's default offset, causing a snap"
    cameraService.StopManualFollow();
    
    // PROBLEM: This intentionally does NOT clear _isManualFollowing!
    // The camera continues updating position in manual follow mode.
}
```

**Current Behavior (Per Code Comments)**:
- Does **NOT** clear `_isManualFollowing` flag
- Does **NOT** clear `_followedObject` reference
- Does **NOT** call `SetFollow()`
- Camera **continues** to track target with current offset in `Update()` loop
- Essentially a no-op that logs a message

**Intended Purpose**: Preserve current offset to avoid position snapping that would occur if we called `SetFollow()`.

**Problem**: There is no true "exit" - the camera remains in manual follow mode indefinitely.

---

### Manual Follow → Restore Native Follow

**Status**: 🚧 **Not Yet Implemented**

**Required Implementation**: See [Task 2.2 in CAMERA_BEHAVIOR.md](../../plans/investigations/CAMERA_BEHAVIOR.md)

**Proposed API**:
```csharp
public enum ManualFollowExitMode
{
    KeepCurrentOffset,      // Preserve smooth follow (current behavior)
    RestoreNativeFollow,    // Exit to KSA native follow (NEW)
}

bool ExitManualFollow(
    ManualFollowExitMode mode,
    bool? unknown0 = null,
    bool? changeControl = null,
    bool? alert = null
);
```

**Expected Behavior for `RestoreNativeFollow` Mode**:
1. Clear `_isManualFollowing` flag
2. Call `SetFollow(_followedObject, unknown0, changeControl, alert)` to restore native follow
3. Clear `_followedObject` reference
4. Return `true` on success, `false` on failure (e.g., incompatible target type)

**Expected Behavior for `KeepCurrentOffset` Mode**:
- Preserve existing non-snapping behavior (current `StopManualFollow()` intent)
- Allows smooth continuation of follow without KSA re-applying default offset

---

## Current Implementation Notes

### Manual Follow State Management

The manual follow implementation stores state in three fields:

```csharp
private dynamic? _followedObject;  // Target being tracked
private double3 _followOffset;     // Offset from target in ECL coordinates
private bool _isManualFollowing;   // Whether manual follow is active
```

**State Lifecycle**:
1. **Enter**: `StartManualFollow()` sets all three fields, calls `camera.Unfollow()`
2. **Update**: Each frame, `Update()` reads these fields and sets `camera.PositionEcl`
3. **Modify Offset**: `UpdateFollowOffset()` changes `_followOffset` for animations
4. **Exit**: ⚠️ Currently incomplete - `StopManualFollow()` does not clear state

---

### Position Update Loop

Manual follow works by updating camera position each frame:

```csharp
public void Update(double deltaTime)
{
    if (_isManualFollowing && _followedObject != null)
    {
        try
        {
            dynamic dynTarget = _followedObject;
            double3 targetPos = dynTarget.GetPositionEcl();
            camera.PositionEcl = targetPos + _followOffset;
        }
        catch (Exception ex)
        {
            // On error, disable manual follow
            _isManualFollowing = false;
            _followedObject = null;
        }
    }
}
```

**Key Points**:
- Runs every frame when `_isManualFollowing == true`
- Uses `dynamic` to call `GetPositionEcl()` on target (avoids type constraints)
- Gracefully handles errors by disabling manual follow
- Position is the **sum** of target position and stored offset

---

### Follow Target Property

The `ICameraService.FollowTarget` property abstracts over native and manual follow:

```csharp
public object? FollowTarget => 
    _isManualFollowing ? _followedObject : GetCamera()?.Following;
```

**Behavior**:
- In manual follow mode: returns `_followedObject` (our stored reference)
- In native follow mode: returns `Camera.Following` (KSA's reference)
- In free camera mode: returns `null`

This allows UI and other code to query "what is the camera following?" without knowing which mode is active.

---

### Rotation Management

The `ApplyRotation()` method demonstrates proper use of `SetMatrix()`:

```csharp
public void ApplyRotation(float yaw, float pitch, float roll)
{
    // Convert YPR to quaternion, then to matrix
    var rotMatrix = CreateRotationMatrix(yaw, pitch, roll);
    
    // CRITICAL: Save position before SetMatrix
    var savedPos = camera.PositionEcl;
    var savedLocalPos = camera.LocalPosition;
    
    camera.SetMatrix(rotMatrix);
    
    // Restore position after SetMatrix
    camera.LocalPosition = savedLocalPos;
    camera.PositionEcl = savedPos;
    camera.WorldRotation = newRot;
}
```

**Why Position Restoration is Needed**:
- `SetMatrix()` can modify `LocalPosition` as a side effect
- Without restoration, camera position can drift
- Always save and restore both `PositionEcl` and `LocalPosition`

---

## Open Questions

These questions need to be answered through experimentation with the KSA game engine.

### 1. SetFollow Boolean Parameters

**Question**: What do the three boolean parameters on `SetFollow()` actually control?

```csharp
camera.SetFollow(
    Astronomical target,
    bool unknown0,        // ??? 
    bool changeControl,   // Camera controller mode?
    bool alert            // UI notification?
);
```

**Current Usage**: We always pass `false, false, false` based on example code.

**Hypothesis**:
- `changeControl`: May toggle between different camera controller modes separate from follow state
  - Could control whether user can manually rotate camera while following
  - Could enable/disable certain camera behaviors (dampening, auto-orient, etc.)
  - Might be the key to understanding the "control mode" vs "follow mode" distinction
- `unknown0`: Purpose completely unclear
- `alert`: Possibly shows a UI notification/toast when following starts

**Experimentation Plan** (from Task 2.3):
1. Create UI toggles for all three boolean flags
2. Try all 8 combinations (2³) of the flags
3. For each combination, observe:
   - Does camera snap to a default offset?
   - Can user control camera rotation/position while following?
   - Are there any UI changes (notifications, HUD updates, etc.)?
   - Does `Camera.Following` behavior change?
   - Are there any accessible properties that reflect "controller mode" state?

**Where to Look**:
- Use reflection to search for properties like `ControlMode`, `Controller`, `CameraController` on `KSA.Camera`
- Check if behavior differs between craft and celestial body targets
- Log before/after state of all camera properties

---

### 2. Native Camera Controller/Control Modes

**Question**: Is there a "camera controller mode" or "camera control mode" that's distinct from follow/unfollow state?

**Evidence Suggesting Yes**:
- The `changeControl` parameter name implies control over something beyond just following
- Real-world games often separate:
  - **Follow target**: What object is tracked
  - **Control mode**: How camera controller behaves (free look, locked, orbit, etc.)

**Evidence Suggesting No**:
- We haven't discovered any camera properties beyond `Following` and position/rotation
- Standard usage doesn't seem to require awareness of additional modes

**Proposed Investigation** (Task 2.4):
Use reflection to search for additional state on `KSA.Camera`:

```csharp
public string? GetNativeControlModeDebug()
{
    var camera = GetCamera();
    if (camera == null) return null;
    
    // Search for likely property/field names
    string[] candidates = new[]
    {
        "ControlMode", "Controller", "CameraController",
        "Mode", "State", "CameraMode", "ControlState",
        "FollowMode", "TrackingMode"
    };
    
    // Use reflection to find and read values
    foreach (var name in candidates)
    {
        var prop = camera.GetType().GetProperty(name);
        if (prop != null)
        {
            var value = prop.GetValue(camera);
            return $"{name}: {value} ({value?.GetType().Name})";
        }
        
        var field = camera.GetType().GetField(name);
        if (field != null)
        {
            var value = field.GetValue(camera);
            return $"{name}: {value} ({value?.GetType().Name})";
        }
    }
    
    return null;  // Nothing found
}
```

**Acceptance Criteria**:
- Method returns descriptive string if mode property/field exists
- Returns `null` gracefully if nothing found
- UI can display this debug info to observe mode changes during flag experiments

---

### 3. Position Snapping Behavior

**Question**: Under what conditions does `SetFollow()` cause the camera to snap to a default offset?

**Observations**:
- Current code avoids calling `SetFollow()` after animations to prevent snapping
- This suggests `SetFollow()` applies a default offset, not preserving current position
- But is this always true, or does it depend on the boolean parameters?

**Hypothesis**:
- Always snaps when `changeControl = true`?
- Never snaps when `changeControl = false`?
- Depends on current camera position relative to target?

**Test Plan**:
1. Start following with `SetFollow(craft, false, false, false)`
2. Note camera position and offset from craft
3. Manually move camera to different position (via `PositionEcl`)
4. Call `SetFollow()` again with different flag combinations
5. Observe if camera:
   - Jumps to a default offset (snap)
   - Maintains current position (smooth)
   - Interpolates to default offset over time (transition)

---

### 4. Manual Follow vs Native Follow - Frame Timing

**Question**: Does manual follow in `Update()` happen at the same timing/frequency as native follow updates?

**Potential Issues**:
- Native follow might update position **after** our `Update()` call in the frame
- This could cause 1-frame lag or jitter when transitioning modes
- Animation smoothness might differ between manual and native follow

**Test Plan**:
1. Create a high-speed orbit animation (target moves quickly)
2. Transition from manual follow to native follow mid-animation
3. Record camera position each frame with high-precision timestamps
4. Analyze for:
   - Position discontinuities (jumps)
   - Timing jitter between frames
   - Differences in update frequency

---

## Summary

This document covers:

✅ **Native Modes**: Following and Free Camera modes provided by KSA
✅ **Custom Modes**: Manual Follow mode implemented in `KsaCameraService`
✅ **API Methods**: Complete coverage of camera control methods with examples
✅ **State Transitions**: How to switch between modes (with notes on missing implementations)
✅ **Implementation Details**: Current code behavior and quirks
⚠️ **Open Questions**: Areas requiring experimentation to understand KSA behavior

**Next Steps** (per CAMERA_BEHAVIOR.md plan):
1. Implement `ExitManualFollow()` with proper mode semantics (Task 2.2)
2. Add safe `SetFollow()` invocation with flag experiments (Task 2.3)  
3. Add best-effort control mode introspection (Task 2.4)
4. Build UI to test all modes and transitions (Phase 3)
5. Systematically answer open questions through in-game experiments
