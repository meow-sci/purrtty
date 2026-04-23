# ImGui Per-Frame Performance Analysis

**Scope:** All sub-mods in `catty-ksa/`  
**Architecture context:** The codebase separates ImGui UI rendering (called on every game tick) from core terminal logic. Every function annotated with `[StarMapAfterGui]` or called from a `Render()` / `Update()` method runs at the game's frame rate (typically 60–120 Hz). Any unnecessary allocation, world query, or repeated computation inside these paths taxes every single frame.

---

## Table of Contents

1. [Sub-Mod Inventory](#1-sub-mod-inventory)
2. [How the Per-Frame Render Loop Works](#2-how-the-per-frame-render-loop-works)
3. [Issue 1 — `TerminalRenderKey` Window-Position Cache Thrashing](#3-issue-1--terminalrenderkey-window-position-cache-thrashing)
4. [Issue 2 — Per-Character `ToString()` Allocations in `TerminalGridRenderer`](#4-issue-2--per-character-tostring-allocations-in-terminalgridrenderer)
5. [Issue 3 — Per-Frame `Console.WriteLine` with String Interpolation in `SkunkworksMod`](#5-issue-3--per-frame-consolewriteline-with-string-interpolation-in-skunkworksmod)
6. [Issue 4 — Per-Frame World Queries in `CameraDebugPanel.RenderCameraInfo()`](#6-issue-4--per-frame-world-queries-in-cameradebugpanelrendercamerainfo)
7. [Issue 5 — Per-Frame String Interpolation for Display Labels in `CameraDebugPanel.RenderAnimationStatus()`](#7-issue-5--per-frame-string-interpolation-for-display-labels-in-cameradebugpanelrenderanimationstatus)
8. [Issue 6 — Per-Frame World Queries and String Builds in `CameraBasicsPanel`](#8-issue-6--per-frame-world-queries-and-string-builds-in-camerabasicspanel)
9. [Issue 7 — Per-Keyframe String Allocation in `KeyframePreviewPanel`](#9-issue-7--per-keyframe-string-allocation-in-keyframepreviewpanel)
10. [Issue 8 — Redundant `GetTargetPosition()` Calls in `SkunkworksMod.OnAfterUi()`](#10-issue-8--redundant-gettargetposition-calls-in-skunkworksmodonafterui)
11. [What Is Already Well-Optimized](#11-what-is-already-well-optimized)
12. [Refactor Priority Matrix](#12-refactor-priority-matrix)
13. [Step-by-Step Refactor Instructions](#13-step-by-step-refactor-instructions)

---

## 1. Sub-Mod Inventory

| Sub-mod | Location (relative to `catty-ksa/`) | Role | Runs per frame? |
|---|---|---|---|
| `caTTY.Core` | `caTTY.Core/` | Terminal parser, screen buffer, cursor, attributes | No (event-driven) |
| `caTTY.Display` | `caTTY.Display/` | ImGui rendering, controller, UI panels | **YES** |
| `caTTY.GameMod` | `caTTY.GameMod/` | Game patching entry point | No (startup) |
| `caTTY.SkunkworksGameMod` | `caTTY.SkunkworksGameMod/` | Camera system, animation, debug UI | **YES** |
| `caTTY.CustomShells` | `caTTY.CustomShells/` | Game console shell, game-stuff shell | No (I/O callbacks) |
| `caTTY.CustomShellContract` | `caTTY.CustomShellContract/` | Shell interface contracts | No |
| `caTTY.TermSequenceRpc` | `caTTY.TermSequenceRpc/` | OSC/socket RPC for game integration | No (event-driven) |
| `caTTY.TestApp` | `caTTY.TestApp/` | Standalone ImGui test harness | Test only |
| `caTTY.TestGameMod` | `caTTY.TestGameMod/` | Integration test mod | Test only |
| `caTTY.Display.Playground` | `caTTY.Display.Playground/` | Experimental rendering | Test only |

Only `caTTY.Display` and `caTTY.SkunkworksGameMod` execute code on every frame.

---

## 2. How the Per-Frame Render Loop Works

### caTTY.Display render path (every frame)

```
TerminalController.Render()                              [every frame]
  └─ TerminalUiRender.RenderTerminalContent()            [every frame]
       └─ CachedRenderStrategy.RenderGrid()              [every frame]
            ├─ cache hit → replay cached draw list       [~1 ms]
            └─ cache miss → TerminalGridRenderer.Render() [~10–50 ms]
                  └─ nested loop: rows × cols
                       └─ CachedColorResolver.ResolveCellColors()
                       └─ _fonts.SelectFont()
                       └─ target.AddText(char.ToString()) ← ISSUE
```

### caTTY.SkunkworksGameMod render path (every frame)

```
SkunkworksMod.OnAfterUi()                                [every frame, [StarMapAfterGui]]
  ├─ _cameraService.Update(dt)
  ├─ _animationPlayer.Update(dt)
  ├─ Console.WriteLine(...)                              ← ISSUE (diagnostic logs, 5 frames)
  ├─ _cameraService.GetTargetPosition()   (called ×2)  ← ISSUE (redundant)
  └─ RenderWindow() [if _windowVisible]
       └─ CameraDebugPanel.Render()
            ├─ RenderCameraInfo()
            │    ├─ Program.GetCamera()                  ← ISSUE (world query)
            │    ├─ camera.PositionEcl                   ← ISSUE (world query)
            │    ├─ camera.GetFieldOfView()              ← ISSUE (world query)
            │    └─ $"Position...", $"FOV..."            ← ISSUE (string alloc)
            ├─ AlexsTestPanel.Render()                   [button-triggered only, OK]
            ├─ CameraBasicsPanel.Render()
            │    ├─ _cameraService.GetCurrentMode()      ← ISSUE (world query)
            │    ├─ _cameraService.GetNativeControlModeDebug() ← ISSUE (world query)
            │    ├─ _cameraService.FollowTarget (×3)     ← ISSUE (repeated access)
            │    └─ $"Current Mode...", $"Target..."     ← ISSUE (string alloc)
            └─ KeyframePreviewPanel.Render()
                 └─ for each keyframe:
                      $"[{kf.Timestamp:F2}s] ..."       ← ISSUE (N allocs/frame)
                      $"Pos(...)", $"YPR(...)", $"FOV()" ← ISSUE (3N allocs/frame)
```

---

## 3. Issue 1 — `TerminalRenderKey` Window-Position Cache Thrashing

### Files affected
- `caTTY.Display/Rendering/TerminalViewportRenderCache.cs` — `TerminalRenderKey` struct, lines 28–72

### What was discovered

`TerminalRenderKey` includes the ImGui window's screen position (`WindowX`, `WindowY`) as part of its equality check:

```csharp
// TerminalViewportRenderCache.cs:71-72
Math.Abs(WindowX - other.WindowX) < 0.001f &&
Math.Abs(WindowY - other.WindowY) < 0.001f &&
```

The key is constructed every frame in `TerminalUiRender.RenderTerminalContent()` using `ImGui.GetCursorScreenPos()`. When the user drags the terminal window, the screen position changes by at least one pixel every mouse-move event. Because the epsilon is `0.001f` (sub-pixel precision), virtually any pixel movement makes `Equals()` return `false`, causing a cache miss.

**A cache miss triggers `TerminalGridRenderer.Render()`** — a full nested row×column loop over every visible cell (~2,000–10,000 iterations), color resolution, font selection, draw call emission, and screen capture. This typically costs 10–50 ms per frame.

### Why the window position is in the key

The window position was added to the key so the cached draw list is invalidated when the window moves. However, cached ImGui draw commands store **absolute screen-space positions**. When the window moves, those coordinates are stale. The original intent was correct: if positions are baked in, the cache must bust on movement.

### The real fix: store draw commands in window-relative coordinates

Rather than busting the entire cache on movement, the render cache should record draw commands relative to the terminal's local origin, then **offset them at replay time** by the current `windowPos`. This way the cache remains valid across window drags; only the draw offset changes each frame, which is a trivially cheap operation.

If switching to relative coordinates is too large a refactor in one step, an acceptable interim fix is to **remove `WindowX`/`WindowY` from the key entirely and instead store them as a separate `DrawOffset` that is applied at replay**. The cache content itself does not change when the window moves — only where it is drawn on screen.

### Impact
**Critical.** Any user who drags the terminal window gets a full re-render every frame for the entire drag duration. This can spike frame time from ~1 ms (cache hit) to ~10–50 ms (full re-render) for every frame the window is in motion.

---

## 4. Issue 2 — Per-Character `ToString()` Allocations in `TerminalGridRenderer`

### Files affected
- `caTTY.Display/Rendering/TerminalGridRenderer.cs` — lines 155 and 430

### What was discovered

Inside the hot cell-rendering loop (executed on every cache miss), each character is converted to a `string` via `char.ToString()`:

```csharp
// Line 155 — main render path (inside run-flush loop)
target.AddText(charPos, runColorU32, runChars[i].ToString(), runFont, _fonts.CurrentFontConfig.FontSize);

// Line 430 — selection overlay path
target.AddText(pos, fgColU32, cell.Character.ToString(), font, fontSize);
```

`char.ToString()` always allocates a new one-character `string` on the managed heap because `string` is a reference type and there is no interning for dynamically generated single-character strings. On a typical 80×24 terminal, a full render emits up to 1,920 visible characters, meaning up to **1,920 heap allocations per cache miss** just from this one pattern. On a 200×50 terminal this rises to 10,000.

These allocations:
- Create GC pressure (each is a short-lived `gen0` object)
- May trigger GC collections mid-frame, causing frame spikes
- Collectively account for a measurable fraction of render time on cache-miss frames

### Root cause

The `AddText` API (on `ITerminalDrawTarget` / `ImGuiDrawTarget`) currently accepts `string`. The backing ImGui API (`drawList.AddText`) also works with strings. C# does not have a built-in runtime string table for non-literal single characters.

### Fix

**Option A (preferred): Add a `ReadOnlySpan<char>` or `char[]` overload to `ITerminalDrawTarget.AddText`.**  
The underlying ImGui binding may support `ReadOnlySpan<char>` or a `byte*` path. If the binding wraps `ImDrawList_AddText_Str`, it typically accepts a pointer; a `Span<char>` overload can be added to `ImGuiDrawTarget` and the interface that avoids the allocation entirely for the already-buffered `runChars` array.

**Option B (quick win): Pre-allocate a static `string[]` lookup table of all 128 (or 256) ASCII characters.**

```csharp
// Add as a static field to TerminalGridRenderer (or a shared utility class)
private static readonly string[] _singleCharStrings = Enumerable.Range(0, 256)
    .Select(i => ((char)i).ToString())
    .ToArray();

// Replace:
target.AddText(charPos, runColorU32, runChars[i].ToString(), ...);
// With:
int charVal = runChars[i];
string charStr = charVal < 256 ? _singleCharStrings[charVal] : runChars[i].ToString();
target.AddText(charPos, runColorU32, charStr, ...);
```

Option B is a two-line change with immediate impact and no API changes. Option A is cleaner and eliminates the string entirely but requires touching the `ITerminalDrawTarget` interface and all implementors.

### Impact
**High.** On every cache miss this doubles the number of managed heap allocations in the render path. The more active the terminal (frequent content changes = frequent cache misses), the greater the cumulative GC pressure.

---

## 5. Issue 3 — Per-Frame `Console.WriteLine` with String Interpolation in `SkunkworksMod`

### Files affected
- `caTTY.SkunkworksGameMod/SkunkworksMod.cs` — `OnAfterUi()`, lines approximately 104–132

### What was discovered

`OnAfterUi()` is annotated with `[StarMapAfterGui]` and runs on **every game frame**. Inside it, there are two diagnostic logging blocks that call `Console.WriteLine` with interpolated strings:

**Block 1 — Animation state transition logging** (runs on the frame animation ends):
```csharp
Console.WriteLine("[Skunkworks] Animation state transitioned from playing to stopped");
Console.WriteLine($"[Skunkworks] Current camera position: {_cameraService.Position}");
Console.WriteLine($"[Skunkworks] Is manual following: {_cameraService.IsManualFollowing}");
// ...
Console.WriteLine("[Skunkworks] Calling StopManualFollow to restore camera mode...");
Console.WriteLine($"[Skunkworks] After StopManualFollow, camera position: {_cameraService.Position}");
```
This block executes on a single transition frame; not strictly per-frame, but it performs world queries (`_cameraService.Position`) inside a string interpolation.

**Block 2 — Position stability diagnostic** (runs every frame for exactly 5 frames after restore):
```csharp
if (_framesSinceRestore >= 0 && _framesSinceRestore < 5 && _cameraService != null)
{
    var currentPos = _cameraService.Position;          // world query per frame
    var targetPos = _cameraService.GetTargetPosition(); // world query per frame
    var currentOffset = currentPos - targetPos;
    Console.WriteLine($"[Skunkworks] Frame {_framesSinceRestore} after restore:");
    Console.WriteLine($"[Skunkworks]   Position: {currentPos}");
    Console.WriteLine($"[Skunkworks]   Offset from target: {currentOffset}");
    _framesSinceRestore++;
}
```

Block 2 is **purely diagnostic** code — it was written to check camera stability after a restore and has no functional effect. Yet it executes two world queries and three `Console.WriteLine` calls (each with a string interpolation allocation) on every frame for five consecutive frames after every animation playback.

`Console.WriteLine` also calls into the OS (buffered I/O), making it non-trivially expensive in a hot path.

### Fix

**Remove Block 2 entirely** (or guard it behind a compile-time `#if DEBUG` or a `bool _verboseDiagnostics` field that is `false` by default). The data it provided was one-time diagnostic information that has presumably served its purpose.

For Block 1, the `Console.WriteLine` calls that include live camera positions should either be removed or moved to a diagnostic helper that only allocates when a diagnostic flag is enabled. The plain string literals (no interpolation) are fine since they are interned at compile time.

### Impact
**Medium.** Executed for 5 frames per animation playback event. While not constant, it pollutes the per-frame budget during the exact moment the camera system is doing important work (restoring from animation) and will cause GC churn from the string allocations.

---

## 6. Issue 4 — Per-Frame World Queries in `CameraDebugPanel.RenderCameraInfo()`

### Files affected
- `caTTY.SkunkworksGameMod/UI/CameraDebugPanel.cs` — `RenderCameraInfo()`, approximately lines 96–128

### What was discovered

`RenderCameraInfo()` is called from `Render()`, which is called every frame the debug window is open. Inside it, the following world state queries execute unconditionally every frame:

```csharp
var camera = Program.GetCamera();           // service locator / world query
var pos = camera.PositionEcl;              // reads live camera position from game world
var fov = camera.GetFieldOfView() * 57.2958f; // reads live FOV, does float multiply
```

`Program.GetCamera()` is a service-locator call. Depending on implementation it may perform a dictionary lookup, a reflection query, or a singleton dereference. Even in the best case (singleton) it is an indirection that runs every frame just to display a position label.

`PositionEcl` and `GetFieldOfView()` read live state from the game's camera object — these are correct to query every frame (they change continuously), but the **string allocations derived from them** are wasteful (see Issue 5).

The more concerning pattern here is `Program.GetCamera()` being invoked per frame. This should be cached as a field during initialization.

### Fix

Cache the camera reference as a private field in `CameraDebugPanel`. Set it during construction (or lazily on first `Render()` call with a null guard). Do not call `Program.GetCamera()` on every frame:

```csharp
// Field
private readonly ICamera _camera;  // or whatever the return type of Program.GetCamera() is

// Constructor
public CameraDebugPanel(ICameraService cameraService, ICameraAnimationPlayer animationPlayer)
{
    _cameraService = cameraService;
    _animationPlayer = animationPlayer;
    _camera = Program.GetCamera();   // ← call once, store result
    // ...
}

// RenderCameraInfo — use _camera directly, no GetCamera() call
private void RenderCameraInfo()
{
    if (!_cameraService.IsAvailable) { /* ... */ return; }
    var pos = _camera.PositionEcl;
    // ...
}
```

Note: If the camera object reference can change during the session (e.g., level reload), a lazy refresh strategy with a dirty flag is appropriate rather than caching forever.

### Impact
**Medium.** `Program.GetCamera()` runs on every frame the debug window is open. The cost depends on the implementation of `GetCamera()`, but any non-trivial lookup inside a 60+ Hz loop is undesirable.

---

## 7. Issue 5 — Per-Frame String Interpolation for Display Labels in `CameraDebugPanel.RenderAnimationStatus()`

### Files affected
- `caTTY.SkunkworksGameMod/UI/CameraDebugPanel.cs` — `RenderCameraInfo()` lines ~115–121 and `RenderAnimationStatus()` lines ~241–261

### What was discovered

Every frame the debug panel is visible, these interpolated strings are freshly allocated:

**In `RenderCameraInfo()`:**
```csharp
ImGui.Text($"Position (default): ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})");
ImGui.Text($"FOV: {fov:F1}°");
```

**In `RenderAnimationStatus()` (only while animation is playing):**
```csharp
ImGui.Text($"{_animationPlayer.CurrentTime:F2}s / {_animationPlayer.Duration:F2}s");
```

**In `RenderAnimationStatus()` (always):**
```csharp
ImGui.Text($"Keyframes loaded: {_animationPlayer.Keyframes.Count}");
```

Each of these creates a new `string` object on the heap every frame. The position/FOV strings change every frame (camera moves), so they cannot be cached across frames — however, they can be formatted more efficiently.

The keyframe count (`_animationPlayer.Keyframes.Count`) is particularly wasteful: keyframes only change when a new animation is generated (button click), yet the string `"Keyframes loaded: 42"` is reallocated every frame.

### Fix

**For position/FOV/time strings (change every frame):** Use `ImGui.Text` with format args if the binding supports it, or use a `Span<char>`-based formatting approach (e.g., `string.Create` or `stackalloc` + `TryFormat`) to avoid heap allocation. As a simpler interim: these strings are small and short-lived (`gen0`), so the GC impact is low. The main concern is call volume; 60 fps × 4 strings = 240 allocations/second from this panel alone. This is acceptable if the debug panel is only used during development.

**For the keyframe count string (rarely changes):** Cache this string and only regenerate it when keyframes are set:

```csharp
// In CameraDebugPanel fields:
private string _cachedKeyframeCountLabel = "Keyframes loaded: 0";
private int _lastKnownKeyframeCount = 0;

// In RenderAnimationStatus():
int count = _animationPlayer.Keyframes.Count;
if (count != _lastKnownKeyframeCount)
{
    _cachedKeyframeCountLabel = $"Keyframes loaded: {count}";
    _lastKnownKeyframeCount = count;
}
ImGui.Text(_cachedKeyframeCountLabel);
```

This is a one-time allocation per change event rather than 60 allocations per second.

### Impact
**Low–Medium.** This is a debug-only panel, so the practical impact in production is zero. However, for developer QoL and as a demonstration of the correct pattern for future UI panels, it should be fixed.

---

## 8. Issue 6 — Per-Frame World Queries and String Builds in `CameraBasicsPanel`

### Files affected
- `caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs` — `RenderCameraModeSection()`, approximately lines 50–100

### What was discovered

`RenderCameraModeSection()` is called every frame the debug window is open. Inside it:

```csharp
var currentMode = _cameraService.GetCurrentMode();              // world query, returns string or enum
var nativeMode = _cameraService.GetNativeControlModeDebug();    // world query, may build a debug string
var followTarget = _cameraService.FollowTarget;                 // world query, returns object reference

ImGui.Text($"Current Mode: {currentMode}");                     // interpolation alloc
if (!string.IsNullOrWhiteSpace(nativeMode))
    ImGui.TextDisabled($"Native Control Mode: {nativeMode}");   // interpolation alloc (if non-empty)
if (followTarget != null)
    ImGui.TextColored(..., $"Target: {followTarget}");          // calls followTarget.ToString() + alloc
```

**`GetNativeControlModeDebug()` is particularly suspicious.** The name implies it builds a debug diagnostic string. If this method allocates inside the camera service on each call (e.g., via string concatenation or reflection), that is an allocation in the critical path every frame.

**`_cameraService.FollowTarget` is accessed three times** across the same `Render()` call (once in `RenderCameraModeSection()` to check for null/get value, and implicitly again through `hasTarget` usage). Each access may go through a property getter that does work.

### Fix

1. **Cache `GetNativeControlModeDebug()`** — this string is unlikely to change every frame. Store it as a field `_cachedNativeMode` and refresh it only when the camera mode changes (via an event or a dirty flag). If `ICameraService` does not expose a mode-changed event, add one.

2. **Cache `GetCurrentMode()` return value** — similarly, the mode label string (or enum) should be cached and only regenerated on mode transition. Use the same mode-changed event or dirty flag pattern.

3. **Cache `FollowTarget` in a local variable** at the top of `RenderCameraModeSection()` (this is already partially done with the `followTarget` variable, but verify it is not re-queried via `_cameraService.FollowTarget` again later in the function).

Example pattern:
```csharp
private void RenderCameraModeSection()
{
    if (!_cameraService.IsAvailable) { /* ... */ return; }

    // Query once, use multiple times
    var followTarget = _cameraService.FollowTarget;
    bool hasTarget = followTarget != null;

    // Use cached strings (refreshed by event/dirty flag, not per-frame)
    ImGui.Text(_cachedCurrentModeLabel);
    if (_cachedNativeMode is { Length: > 0 })
        ImGui.TextDisabled(_cachedNativeModeLabel);
    // ...
}
```

### Impact
**Low–Medium.** As with Issue 5, this panel is debug-only. However, `GetNativeControlModeDebug()` has an unknown implementation cost that warrants investigation.

---

## 9. Issue 7 — Per-Keyframe String Allocation in `KeyframePreviewPanel`

### Files affected
- `caTTY.SkunkworksGameMod/UI/KeyframePreviewPanel.cs` — `Render()`, approximately lines 38–75

### What was discovered

When keyframes are loaded for preview, `Render()` loops over every keyframe and constructs **four separate interpolated strings per keyframe, per frame**:

```csharp
for (int i = 0; i < _previewKeyframes.Count; i++)
{
    var kf = _previewKeyframes[i];
    ImGui.PushID(i);

    string label = kf.DebugLabel != null
        ? $"[{kf.Timestamp:F2}s] {kf.DebugLabel}"   // alloc #1
        : $"[{kf.Timestamp:F2}s]";                   // alloc #1

    ImGui.Text(label);
    ImGui.SameLine();
    ImGui.TextDisabled($"Pos({kf.Offset.X:F1}, {kf.Offset.Y:F1}, {kf.Offset.Z:F1})");  // alloc #2
    ImGui.SameLine();
    ImGui.TextDisabled($"YPR({kf.Yaw:F1}, {kf.Pitch:F1}, {kf.Roll:F1})");              // alloc #3
    ImGui.SameLine();
    ImGui.TextDisabled($"FOV({kf.Fov:F1})");                                            // alloc #4

    ImGui.SameLine();
    if (ImGui.SmallButton("Apply")) { ApplyKeyframe(kf, cameraService); }

    ImGui.PopID();
}
```

**Keyframes are static once loaded.** They are set by `SetPreviewKeyframes()` (called only when the "Preview Keyframes" button is clicked) and cleared by `ClearPreview()`. Between those events, the data never changes — yet all four strings are reallocated every frame.

**Scale:** An orbit animation with a 5-second duration at typical keyframe density generates 50–200 keyframes. At 4 strings per keyframe, this is **200–800 string allocations per frame** just from this panel, as long as the preview is loaded and the "Orbit Animation" collapsible section is expanded.

### Fix

Pre-compute all display strings when keyframes are set in `SetPreviewKeyframes()`, and cache them in a parallel array (or a list of a small display-data struct). Clear the cache in `ClearPreview()`.

```csharp
// Add to KeyframePreviewPanel:
private readonly record struct KeyframeDisplayStrings(
    string Label,
    string Position,
    string Ypr,
    string Fov);

private List<KeyframeDisplayStrings> _previewDisplayStrings = new();

public void SetPreviewKeyframes(IEnumerable<CameraKeyframe> keyframes)
{
    _previewKeyframes.Clear();
    _previewKeyframes.AddRange(keyframes);

    _previewDisplayStrings.Clear();
    foreach (var kf in _previewKeyframes)
    {
        _previewDisplayStrings.Add(new KeyframeDisplayStrings(
            Label: kf.DebugLabel != null
                ? $"[{kf.Timestamp:F2}s] {kf.DebugLabel}"
                : $"[{kf.Timestamp:F2}s]",
            Position: $"Pos({kf.Offset.X:F1}, {kf.Offset.Y:F1}, {kf.Offset.Z:F1})",
            Ypr: $"YPR({kf.Yaw:F1}, {kf.Pitch:F1}, {kf.Roll:F1})",
            Fov: $"FOV({kf.Fov:F1})"
        ));
    }
}

public void ClearPreview()
{
    _previewKeyframes.Clear();
    _previewDisplayStrings.Clear();   // ← also clear display strings
}

// In Render():
for (int i = 0; i < _previewKeyframes.Count; i++)
{
    var ds = _previewDisplayStrings[i];   // no alloc — reads cached strings
    ImGui.PushID(i);
    ImGui.Text(ds.Label);
    ImGui.SameLine();
    ImGui.TextDisabled(ds.Position);
    ImGui.SameLine();
    ImGui.TextDisabled(ds.Ypr);
    ImGui.SameLine();
    ImGui.TextDisabled(ds.Fov);
    ImGui.SameLine();
    if (ImGui.SmallButton("Apply")) { ApplyKeyframe(_previewKeyframes[i], cameraService); }
    ImGui.PopID();
}
```

### Impact
**High** (within the SkunkworksMod scope). This is the single largest source of per-frame allocations in the debug mod — up to 800 allocations per frame for a typical orbit preview. Because keyframes are immutable once loaded, the fix is straightforward with zero semantic risk.

---

## 10. Issue 8 — Redundant `GetTargetPosition()` Calls in `SkunkworksMod.OnAfterUi()`

### Files affected
- `caTTY.SkunkworksGameMod/SkunkworksMod.cs` — `OnAfterUi()`, lines approximately 87 and 127

### What was discovered

Within a single execution of `OnAfterUi()`, `_cameraService.GetTargetPosition()` is called twice:

```csharp
// First call — inside animation frame application block
if (animFrame.HasValue && _cameraService != null)
{
    var targetPos = _cameraService.GetTargetPosition();   // ← call #1
    _cameraService.Position = targetPos + frame.Offset;
    _cameraService.LookAt(targetPos);
    _cameraService.UpdateFollowOffset(frame.Offset);
}

// Second call — inside diagnostic tracking block (5 frames after restore)
if (_framesSinceRestore >= 0 && _framesSinceRestore < 5 && _cameraService != null)
{
    var currentPos = _cameraService.Position;
    var targetPos = _cameraService.GetTargetPosition();   // ← call #2 (same frame)
    var currentOffset = currentPos - targetPos;
    // ...
}
```

`GetTargetPosition()` queries the game world for the position of the camera's follow target — this is a potentially non-trivial operation (object position lookup in the game's scene graph). Calling it twice in the same frame is redundant when both calls should return the same value (the target does not move between the two calls within a single `OnAfterUi` execution).

Note: If Issue 3 (diagnostic logging) is resolved by removing the second block entirely, this issue disappears automatically. However, if the second block is kept (e.g., moved behind a debug flag), it should reuse the result from the first call.

### Fix

Compute `GetTargetPosition()` once per frame at the top of `OnAfterUi()` if the follow target is active, and pass it to any code that needs it:

```csharp
public void OnAfterUi(double dt)
{
    if (!_isInitialized || _isDisposed) return;

    bool isCurrentlyPlaying = _animationPlayer?.IsPlaying ?? false;
    _cameraService?.Update(dt);

    // Compute target position once for this frame
    var targetPos = _cameraService?.GetTargetPosition() ?? default;

    var animFrame = _animationPlayer?.Update(dt);
    if (animFrame.HasValue && _cameraService != null)
    {
        var frame = animFrame.Value;
        _cameraService.Position = targetPos + frame.Offset;
        _cameraService.LookAt(targetPos);
        _cameraService.FieldOfView = frame.Fov;
        _cameraService.UpdateFollowOffset(frame.Offset);
    }
    // ... rest of method uses targetPos without calling GetTargetPosition() again
}
```

### Impact
**Low.** The second call only occurs for 5 frames per animation completion event. This is a correctness/hygiene fix more than a performance emergency.

---

## 11. What Is Already Well-Optimized

The following patterns were examined and found to be **already optimized** — they should not be changed:

| Component | Optimization present |
|---|---|
| `CachedRenderStrategy` | Full frame-level render cache; skips `TerminalGridRenderer` entirely on cache hit |
| `TerminalGridRenderer` — buffer management | Uses `ArrayPool<T>` for all temporary buffers; no per-frame heap allocations for row/column arrays |
| `TerminalGridRenderer` — dirty row tracking | Skips rendering of rows that have not changed since last frame |
| `TerminalGridRenderer` — run batching | Merges adjacent cells with identical color+font into a single `AddText` call |
| `TerminalGridRenderer` — early exit | Skips rendering entirely for empty/default cells |
| `CachedColorResolver` | Caches ANSI 16-color array, 240-entry indexed color array, and a dictionary of up to 4,096 custom RGB colors; the hot-path `ResolveCellColors()` returns pre-computed `uint32` |
| `CachedColorResolver.ColorToU32()` | Marked `[MethodImpl(MethodImplOptions.AggressiveInlining)]` |
| `TerminalUiFonts.SelectFont()` | Font pointers are pre-loaded; `SelectFont()` is a pure attribute lookup |
| `TerminalController.Update()` | Only updates cursor blink state — minimal work |
| `AlexsTestPanel` — LINQ on button click | The `Universe...OfType<Vehicle>().ToList()` chain is inside `if (ImGui.Button(...))` — it only executes when the button is actually clicked, not every frame |

---

## 12. Refactor Priority Matrix

| # | Issue | File(s) | Frames affected | Allocs/frame | Effort | Priority |
|---|---|---|---|---|---|---|
| 1 | Window-position cache thrashing | `TerminalViewportRenderCache.cs` | Every frame window is dragged | Full re-render (~10–50 ms) | Medium | **Critical** |
| 2 | Per-char `ToString()` in grid renderer | `TerminalGridRenderer.cs:155,430` | Cache miss frames | 1,920–10,000 | Low | **High** |
| 7 | Per-keyframe string allocation in preview panel | `KeyframePreviewPanel.cs` | All frames with preview loaded | 200–800 | Low | **High** |
| 3 | Per-frame Console.WriteLine + string interp (diagnostic) | `SkunkworksMod.cs` | 5 frames per animation end | ~3 | Trivial | Medium |
| 4 | Per-frame `Program.GetCamera()` call | `CameraDebugPanel.cs` | All frames window open | 0 (no alloc, but world query) | Low | Medium |
| 6 | Per-frame world queries + string builds in camera basics panel | `CameraBasicsPanel.cs` | All frames window open | 2–3 | Low | Medium |
| 8 | Redundant `GetTargetPosition()` | `SkunkworksMod.cs` | 5 frames per animation end | 0 | Trivial | Low |
| 5 | Per-frame string interpolation in animation status | `CameraDebugPanel.cs` | All frames window open | 3–4 | Low | Low |

---

## 13. Step-by-Step Refactor Instructions

Each step below is self-contained and can be given to a coding agent independently. Steps are ordered by priority.

---

### Step 1 — Fix `TerminalRenderKey` cache thrashing on window movement

**Context:**  
File: `caTTY.Display/Rendering/TerminalViewportRenderCache.cs`  
File: `caTTY.Display/Controllers/TerminalUi/TerminalUiRender.cs`  

The `TerminalRenderKey` struct stores `WindowX` and `WindowY` (the absolute screen position of the terminal window) and uses them in `Equals()` with a `0.001f` epsilon. Any sub-pixel window movement invalidates the cache and triggers a full re-render.

**Goal:** The render cache should not bust when the window moves. Window position only determines *where* the cached content is drawn, not *what* the content is.

**Task:**

1. Remove `WindowX` and `WindowY` from `TerminalRenderKey`. Remove them from the constructor, `Equals()`, `GetHashCode()`, and the struct fields. The struct will have 9 fields instead of 11.

2. In `TerminalViewportRenderCache` (or the draw/replay method of the render cache), change the cache draw method to accept a `drawOffset` parameter (`float2`) instead of relying on baked-in absolute positions. When replaying the cache, add the current `windowPos` as an offset to every recorded draw command. (If the underlying cache implementation records ImGui draw list commands as absolute positions, this may require storing all positions as relative-to-origin offsets at capture time and then adding the current origin at replay. Consult `CachedRenderStrategy.cs` and the cache backing store implementation for how captured draw commands are replayed.)

3. Update the call site in `TerminalUiRender.RenderTerminalContent()` where `TerminalRenderKey` is constructed — remove the `windowPos.X` and `windowPos.Y` arguments. Pass the current `windowPos` separately to the render strategy's `RenderGrid()` or the cache's draw method.

4. Verify that the cache still invalidates correctly when actual content changes (screen buffer revision), when the theme changes, or when the font/size changes — these are all still in the key.

5. Write a comment in `TerminalRenderKey` explaining that window position is intentionally excluded because it is applied as a draw offset at replay time.

**Acceptance criteria:**  
Dragging the terminal window should show no noticeable frame rate drop or stutter. The performance counter for `TerminalController.Render` should remain at its cache-hit baseline (~1 ms) during a window drag.

---

### Step 2 — Eliminate per-character `char.ToString()` heap allocations in `TerminalGridRenderer`

**Context:**  
File: `caTTY.Display/Rendering/TerminalGridRenderer.cs` lines 155 and 430  
File: `caTTY.Display/Rendering/ITerminalDrawTarget.cs` (interface)  
File: `caTTY.Display/Rendering/ImGuiDrawTarget.cs` (implementation)  

Two call sites call `char.ToString()` per visible character, causing thousands of heap allocations per cache-miss render.

**Task (Option A — preferred):**

1. Add a new overload to `ITerminalDrawTarget`:
   ```csharp
   void AddText(float2 pos, uint color, char singleChar, ImFontPtr font, float fontSize);
   ```

2. Implement this overload in `ImGuiDrawTarget`. Use a `stackalloc char[1]` buffer or the `Span<char>`-based path of the ImGui binding if available. If the underlying `ImDrawList.AddText` only accepts `string`, use the string-table approach below as the implementation body.

3. Replace the two call sites in `TerminalGridRenderer` to use the new `char` overload.

**Task (Option B — quick win if Option A is not feasible due to binding constraints):**

1. Add a static lookup table to `TerminalGridRenderer`:
   ```csharp
   private static readonly string[] _charStringCache =
       Enumerable.Range(0, 256).Select(i => ((char)i).ToString()).ToArray();
   ```

2. At line 155, replace:
   ```csharp
   target.AddText(charPos, runColorU32, runChars[i].ToString(), runFont, _fonts.CurrentFontConfig.FontSize);
   ```
   With:
   ```csharp
   int cv = runChars[i];
   string cs = cv < 256 ? _charStringCache[cv] : runChars[i].ToString();
   target.AddText(charPos, runColorU32, cs, runFont, _fonts.CurrentFontConfig.FontSize);
   ```

3. At line 430, apply the same pattern for `cell.Character.ToString()`.

**Acceptance criteria:**  
On a cache-miss frame (detectable via the perf counter), allocation profiling should show zero `String` allocations from `TerminalGridRenderer.Render()` for printable ASCII characters. Non-ASCII characters will still fall back to `ToString()` under Option B, which is acceptable.

---

### Step 3 — Pre-compute keyframe display strings in `KeyframePreviewPanel`

**Context:**  
File: `caTTY.SkunkworksGameMod/UI/KeyframePreviewPanel.cs`  

The `Render()` method creates 4 interpolated strings per keyframe per frame. Keyframes only change when `SetPreviewKeyframes()` is called.

**Task:**

1. Add a private `readonly record struct KeyframeDisplayStrings` with four `string` fields: `Label`, `Position`, `Ypr`, `Fov`.

2. Add a `List<KeyframeDisplayStrings> _previewDisplayStrings = new()` field.

3. In `SetPreviewKeyframes()`, after `_previewKeyframes.AddRange(keyframes)`, populate `_previewDisplayStrings` by iterating over `_previewKeyframes` and building all four strings once per keyframe. Use the exact same format strings currently in `Render()`.

4. In `ClearPreview()`, also call `_previewDisplayStrings.Clear()`.

5. In `Render()`, replace the four `$"..."` interpolations inside the loop with reads from `_previewDisplayStrings[i].Label`, `.Position`, `.Ypr`, `.Fov`. No other logic changes.

**Acceptance criteria:**  
Calling `SetPreviewKeyframes()` once should populate the string cache. Subsequent frames should show zero `String` allocations from `KeyframePreviewPanel.Render()` in an allocation profiler.

---

### Step 4 — Remove (or guard) per-frame diagnostic Console.WriteLine in `SkunkworksMod`

**Context:**  
File: `caTTY.SkunkworksGameMod/SkunkworksMod.cs` — `OnAfterUi()`, the `_framesSinceRestore` tracking block (lines ~120–133)  

**Task:**

1. Evaluate whether the position-stability diagnostic (the `_framesSinceRestore` 5-frame tracking loop) is still needed. If it was temporary debugging code, **delete the entire block**, the `_framesSinceRestore` field, and any references to it. Also delete the line `_framesSinceRestore = 0;` in the animation restore block above it.

2. If the diagnostic is deliberately preserved, wrap the entire block in `#if DEBUG` compiler directives:
   ```csharp
   #if DEBUG
   if (_framesSinceRestore >= 0 && _framesSinceRestore < 5 && _cameraService != null)
   {
       // ...
   }
   #endif
   ```
   Also move the field declaration inside `#if DEBUG`.

3. For the animation state transition `Console.WriteLine` calls (the non-interpolated ones like `"[Skunkworks] Animation state transitioned..."` and the interpolated ones like `$"Current camera position: {_cameraService.Position}"`): keep the non-interpolated literals (they use interned strings) but wrap the interpolated ones in `#if DEBUG` or remove them. These execute only on transition frames, not every frame, so they are lower priority than the 5-frame loop.

**Acceptance criteria:**  
In a release build (or with the diagnostic removed), a profiler/log output should show zero console writes during the 5 frames following animation completion. The `_framesSinceRestore` field should not exist in non-debug builds.

---

### Step 5 — Cache `Program.GetCamera()` in `CameraDebugPanel`

**Context:**  
File: `caTTY.SkunkworksGameMod/UI/CameraDebugPanel.cs` — `RenderCameraInfo()`, line calling `Program.GetCamera()`  

**Task:**

1. Determine the return type of `Program.GetCamera()` (likely an interface or concrete camera class from the KSA game API).

2. Add a private field of that type: `private readonly <CameraType> _camera;`

3. In the constructor of `CameraDebugPanel`, call `Program.GetCamera()` once and assign it to `_camera`.

4. In `RenderCameraInfo()`, remove the `var camera = Program.GetCamera();` line and replace all uses of `camera` with `_camera`.

5. Add a null guard: if `_camera` is null (e.g., if `GetCamera()` can return null during early initialization), show the same "Camera not available" message that is currently shown when `!_cameraService.IsAvailable`.

6. Consider whether `_camera` can become stale (e.g., on scene reload). If so, add a `RefreshCamera()` method that calls `Program.GetCamera()` again, and hook it to any relevant game event (e.g., scene loaded, mod reloaded).

**Acceptance criteria:**  
`Program.GetCamera()` should appear exactly once in `CameraDebugPanel.cs` (in the constructor or a `RefreshCamera()` helper). The per-frame `RenderCameraInfo()` call should not contain any `Program.*` calls.

---

### Step 6 — Cache mode/target strings in `CameraBasicsPanel`

**Context:**  
File: `caTTY.SkunkworksGameMod/UI/CameraBasicsPanel.cs` — `RenderCameraModeSection()`  

**Task:**

1. Add three cached-string fields:
   ```csharp
   private string _cachedCurrentModeLabel = "";
   private string _cachedNativeModeLabel = "";
   private string _cachedFollowTargetLabel = "";
   ```

2. Add a method `RefreshCachedLabels()` that calls `GetCurrentMode()`, `GetNativeControlModeDebug()`, and reads `FollowTarget`, then updates the three cached labels.

3. Determine the best way to call `RefreshCachedLabels()`:
   - **Option A:** Call it when the camera mode changes. If `ICameraService` exposes a mode-change event (or can be extended to do so), subscribe to it in the `CameraBasicsPanel` constructor.
   - **Option B (simpler):** Call `RefreshCachedLabels()` only when `ImGui.IsWindowAppearing()` returns true (i.e., when the debug window is opened/re-opened), and also every N frames (e.g., every 30 frames) as a refresh rate limiter.

4. In `RenderCameraModeSection()`, use the cached strings directly for the `ImGui.Text` calls. The call to `_cameraService.FollowTarget` for the purpose of `hasTarget` (the bool) can remain as a per-frame access since it is a simple property read and no string is allocated from it.

5. The `GetNativeControlModeDebug()` result is the most important to cache since its name suggests it may build a string dynamically. If it turns out to be a simple enum-to-string (O(1) interned), caching it is still good practice but lower urgency.

**Acceptance criteria:**  
`GetCurrentMode()` and `GetNativeControlModeDebug()` should not appear in the `RenderCameraModeSection()` body. The label strings should only be rebuilt on mode change or on window appearing.

---

### Step 7 — Cache animation status strings in `CameraDebugPanel`

**Context:**  
File: `caTTY.SkunkworksGameMod/UI/CameraDebugPanel.cs` — `RenderAnimationStatus()`  

**Task:**

1. Add a cached keyframe count label field and update it only when the count changes:
   ```csharp
   private string _cachedKeyframeCountLabel = "Keyframes loaded: 0";
   private int _lastKeyframeCount = 0;
   ```

2. In `RenderAnimationStatus()`, before using the label:
   ```csharp
   int count = _animationPlayer.Keyframes.Count;
   if (count != _lastKeyframeCount)
   {
       _cachedKeyframeCountLabel = $"Keyframes loaded: {count}";
       _lastKeyframeCount = count;
   }
   ImGui.Text(_cachedKeyframeCountLabel);
   ```

3. The time-display string (`$"{currentTime:F2}s / {duration:F2}s"`) changes every frame during animation playback and cannot be meaningfully cached. Leave it as-is or use a `Span<char>` formatting approach if allocation profiling shows it is a concern. For now, accept this as a known allocation during active animation only.

**Acceptance criteria:**  
The keyframe count label should allocate a new string only when the count changes (triggered by "Preview Keyframes" button click or clear), not on every frame.

---

### Step 8 — Eliminate redundant `GetTargetPosition()` call in `SkunkworksMod.OnAfterUi()`

**Context:**  
File: `caTTY.SkunkworksGameMod/SkunkworksMod.cs` — `OnAfterUi()`  

Note: If Step 4 is completed and the 5-frame diagnostic block is removed, this step is automatically resolved. Only perform this step if the diagnostic block is kept.

**Task:**

1. Identify both call sites of `_cameraService.GetTargetPosition()` within `OnAfterUi()`.
2. Add a single `float3` (or `double3`, match the return type) local variable `targetPos` computed once at the appropriate point in the function.
3. Pass `targetPos` to both blocks that need it rather than calling `GetTargetPosition()` a second time.

**Acceptance criteria:**  
`GetTargetPosition()` should appear exactly once in `OnAfterUi()`.

---

*End of ImGui Performance Analysis.*
