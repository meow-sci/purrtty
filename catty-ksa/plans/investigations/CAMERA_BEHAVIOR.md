# Overview

We need to figure out how the camera controls work in detail in the KSA (Kitten Space Agency) game.

We have some code which is doing camera manipulation in the `caTTY.SkunkworksGameMod` mod, but there are some areas of camera control behaviors observed that cannot be explained yet.

We need to go back to fundamental building blocks of ways to control the camera in-game before building up the more complex features like the existing keyframe system and orbit animation.

# Goal

1. Refactor the existing SkukworksGameMod ImGui code to put all the existing animation/orbit code into a ImGui collapsing header and arrange the code to expect many collapsing headers of distinct, separate experimental code that has no relation to one another.  Name the existing one "Orbit Animation"
2. Add a new collapsing header section for "Camera Basics"
3. Implement completely new and isolated code to test camera basics, we want ImGui controls which will:
    1. Change the camera mode (see the existing code to see how this is happening), and turn it into buttons to switch between modes and restore the default mode, etc.
    2. A series of buttons to move the camera in various vector directions to test moving camera

# Tasks

## MUST FOLLOW RULES FOR ALL TASKS

- Before a task is complete
    - the solution must compile successfully with `dotnet build`
- Upon task completion
    - create a git commit of all changes with a good subject line and detailed markdown body of what was implemented.  the subject should be prefixed with e.g. [phase 1 task 1]: [subject here]

## Phase 1: UI Refactoring - Collapsing Header Structure

### Task 1.1: Refactor CameraDebugPanel to use Collapsing Headers
**File**: `caTTY.SkunkworksGameMod/UI/CameraDebugPanel.cs`

**Objective**: Restructure the existing camera debug panel to organize content into collapsing headers, making room for multiple experimental sections.

**Implementation Details**:
1. The existing `Render()` method currently renders sections in sequence:
   - Camera Info
   - Orbit Action controls
   - Animation Status
   - Keyframe Preview

2. Wrap the orbit-related sections in an ImGui collapsing header:
   ```csharp
   if (ImGui.CollapsingHeader("Orbit Animation", ImGuiTreeNodeFlags.DefaultOpen))
   {
       // Move existing orbit controls here
       RenderOrbitControls();
       ImGui.Spacing();
       ImGui.SeparatorText("Animation Status");
       RenderAnimationStatus();
       ImGui.Spacing();
       ImGui.SeparatorText("Keyframe Preview");
       _previewPanel.Render(_cameraService);
   }
   ```

3. Keep "Camera Info" outside collapsing headers at the top for always-visible status

4. Preserve all existing functionality - this is purely a structural change

**Acceptance Criteria**:
- All existing orbit animation controls are contained within a "Orbit Animation" collapsing header
- Camera info remains visible at the top
- No functionality changes - all existing features work identically
- The collapsing header is expanded by default (`ImGuiTreeNodeFlags.DefaultOpen`)

---

## Phase 2: Camera Mode Understanding & Infrastructure

### Task 2.1: Document Camera Control Modes
**File**: Create `caTTY.SkunkworksGameMod/Camera/CameraModes.md`

**Objective**: Document the different camera control modes available in KSA and how they're exposed through the Camera API.

**Research and Document**:
1. **Native KSA Camera Modes**:
   - **Following Mode**: Camera following an astronomical object via `Camera.Following` property and `SetFollow()` method
   - **Free Camera Mode**: Camera not following any object (after `Unfollow()` call)
   
2. **Custom Implementation Modes** (in our code):
   - **Manual Follow Mode**: Custom tracking system that unfollows in KSA but manually updates position to maintain follow behavior
   
3. **Camera Control Methods** (from temp_examples/README.md):
   - `SetFollow(Astronomical object, bool, bool changeControl, bool alert)` - start following
   - `Unfollow()` - stop following
   - `PositionEcl` property - direct position manipulation
   - `WorldRotation` property - direct rotation manipulation
   - `Translate(double3 offset)` - relative movement
   - `LookAt(double3 target, double3 up)` - orient camera

4. **State Transitions**:
   - Following → Free: call `Unfollow()`
   - Free → Following: call `SetFollow(object, ...)`
   - Following → Manual Follow: call `StartManualFollow()` (custom implementation)
    - Manual Follow → (Exit): **TBD** (requires explicit implementation; see Task 2.2)

**IMPORTANT NOTE (current code mismatch to fix in plan)**:
- In current `KsaCameraService`, `StopManualFollow()` is intentionally *not* an “exit manual follow” operation. It does **not** clear `_isManualFollowing` and does **not** call `SetFollow()`. In effect, manual follow continues.
- Therefore: “Exit manual follow / restore native follow (and any native control/controller mode)” must be implemented as a separate, explicit operation.

**Open question to answer via experiments**:
- What do the boolean parameters on `SetFollow(Astronomical, bool, bool changeControl, bool alert)` actually do in KSA?
  - Especially `changeControl`, which likely toggles a camera control/controller mode that’s separate from follow state.

**Deliverable**: Markdown file documenting all modes, methods, and state transitions with code examples

---

### Task 2.2: Define Manual-Follow “Exit” Semantics (keep both behaviors)
**Files**:
- `caTTY.SkunkworksGameMod/Camera/ICameraService.cs`
- `caTTY.SkunkworksGameMod/Camera/KsaCameraService.cs`

**Objective**: Support two distinct outcomes:
1) Keep the current offset and avoid snapping (required by current orbit/animation behavior).
2) Restore native KSA follow/control behavior (required for “camera basics” mode-switch testing).

**Proposed API** (recommended):
```csharp
public enum ManualFollowExitMode
{
    KeepCurrentOffset,
    RestoreNativeFollow,
}
```

Add a single explicit method:
```csharp
bool ExitManualFollow(
    ManualFollowExitMode mode,
    bool? unknown0 = null,
    bool? changeControl = null,
    bool? alert = null);
```

**Behavior requirements**:
- `KeepCurrentOffset` must preserve existing non-snapping behavior.
- `RestoreNativeFollow` must actually leave manual follow (clear `_isManualFollowing`) and attempt a native `SetFollow(...)` using experimentable flags.
- If native restore fails (wrong target type, missing method, etc.), return false and provide a useful console message.

**Acceptance Criteria**:
- There is a reliable and explicit “exit manual follow” path.
- Existing orbit/animation behavior can still use the non-snapping option.

---

### Task 2.3: Implement Safe `SetFollow` Invocation + Flag Experiment Support
**Files**:
- `caTTY.SkunkworksGameMod/Camera/KsaCameraService.cs`
- (optional helper) `caTTY.SkunkworksGameMod/Camera/SetFollowOptions.cs`

**Objective**: Call `KSA.Camera.SetFollow(...)` robustly even when follow targets are stored as `dynamic`/`object`, and enable systematic experimentation of the boolean parameters.

**Why**:
- The followed object may not be statically typed as `Astronomical`, and a direct call may fail at runtime.
- The “camera mode” requirement likely depends on the `changeControl` flag, so we need a way to vary it.

**Implementation details**:
1. Add a small options record/struct:
   ```csharp
   public readonly record struct SetFollowOptions(
       bool Unknown0,
       bool ChangeControl,
       bool Alert);
   ```
   (Rename `Unknown0` once its meaning is discovered.)

2. Add `TrySetFollow(object target, SetFollowOptions options, out string? error)` implemented via reflection:
   - Locate a `SetFollow` overload by name and parameter count.
   - Validate parameter[0] type compatibility with `target.GetType()`.
   - Invoke with the three bool flags.
   - Return false with an actionable error on failure (expected type, actual type, exception message).

3. Add a “matrix test” plan:
   - Provide UI buttons/toggles to try all 8 combinations of the 3 bools (or at least expose toggles).
   - For each attempt, log: options + `GetCurrentMode()` + `GetNativeControlModeDebug()` before/after.

**Acceptance Criteria**:
- `SetFollow` can be attempted without dynamic binder crashes.
- Failure paths are observable and debuggable.
- Flags are configurable from the UI to explore camera-control-mode behavior.

---

### Task 2.4: Add Best-Effort “Control Mode” Introspection (reflection)
**File**: `caTTY.SkunkworksGameMod/Camera/KsaCameraService.cs`

**Objective**: Expose additional diagnostics about KSA’s internal camera control/controller mode beyond follow/unfollow/manual-follow.

**Implementation ideas**:
- Use reflection on `KSA.Camera` instance to look for likely properties/fields:
  - `ControlMode`, `Controller`, `CameraController`, `Mode`, `State`, etc.
- Return a readable string (type name + value) if found, otherwise `null`.

**Acceptance Criteria**:
- UI can display native controller/mode debug info when discoverable.
- If not discoverable, the feature fails gracefully.

---

### Task 2.5: Add Camera Mode Query/Control Methods to ICameraService
**File**: `caTTY.SkunkworksGameMod/Camera/ICameraService.cs`

**Objective**: Extend the camera service interface to expose camera mode information and mode switching operations.

**Add New Interface Methods**:
```csharp
/// <summary>
/// Gets whether the camera is currently following an object in native KSA follow mode.
/// </summary>
bool IsFollowing { get; }

/// <summary>
/// Starts following the current follow target using native KSA follow mode.
/// Restores the camera to standard following behavior with default offset.
/// </summary>
/// <returns>True if successfully started following, false if no target available.</returns>
bool StartFollowing();

/// <summary>
/// Attempts native KSA SetFollow using the provided flags.
/// This exists specifically to explore the meaning of SetFollow's boolean parameters.
/// </summary>
bool TryStartFollowingWithOptions(bool unknown0, bool changeControl, bool alert);

/// <summary>
/// Stops following and enters free camera mode.
/// Camera position and rotation become fully manual.
/// </summary>
void EnterFreeCameraMode();

/// <summary>
/// Gets the current camera control mode as a human-readable string.
/// E.g., "Following", "Free Camera", "Manual Follow"
/// </summary>
string GetCurrentMode();

/// <summary>
/// Best-effort debug string describing KSA's internal camera controller/control mode.
/// Returns null if not discoverable via reflection.
/// </summary>
string? GetNativeControlModeDebug();

/// <summary>
/// Exits manual follow explicitly. See Task 2.2 for required semantics.
/// </summary>
bool ExitManualFollow(ManualFollowExitMode mode, bool? unknown0 = null, bool? changeControl = null, bool? alert = null);
```

**Rationale**:
- `IsFollowing`: Distinct from `IsManualFollowing` - indicates native KSA follow state
- `StartFollowing()`: Restores normal KSA follow mode (useful after manual control)
- `EnterFreeCameraMode()`: Clearer name than raw `Unfollow()` for UI purposes
- `GetCurrentMode()`: Provides user-friendly mode name for display

**Acceptance Criteria**:
- Interface compiles with new methods
- XML documentation explains each method clearly
- Methods follow existing naming conventions

---

### Task 2.6: Implement Camera Mode Methods in KsaCameraService
**File**: `caTTY.SkunkworksGameMod/Camera/KsaCameraService.cs`

**Objective**: Implement the new camera mode query and control methods added to the interface.

**Implementation Details**:

1. **IsFollowing Property**:
   ```csharp
   public bool IsFollowing
   {
       get
       {
           var camera = GetCamera();
           return camera?.Following != null && !_isManualFollowing;
       }
   }
   ```
   - Returns true only if camera has a Following object AND not in manual follow mode

2. **StartFollowing Method**:
   ```csharp
   public bool StartFollowing()
   {
       var camera = GetCamera();
       if (camera == null) return false;
       
       // If we have a stored follow object from manual follow, use it
       var targetObject = _followedObject ?? camera.Following;
       if (targetObject == null) return false;
       
       // Clear manual follow state
       _isManualFollowing = false;
       _followedObject = null;
       
       // Call SetFollow with default parameters
       // Parameters: (object, bool, bool changeControl, bool alert)
       camera.SetFollow(targetObject, false, false, false);
       
       Console.WriteLine($"[KsaCameraService] Started following in native KSA mode");
       return true;
   }
   ```

3. **EnterFreeCameraMode Method**:
   ```csharp
   public void EnterFreeCameraMode()
   {
       var camera = GetCamera();
       if (camera == null) return;
       
       // Clear manual follow state
       _isManualFollowing = false;
       _followedObject = null;
       
       // Unfollow in KSA
       camera.Unfollow();
       
       Console.WriteLine("[KsaCameraService] Entered free camera mode");
   }
   ```

4. **GetCurrentMode Method**:
   ```csharp
   public string GetCurrentMode()
   {
       if (_isManualFollowing)
           return "Manual Follow";
       
       var camera = GetCamera();
       if (camera?.Following != null)
           return "Following";
       
       return "Free Camera";
   }
   ```

**Acceptance Criteria**:
- All interface methods implemented
- Console logging for mode transitions
- Manual follow state properly cleared when entering other modes
- SetFollow call uses appropriate parameters (research what each bool does if needed)

---

## Phase 3: Camera Basics UI Section

### Task 3.1: Create CameraBasicsPanel Component
**File**: Create `caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs`

**Objective**: Create a new reusable UI component for testing basic camera controls.

**Class Structure**:
```csharp
namespace caTTY.SkunkworksGameMod.UI;

/// <summary>
/// ImGui panel for testing basic camera control operations.
/// Provides UI for mode switching and manual camera movement.
/// </summary>
public class CameraBasicsPanel
{
    private readonly ICameraService _cameraService;
    
    // Movement parameters (UI state)
    private float _moveDistance = 10.0f; // meters
    private float _rotationDegrees = 15.0f; // degrees

    // SetFollow flag experiment UI state
    private bool _setFollowUnknown0 = false;
    private bool _setFollowChangeControl = false;
    private bool _setFollowAlert = false;
    
    public CameraBasicsPanel(ICameraService cameraService)
    {
        _cameraService = cameraService;
    }
    
    /// <summary>
    /// Renders the camera basics panel.
    /// </summary>
    public void Render()
    {
        RenderCameraModeSection();
        ImGui.Spacing();
        ImGui.Separator();
        RenderCameraMovementSection();
    }
    
    private void RenderCameraModeSection() { /* To be implemented in Task 3.2 */ }
    private void RenderCameraMovementSection() { /* To be implemented in Task 3.3 */ }
}
```

**Integration Point**:
- Instantiate in `CameraDebugPanel` constructor: `_cameraBasicsPanel = new CameraBasicsPanel(cameraService);`
- Add field: `private readonly CameraBasicsPanel _cameraBasicsPanel;`
- Call in `Render()` method within a collapsing header (see Task 3.4)

**Acceptance Criteria**:
- Class compiles with proper namespace and documentation
- Constructor accepts and stores ICameraService
- Render method exists with proper structure
- Follows C# naming conventions and code style

---

### Task 3.2: Implement Camera Mode Controls
**File**: `caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs`

**Objective**: Implement the `RenderCameraModeSection()` method to provide UI controls for switching camera modes.

**Implementation**:
```csharp
private void RenderCameraModeSection()
{
    ImGui.SeparatorText("Camera Control Mode");
    
    if (!_cameraService.IsAvailable)
    {
        ImGui.TextColored(new float4(1, 0, 0, 1), "Camera not available");
        return;
    }
    
    // Display current mode
    var currentMode = _cameraService.GetCurrentMode();
    ImGui.Text($"Current Mode: {currentMode}");

    // Display native control/controller debug if available
    var nativeMode = _cameraService.GetNativeControlModeDebug();
    if (!string.IsNullOrWhiteSpace(nativeMode))
        ImGui.TextDisabled($"Native Control Mode: {nativeMode}");
    
    // Display follow target if any
    var followTarget = _cameraService.FollowTarget;
    if (followTarget != null)
    {
        ImGui.TextColored(new float4(0.5f, 1, 0.5f, 1), $"Target: {followTarget}");
    }
    else
    {
        ImGui.TextDisabled("No follow target");
    }
    
    ImGui.Spacing();

    // Mode switching buttons
    ImGui.Text("Switch Mode:");

    bool hasTarget = followTarget != null;

    // Expose SetFollow flags for experimentation
    ImGui.SeparatorText("SetFollow Flags (Experiment)");
    ImGui.TextDisabled("These map to SetFollow(Astronomical, bool unknown0, bool changeControl, bool alert)");
    ImGui.Checkbox("unknown0", ref _setFollowUnknown0);
    ImGui.SameLine();
    ImGui.Checkbox("changeControl", ref _setFollowChangeControl);
    ImGui.SameLine();
    ImGui.Checkbox("alert", ref _setFollowAlert);

    // Native follow (default)
    if (!hasTarget) ImGui.BeginDisabled();
    if (ImGui.Button("Native Follow (default)"))
    {
        if (_cameraService.StartFollowing())
            Console.WriteLine("[CameraBasicsPanel] Switched to native follow (default)");
        else
            Console.WriteLine("[CameraBasicsPanel] Failed to start following (default)");
    }
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Use KSA's built-in follow mode with default parameters");
    if (!hasTarget) ImGui.EndDisabled();

    ImGui.SameLine();

    // Native follow (flags)
    if (!hasTarget) ImGui.BeginDisabled();
    if (ImGui.Button("Native Follow (flags)"))
    {
        if (_cameraService.TryStartFollowingWithOptions(_setFollowUnknown0, _setFollowChangeControl, _setFollowAlert))
            Console.WriteLine("[CameraBasicsPanel] Switched to native follow (flags)");
        else
            Console.WriteLine("[CameraBasicsPanel] Failed to start following (flags)");
    }
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Invoke SetFollow with explicit flags to discover their behavior");
    if (!hasTarget) ImGui.EndDisabled();

    // Free camera
    if (ImGui.Button("Free Camera"))
    {
        _cameraService.EnterFreeCameraMode();
        Console.WriteLine("[CameraBasicsPanel] Switched to free camera mode");
    }
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Stop following; camera becomes fully manual");

    // Manual follow entry/exit
    ImGui.SeparatorText("Manual Follow");

    if (!hasTarget) ImGui.BeginDisabled();
    if (ImGui.Button("Enter Manual Follow"))
    {
        var currentOffset = _cameraService.Position - _cameraService.GetTargetPosition();
        _cameraService.StartManualFollow(currentOffset);
        Console.WriteLine("[CameraBasicsPanel] Entered manual follow mode");
    }
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Custom tracking mode that preserves the current offset");
    if (!hasTarget) ImGui.EndDisabled();

    ImGui.SameLine();

    if (ImGui.Button("Exit Manual Follow (keep offset)"))
    {
        _cameraService.ExitManualFollow(ManualFollowExitMode.KeepCurrentOffset);
        Console.WriteLine("[CameraBasicsPanel] Exit manual follow (keep offset)");
    }

    ImGui.SameLine();

    if (!hasTarget) ImGui.BeginDisabled();
    if (ImGui.Button("Exit Manual Follow (restore native)"))
    {
        _cameraService.ExitManualFollow(
            ManualFollowExitMode.RestoreNativeFollow,
            unknown0: _setFollowUnknown0,
            changeControl: _setFollowChangeControl,
            alert: _setFollowAlert);
        Console.WriteLine("[CameraBasicsPanel] Exit manual follow (restore native)");
    }
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Attempts to restore native follow/control via SetFollow; may snap depending on KSA behavior");
    if (!hasTarget) ImGui.EndDisabled();
}
```

**Key Features**:
- Visual indication of current mode (text display)
- Native mode/controller debug display (best-effort)
- Mode switching includes: native follow (default), native follow (flagged), free camera
- Manual follow includes explicit exit behaviors (keep offset vs restore native)
- Buttons disabled when no follow target available (where applicable)
- Tooltips explaining each mode
- Console logging for debugging

**Acceptance Criteria**:
- Follow/unfollow/manual-follow entry/exit can be exercised independently
- SetFollow flags can be toggled and invoked to observe effects on native control mode
- Buttons are appropriately disabled when target unavailable
- Current mode is displayed accurately
- Tooltips provide clear explanations
- Console logs confirm mode switches

---

### Task 3.3: Implement Camera Movement Controls
**File**: `caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs`

**Objective**: Implement the `RenderCameraMovementSection()` method to provide UI controls for manually moving the camera in various directions.

**Implementation**:
```csharp
private void RenderCameraMovementSection()
{
    ImGui.SeparatorText("Camera Movement");
    
    if (!_cameraService.IsAvailable)
    {
        ImGui.TextColored(new float4(1, 0, 0, 1), "Camera not available");
        return;
    }
    
    // Movement distance slider
    ImGui.SliderFloat("Move Distance (m)", ref _moveDistance, 1.0f, 1000.0f);
    
    ImGui.Spacing();
    ImGui.Text("Camera-Relative Movement:");
    ImGui.TextDisabled("Note: In native follow mode, KSA may overwrite Position each frame; switch to Free Camera or Manual Follow to test movement.");
    
    // Forward/Backward
    if (ImGui.Button("Forward"))
    {
        var forward = _cameraService.Forward;
        var offset = forward * _moveDistance;
        _cameraService.Position += offset;
        Console.WriteLine($"[CameraBasics] Moved forward by {_moveDistance}m");
    }
    ImGui.SameLine();
    if (ImGui.Button("Backward"))
    {
        var forward = _cameraService.Forward;
        var offset = forward * -_moveDistance;
        _cameraService.Position += offset;
        Console.WriteLine($"[CameraBasics] Moved backward by {_moveDistance}m");
    }
    
    // Left/Right
    if (ImGui.Button("Left"))
    {
        var right = _cameraService.Right;
        var offset = right * -_moveDistance;
        _cameraService.Position += offset;
        Console.WriteLine($"[CameraBasics] Moved left by {_moveDistance}m");
    }
    ImGui.SameLine();
    if (ImGui.Button("Right"))
    {
        var right = _cameraService.Right;
        var offset = right * _moveDistance;
        _cameraService.Position += offset;
        Console.WriteLine($"[CameraBasics] Moved right by {_moveDistance}m");
    }
    
    // Up/Down (camera-relative; uses the camera's current Up vector)
    if (ImGui.Button("Up"))
    {
        var up = _cameraService.Up;
        var offset = up * _moveDistance;
        _cameraService.Position += offset;
        Console.WriteLine($"[CameraBasics] Moved up by {_moveDistance}m");
    }
    ImGui.SameLine();
    if (ImGui.Button("Down"))
    {
        var up = _cameraService.Up;
        var offset = up * -_moveDistance;
        _cameraService.Position += offset;
        Console.WriteLine($"[CameraBasics] Moved down by {_moveDistance}m");
    }
    
    ImGui.Spacing();
    ImGui.Separator();
    ImGui.Text("World-Space Movement (ECL):");
    
    // World X-axis
    if (ImGui.Button("+X (ECL)"))
    {
        _cameraService.Position += new double3(_moveDistance, 0, 0);
        Console.WriteLine($"[CameraBasics] Moved +X by {_moveDistance}m");
    }
    ImGui.SameLine();
    if (ImGui.Button("-X (ECL)"))
    {
        _cameraService.Position += new double3(-_moveDistance, 0, 0);
        Console.WriteLine($"[CameraBasics] Moved -X by {_moveDistance}m");
    }
    
    // World Y-axis
    if (ImGui.Button("+Y (ECL)"))
    {
        _cameraService.Position += new double3(0, _moveDistance, 0);
        Console.WriteLine($"[CameraBasics] Moved +Y by {_moveDistance}m");
    }
    ImGui.SameLine();
    if (ImGui.Button("-Y (ECL)"))
    {
        _cameraService.Position += new double3(0, -_moveDistance, 0);
        Console.WriteLine($"[CameraBasics] Moved -Y by {_moveDistance}m");
    }
    
    // World Z-axis
    if (ImGui.Button("+Z (ECL)"))
    {
        _cameraService.Position += new double3(0, 0, _moveDistance);
        Console.WriteLine($"[CameraBasics] Moved +Z by {_moveDistance}m");
    }
    ImGui.SameLine();
    if (ImGui.Button("-Z (ECL)"))
    {
        _cameraService.Position += new double3(0, 0, -_moveDistance);
        Console.WriteLine($"[CameraBasics] Moved -Z by {_moveDistance}m");
    }
    
    ImGui.Spacing();
    ImGui.Separator();
    
    // Reset button
    if (ImGui.Button("Snap to Follow Target"))
    {
        if (_cameraService.FollowTarget != null)
        {
            var targetPos = _cameraService.GetTargetPosition();
            _cameraService.Position = targetPos;
            Console.WriteLine("[CameraBasics] Snapped to follow target position");
        }
    }
    if (ImGui.IsItemHovered())
        ImGui.SetTooltip("Move camera to exactly the follow target's position");
}
```

**Key Features**:
- Configurable movement distance slider (1-1000m)
- Camera-relative movement (Forward, Back, Left, Right, Up, Down) using camera direction vectors
- World-space movement along ECL coordinate axes (+/-X, +/-Y, +/-Z)
- "Snap to Follow Target" button to return to target position
- Console logging for all movements

**Acceptance Criteria**:
- Camera moves correctly in camera-relative directions
- Camera moves correctly in world-space directions
- Movement distance is configurable via slider
- Snap to target button works when target exists
- All movements are logged to console
- Movement works in both following and free camera modes

---

### Task 3.4: Integrate Camera Basics Panel into Main UI
**File**: `caTTY.SkunkworksGameMod/UI/CameraDebugPanel.cs`

**Objective**: Add the Camera Basics panel to the main debug UI within a collapsing header.

**Implementation Steps**:

1. **Add field and constructor parameter**:
   ```csharp
   private readonly CameraBasicsPanel _cameraBasicsPanel;
   
   public CameraDebugPanel(
       ICameraService cameraService,
       ICameraAnimationPlayer animationPlayer)
   {
       _cameraService = cameraService;
       _animationPlayer = animationPlayer;
       _orbitAction = new CameraOrbitRpcAction(cameraService, animationPlayer);
       _previewPanel = new KeyframePreviewPanel();
       _cameraBasicsPanel = new CameraBasicsPanel(cameraService);  // Add this line
   }
   ```

2. **Update Render method** to add collapsing header:
   ```csharp
   public void Render()
   {
       ImGui.SeparatorText("Camera Info");
       RenderCameraInfo();
       
       ImGui.Spacing();
       
       // Camera Basics section
       if (ImGui.CollapsingHeader("Camera Basics", ImGuiTreeNodeFlags.DefaultOpen))
       {
           _cameraBasicsPanel.Render();
       }
       
       ImGui.Spacing();
       
       // Orbit Animation section (from Task 1.1)
       if (ImGui.CollapsingHeader("Orbit Animation"))
       {
           ImGui.SeparatorText("Orbit Action");
           RenderOrbitControls();
           
           ImGui.Spacing();
           ImGui.SeparatorText("Animation Status");
           RenderAnimationStatus();
           
           ImGui.Spacing();
           ImGui.SeparatorText("Keyframe Preview");
           _previewPanel.Render(_cameraService);
       }
   }
   ```

**Acceptance Criteria**:
- Camera Basics appears as a collapsing header above Orbit Animation
- Camera Basics is expanded by default
- Both sections function independently
- UI layout is clean and organized

---

## Phase 4: Testing and Validation

### Task 4.1: Manual Testing Checklist
**File**: Create `plans/investigations/CAMERA_BASICS_TESTING.md`

**Objective**: Document testing procedures and expected behaviors for the camera basics feature.

**Testing Scenarios**:

1. **Camera Mode Switching**:
   - [ ] Start game with camera following a craft
   - [ ] Open Skunkworks window (F11)
   - [ ] Verify "Current Mode" shows "Following"
   - [ ] Click "Free Camera Mode" - verify mode changes
   - [ ] Click "Native Follow Mode" - verify camera re-follows craft
   - [ ] Click "Manual Follow Mode" - verify mode changes
   - [ ] Switch between all three modes multiple times

2. **Camera-Relative Movement**:
   - [ ] Enter Free Camera mode
   - [ ] Test all 6 directional buttons (Forward, Back, Left, Right, Up, Down)
   - [ ] Verify camera moves in expected directions relative to current view
   - [ ] Change move distance slider - verify different movement magnitudes
   - [ ] Rotate camera, test movements again - verify they're relative to new orientation

3. **World-Space Movement**:
   - [ ] Test all 6 ECL axis buttons (+/-X, +/-Y, +/-Z)
   - [ ] Verify movement is consistent regardless of camera orientation
   - [ ] Verify Z-axis movement is truly "up/down" in world space

4. **Follow Target Interaction**:
   - [ ] In Following mode, try movement buttons - observe behavior
   - [ ] In Free Camera mode from a followed object, test "Snap to Follow Target"
   - [ ] Move far away, use snap button, verify return to target position

5. **UI Integration**:
   - [ ] Verify both collapsing headers work independently
   - [ ] Collapse Camera Basics, expand Orbit Animation - verify isolation
   - [ ] Test orbit animation still works after refactoring

6. **Edge Cases**:
   - [ ] Test with no follow target (free camera at game start)
   - [ ] Verify appropriate buttons are disabled
   - [ ] Test mode switching while orbit animation is running
   - [ ] Test movement during animation playback

**Expected Console Output Examples**:
```
[CameraBasicsPanel] Switched to free camera mode
[KsaCameraService] Entered free camera mode
[CameraBasics] Moved forward by 50m
[CameraBasics] Moved +Z by 100m
[CameraBasicsPanel] Switched to native follow mode
[KsaCameraService] Started following in native KSA mode
```

**Acceptance Criteria**:
- All test scenarios documented with checkboxes
- Expected behaviors clearly described
- Console output patterns documented
- Edge cases identified

---

### Task 4.2: Create Integration Test Plan
**File**: `plans/investigations/CAMERA_BASICS_INTEGRATION.md`

**Objective**: Document how Camera Basics interacts with existing features and identify potential issues.

**Integration Points to Verify**:

1. **With Orbit Animation**:
   - Switching to free camera during orbit playback
   - Returning to follow mode after animation ends
   - Manual camera movement affecting animation start position

2. **With Manual Follow Mode**:
   - Transition from manual follow to native follow
   - Animation system using manual follow
   - StopManualFollow behavior with new mode controls

3. **With RPC System**:
   - OSC commands interacting with mode switches
   - Camera state consistency across RPC calls

4. **State Consistency**:
   - `_isManualFollowing` flag consistency
   - `_followedObject` reference management
   - Camera.Following property synchronization

**Potential Issues to Watch For**:
- Mode conflicts between animation system and manual controls
- State leaks when rapidly switching modes
- Position snapping when transitioning between modes
- Console spam from per-frame updates

**Mitigation Strategies**:
- Clear state in EnterFreeCameraMode() and StartFollowing()
- Stop animation when switching to free camera
- Rate-limit console logging for frequent operations

---

## Summary

This implementation plan breaks down the camera basics feature into 12 discrete tasks across 4 phases:

**Phase 1** (1 task): Refactor UI to use collapsing headers
**Phase 2** (3 tasks): Add camera mode infrastructure to ICameraService and implementation
**Phase 3** (4 tasks): Build Camera Basics UI panel with mode switching and movement controls
**Phase 4** (2 tasks): Testing and validation documentation

Each task is self-contained and can be implemented by a separate sub-agent, with clear:
- File locations
- Implementation details with code examples
- Acceptance criteria
- Integration points

The tasks build on each other logically but maintain clear boundaries for parallel development where possible (e.g., documentation tasks can proceed alongside implementation).

