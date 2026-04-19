# KSA (Kitten Space Agency) Camera System Analysis

This document provides a comprehensive technical reference for manipulating the game camera in Kitten Space Agency (KSA) via the modding API. It is intended as a reference for mod developers building custom camera control systems.

---

## Table of Contents

1. [Required Dependencies](#required-dependencies)
2. [Game Assembly DLL References](#game-assembly-dll-references)
3. [Core Types from Game Assemblies](#core-types-from-game-assemblies)
4. [Obtaining the Camera Instance](#obtaining-the-camera-instance)
5. [Camera Class API Reference](#camera-class-api-reference)
6. [Coordinate System (ECL Frame)](#coordinate-system-ecl-frame)
7. [Position Control](#position-control)
8. [Rotation Control](#rotation-control)
9. [Field of View (FOV)](#field-of-view-fov)
10. [Look-At Functionality](#look-at-functionality)
11. [Following Objects](#following-objects)
12. [Camera Animation System](#camera-animation-system)
13. [Orbiting/Circling the Target](#orbitingcircling-the-target)
14. [Input Smoothing](#input-smoothing)
15. [Key Implementation Patterns](#key-implementation-patterns)
16. [Complete Code Examples](#complete-code-examples)

---

## Required Dependencies

### NuGet Packages

```xml
<PackageReference Include="Lib.Harmony" Version="2.4.2">
    <PrivateAssets>all</PrivateAssets>
</PackageReference>
<PackageReference Include="StarMap.API" Version="0.3.6">
    <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

### Using Statements

```csharp
using Brutal.ImGuiApi;      // ImGui UI controls (optional, for debug UI)
using Brutal.Numerics;      // double3, doubleQuat, floatQuat, float4x4
using KSA;                  // Camera, Astronomical, Universe, Vehicle
using StarMap.API;          // [StarMapMod], [StarMapAfterGui] attributes
using System.Reflection;    // For runtime camera lookup
```

---

## Game Assembly DLL References

Reference these DLLs from the KSA installation directory (`C:\Program Files\Kitten Space Agency\`):

| DLL | Purpose |
|-----|---------|
| `KSA.dll` | Main game assembly - Camera, Astronomical, Universe, Vehicle classes |
| `Brutal.Core.Numerics.dll` | Math types - double3, doubleQuat, floatQuat, float4x4 |
| `Brutal.Core.Common.dll` | Core utilities |
| `Brutal.ImGui.dll` | ImGui bindings (for debug UI) |
| `Brutal.ImGui.Extensions.dll` | Extended ImGui functionality |
| `Planet.Render.Core.dll` | Rendering system |
| `Brutal.Core.Strings.dll` | String utilities |

### Project File Reference Example

```xml
<PropertyGroup>
    <KSAFolder>C:\Program Files\Kitten Space Agency</KSAFolder>
</PropertyGroup>

<ItemGroup>
    <Reference Include="Brutal.Core.Numerics" Condition="Exists('$(KSAFolder)/Brutal.Core.Numerics.dll')">
        <HintPath>$(KSAFolder)/Brutal.Core.Numerics.dll</HintPath>
        <Private>false</Private>
    </Reference>
    <Reference Include="KSA" Condition="Exists('$(KSAFolder)/KSA.dll')">
        <HintPath>$(KSAFolder)/KSA.dll</HintPath>
        <Private>false</Private>
    </Reference>
    <!-- Add other references as needed -->
</ItemGroup>
```

---

## Core Types from Game Assemblies

### From `Brutal.Core.Numerics.dll`

| Type | Description | Key Members |
|------|-------------|-------------|
| `double3` | 3D vector with double precision | `X`, `Y`, `Z`, `Length()`, `Normalized()`, `Zero` (static) |
| `doubleQuat` | Quaternion with double precision | `X`, `Y`, `Z`, `W`, `CreateFromAxisAngle()` (static) |
| `floatQuat` | Quaternion with float precision | `X`, `Y`, `Z`, `W` |
| `float4x4` | 4x4 transformation matrix | `CreateFromQuaternion()` (static) |
| `float4` | 4D vector (used for colors) | `X`, `Y`, `Z`, `W` |

### From `KSA.dll`

| Type | Description |
|------|-------------|
| `Camera` | Main camera control class |
| `Astronomical` | Celestial bodies and vessels with orbital data |
| `Universe` | Static class for simulation time |
| `Vehicle` | Game vehicle/vessel class |
| `SimTime` | Simulation time type |

---

## Obtaining the Camera Instance

The camera instance is obtained via reflection from `KSA.Program`:

```csharp
private Camera? GetCamera()
{
    try
    {
        var ksaAssembly = typeof(KSA.Camera).Assembly;
        var programType = ksaAssembly.GetType("KSA.Program");

        if (programType != null)
        {
            // Try GetMainCamera first, fall back to GetCamera
            var getMainCameraMethod = programType.GetMethod("GetMainCamera",
                BindingFlags.Public | BindingFlags.Static);
            var getCameraMethod = programType.GetMethod("GetCamera",
                BindingFlags.Public | BindingFlags.Static);

            MethodInfo? methodToUse = getMainCameraMethod ?? getCameraMethod;

            if (methodToUse != null)
            {
                return methodToUse.Invoke(null, null) as Camera;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error finding camera: {ex.Message}");
    }
    return null;
}
```

### Best Practice: Cache the Camera

```csharp
private Camera? _camera;

private void UpdateCamera()
{
    // Only fetch if needed
    if (_camera == null || /* control is active */)
    {
        _camera = GetCamera();
    }
}
```

---

## Camera Class API Reference

### Properties

| Property | Type | Access | Description |
|----------|------|--------|-------------|
| `PositionEcl` | `double3` | get/set | Camera position in ECL (Ecliptic) coordinates |
| `LocalPosition` | `double3` | get/set | Camera position relative to current reference frame |
| `LocalRotation` | `doubleQuat` | get | Quaternion rotation in local space |
| `WorldRotation` | `doubleQuat` | get/set | Quaternion rotation in world space |
| `Following` | `dynamic` | get | Currently followed astronomical object (or null) |

### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `GetFieldOfView()` | `float` | Returns FOV in **radians** |
| `SetFieldOfView(float fov)` | `void` | Sets FOV (accepts **degrees**, converts internally) |
| `SetMatrix(float4x4 matrix)` | `void` | Sets rotation and position from 4x4 matrix |
| `GetForward()` | `double3` | Returns camera forward direction vector |
| `GetRight()` | `double3` | Returns camera right direction vector |
| `GetUp()` | `double3` | Returns camera up direction vector |
| `LookAt(double3 target, double3 up)` | `void` | Orients camera to look at target position |
| `Translate(double3 offset)` | `void` | Translates camera by offset |
| `SetFollow(Astronomical obj, bool?, bool changeControl, bool alert)` | `void` | Makes camera follow an object |
| `Unfollow()` | `void` | Stops following current object |

### Static Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Camera.LookAtRotation(double3 forward, double3 up)` | `doubleQuat` | Returns rotation quaternion from direction vectors |

---

## Coordinate System (ECL Frame)

KSA uses the **ECL (Ecliptic Inertial) coordinate frame**:

```
       Z (Up - Ecliptic Pole)
       |
       |
       |______ X (Right - Vernal Equinox)
      /
     /
    Y (Forward - Into screen)
```

| Axis | Direction | Usage |
|------|-----------|-------|
| X | Right (toward vernal equinox) | Strafe left/right |
| Y | Forward (toward ecliptic north) | Forward/backward movement, altitude in some contexts |
| Z | Up (ecliptic pole) | Vertical movement |

### Key Implications

- All camera positions (`PositionEcl`) are in ECL coordinates
- All target positions from `Astronomical.GetPositionEcl()` are in ECL coordinates
- The "up" vector for `LookAt()` is typically `new double3(0, 0, 1)` (ECL Z-axis)

---

## Position Control

### Direct Position Setting

```csharp
// Set absolute position
camera.PositionEcl = new double3(x, y, z);

// Apply offset to current position
camera.PositionEcl = camera.PositionEcl + offset;
```

### Using Translate

```csharp
// Move in camera-relative direction
var right = camera.GetRight();
camera.Translate(right * deltaTime * speed);
```

### Camera-Relative Movement

```csharp
// Get camera orientation vectors
double3 forward = camera.GetForward();
double3 right = camera.GetRight();
double3 up = camera.GetUp();

// Build movement vector
double3 movement = double3.Zero;
if (moveForward)  movement += forward * speed * dt;
if (moveBack)     movement -= forward * speed * dt;
if (strafeRight)  movement += right * speed * dt;
if (strafeLeft)   movement -= right * speed * dt;
if (moveUp)       movement += up * speed * dt;
if (moveDown)     movement -= up * speed * dt;

// Apply
camera.PositionEcl = camera.PositionEcl + movement;
```

---

## Rotation Control

### Yaw/Pitch/Roll in ECL Frame

The rotation axes in ECL space:

| Rotation | Axis | Direction |
|----------|------|-----------|
| **Yaw** | Z-axis (Up) | `new double3(0, 0, 1)` |
| **Pitch** | X-axis (Right) | `new double3(1, 0, 0)` |
| **Roll** | Y-axis (Forward) | `new double3(0, 1, 0)` |

### Building a Rotation Quaternion from Euler Angles

```csharp
// Convert degrees to radians
var yawRad = yaw * (Math.PI / 180.0);
var pitchRad = pitch * (Math.PI / 180.0);
var rollRad = roll * (Math.PI / 180.0);

// Create individual rotation quaternions
var yawQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), yawRad);   // Around Z
var pitchQuat = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), pitchRad); // Around X
var rollQuat = doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), rollRad);  // Around Y

// Combine (order matters: Yaw * Pitch * Roll for extrinsic ZXY)
var finalRotation = yawQuat * pitchQuat * rollQuat;
```

### Applying Rotation Using SetMatrix (Recommended)

The `SetMatrix()` approach preserves position precision:

```csharp
// Build rotation quaternion (see above)
var newRot = yawQuat * pitchQuat * rollQuat;

// Convert to float quaternion (required for matrix creation)
var fQuat = new floatQuat(
    (float)newRot.X,
    (float)newRot.Y,
    (float)newRot.Z,
    (float)newRot.W
);

// Create rotation matrix
var rotMatrix = float4x4.CreateFromQuaternion(fQuat);

// Save current position (SetMatrix affects position!)
var savedEclPos = camera.PositionEcl;
var savedLocalPos = camera.LocalPosition;

// Apply the matrix
camera.SetMatrix(rotMatrix);

// Restore position to prevent drift
camera.LocalPosition = savedLocalPos;
camera.PositionEcl = savedEclPos;

// Also force WorldRotation to ensure consistency
camera.WorldRotation = newRot;
```

### Converting Quaternion to Euler Angles (YPR)

For extrinsic ZXY order (Yaw-Z, Pitch-X, Roll-Y):

```csharp
private (float yaw, float pitch, float roll) QuaternionToYPR(doubleQuat q)
{
    var qw = q.W;
    var qx = q.X;
    var qy = q.Y;
    var qz = q.Z;

    // Rotation matrix elements
    double r00 = 1.0 - 2.0 * (qy * qy + qz * qz);
    double r01 = 2.0 * (qx * qy - qw * qz);
    double r11 = 1.0 - 2.0 * (qx * qx + qz * qz);
    double r20 = 2.0 * (qx * qz - qw * qy);
    double r21 = 2.0 * (qy * qz + qw * qx);
    double r22 = 1.0 - 2.0 * (qx * qx + qy * qy);

    // Extract angles (extrinsic ZXY)
    var pitch = Math.Asin(Math.Clamp(r21, -1.0, 1.0));
    var yaw = Math.Atan2(-r01, r11);
    var roll = Math.Atan2(-r20, r22);

    // Convert to degrees
    return (
        (float)(yaw * 180.0 / Math.PI),
        (float)(pitch * 180.0 / Math.PI),
        (float)(roll * 180.0 / Math.PI)
    );
}
```

---

## Field of View (FOV)

**Important:** FOV is stored internally in **radians** but `SetFieldOfView()` accepts **degrees**.

### Conversion Factor

```csharp
const float RadToDeg = 57.2958f;  // 180 / PI
const float DegToRad = 0.0174533f; // PI / 180
```

### Reading FOV

```csharp
float fovRadians = camera.GetFieldOfView();
float fovDegrees = fovRadians * 57.2958f;
```

### Setting FOV

```csharp
// SetFieldOfView accepts degrees
float desiredFovDegrees = 60.0f;
camera.SetFieldOfView(desiredFovDegrees);
```

### Valid FOV Range

Typically 15 to 120 degrees:

```csharp
float fov = Math.Clamp(fovDegrees, 15.0f, 120.0f);
camera.SetFieldOfView(fov);
```

---

## Look-At Functionality

### Basic Look-At

```csharp
double3 targetPosition = /* target ECL position */;
double3 upVector = new double3(0, 0, 1);  // ECL up

camera.LookAt(targetPosition, upVector);
```

### Look-At with Offset (Look Ahead)

```csharp
// Look at a point 100 meters in front of camera
var forward = camera.GetForward();
var lookTarget = camera.PositionEcl + forward * 100.0;
camera.LookAt(lookTarget, new double3(0, 0, 1));
```

### Tracking a Moving Object

```csharp
// Get current position of followed vessel
if (camera.Following != null)
{
    try
    {
        double3 vesselPos = camera.Following.GetPositionEcl();
        camera.LookAt(vesselPos, new double3(0, 0, 1));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error tracking vessel: {ex.Message}");
    }
}
```

### Getting a Look-At Rotation (Without Applying)

```csharp
double3 direction = (targetPos - cameraPos).Normalized();
double3 upEcl = new double3(0, 0, 1);

doubleQuat lookRotation = Camera.LookAtRotation(direction, upEcl);
```

---

## Following Objects

### Built-in Follow System

```csharp
// Start following an object
if (target is Astronomical astronomical)
{
    camera.SetFollow(astronomical, false, changeControl: false, alert: false);
}

// Stop following
camera.Unfollow();
```

### Manual Follow (Free Camera While Tracking)

This pattern maintains an offset from the target while allowing free rotation:

```csharp
private dynamic? _followedObject;
private double3 _followOffset;
private bool _isManualFollowing;

// Capture current follow state
void StartManualFollow()
{
    var currentFollowing = camera.Following;
    if (currentFollowing != null)
    {
        _followedObject = currentFollowing;
        _followOffset = camera.PositionEcl - currentFollowing.GetPositionEcl();
        _isManualFollowing = true;
        camera.Unfollow();  // Release built-in follow
    }
}

// Update each frame
void UpdateManualFollow()
{
    if (_isManualFollowing && _followedObject != null)
    {
        try
        {
            var targetPos = _followedObject.GetPositionEcl();
            camera.PositionEcl = targetPos + _followOffset;
        }
        catch
        {
            _isManualFollowing = false;
            _followedObject = null;
        }
    }
}

// Resume built-in follow
void StopManualFollow()
{
    if (_followedObject is Astronomical astronomical)
    {
        camera.SetFollow(astronomical, false, changeControl: false, alert: false);
    }
    _isManualFollowing = false;
}
```

---

## Camera Animation System

### Keyframe Structure

```csharp
public class AnimationKeyframe
{
    public float Timestamp { get; set; }      // Time in seconds
    public double OffsetX { get; set; }       // Position X offset
    public double OffsetY { get; set; }       // Position Y offset
    public double OffsetZ { get; set; }       // Position Z offset
    public float Yaw { get; set; }            // Yaw in degrees
    public float Pitch { get; set; }          // Pitch in degrees
    public float Roll { get; set; }           // Roll in degrees
    public float Fov { get; set; }            // FOV in degrees

    // Convenience property for double3 access
    [JsonIgnore]
    public double3 Offset
    {
        get => new double3(OffsetX, OffsetY, OffsetZ);
        set { OffsetX = value.X; OffsetY = value.Y; OffsetZ = value.Z; }
    }
}
```

### Catmull-Rom Spline Interpolation

Provides smooth transitions between keyframes:

```csharp
// For double3 positions
private double3 CatmullRom(double3 p0, double3 p1, double3 p2, double3 p3, float t)
{
    double t2 = t * t;
    double t3 = t2 * t;

    return 0.5 * (
        (2.0 * p1) +
        (-p0 + p2) * t +
        (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2 +
        (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3
    );
}

// For float values (yaw, pitch, roll, fov)
private float CatmullRomFloat(float p0, float p1, float p2, float p3, float t)
{
    float t2 = t * t;
    float t3 = t2 * t;

    return 0.5f * (
        (2.0f * p1) +
        (-p0 + p2) * t +
        (2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
        (-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
    );
}
```

### Animation Playback Loop

```csharp
public (double3 Offset, float Yaw, float Pitch, float Roll, float Fov)? Update(double dt)
{
    if (!IsPlaying || _keyframes.Count < 2) return null;

    CurrentTime += (float)dt;

    // Check if animation ended
    if (CurrentTime >= _keyframes.Last().Timestamp)
    {
        IsPlaying = false;
        var last = _keyframes.Last();
        return (last.Offset, last.Yaw, last.Pitch, last.Roll, last.Fov);
    }

    // Find current segment
    int segmentIndex = -1;
    for (int i = 0; i < _keyframes.Count - 1; i++)
    {
        if (CurrentTime >= _keyframes[i].Timestamp &&
            CurrentTime < _keyframes[i+1].Timestamp)
        {
            segmentIndex = i;
            break;
        }
    }

    if (segmentIndex >= 0)
    {
        // Get 4 keyframes for Catmull-Rom (p0, p1, p2, p3)
        var p0 = _keyframes[Math.Max(0, segmentIndex - 1)];
        var p1 = _keyframes[segmentIndex];
        var p2 = _keyframes[segmentIndex + 1];
        var p3 = _keyframes[Math.Min(_keyframes.Count - 1, segmentIndex + 2)];

        float duration = p2.Timestamp - p1.Timestamp;
        float elapsed = CurrentTime - p1.Timestamp;
        float t = elapsed / duration;

        // Interpolate all values
        double3 pos = CatmullRom(p0.Offset, p1.Offset, p2.Offset, p3.Offset, t);
        float yaw = CatmullRomFloat(p0.Yaw, p1.Yaw, p2.Yaw, p3.Yaw, t);
        float pitch = CatmullRomFloat(p0.Pitch, p1.Pitch, p2.Pitch, p3.Pitch, t);
        float roll = CatmullRomFloat(p0.Roll, p1.Roll, p2.Roll, p3.Roll, t);
        float fov = CatmullRomFloat(p0.Fov, p1.Fov, p2.Fov, p3.Fov, t);

        return (pos, yaw, pitch, roll, fov);
    }

    return null;
}
```

---

## Orbiting/Circling the Target

Create a circular camera path around a target:

```csharp
private float _circleAngle = 0.0f;
private double _circleRadius = 1000.0;
private float _rotationSpeed = 1.0f;

void UpdateCircleOrbit(double dt)
{
    var following = camera.Following;
    if (following == null) return;

    var targetPos = following.GetPositionEcl();

    // Advance angle
    _circleAngle += (float)(dt * _rotationSpeed);

    // Calculate position on horizontal circle (ECL XZ plane)
    var circleX = Math.Cos(_circleAngle) * _circleRadius;
    var circleZ = Math.Sin(_circleAngle) * _circleRadius;

    // Preserve current altitude relative to target
    var currentPos = camera.PositionEcl;
    var currentAltitude = currentPos.Y - targetPos.Y;

    // Set new position
    camera.PositionEcl = new double3(
        targetPos.X + circleX,
        targetPos.Y + currentAltitude,
        targetPos.Z + circleZ
    );

    // Look at target
    camera.LookAt(targetPos, new double3(0, 0, 1));
}
```

---

## Input Smoothing

Exponential moving average filter for smooth input:

```csharp
public class SmoothingFilter
{
    private double[] _currentValues;
    private bool _initialized = false;

    public SmoothingFilter(int size)
    {
        _currentValues = new double[size];
    }

    /// <summary>
    /// Apply smoothing to input values.
    /// </summary>
    /// <param name="target">Target values to smooth toward</param>
    /// <param name="smoothness">0.0 (no smoothing) to 0.99 (high smoothing)</param>
    public double[] Apply(double[] target, float smoothness)
    {
        if (!_initialized)
        {
            Array.Copy(target, _currentValues, target.Length);
            _initialized = true;
            return _currentValues;
        }

        float s = Math.Clamp(smoothness, 0.0f, 0.99f);
        float factor = 1.0f - s;

        for (int i = 0; i < _currentValues.Length; i++)
        {
            _currentValues[i] += (target[i] - _currentValues[i]) * factor;
        }

        return _currentValues;
    }

    public void Reset()
    {
        _initialized = false;
        Array.Clear(_currentValues, 0, _currentValues.Length);
    }
}
```

---

## Key Implementation Patterns

### 1. Position Preservation After SetMatrix

`SetMatrix()` affects both rotation AND position. Always save and restore:

```csharp
var savedPos = camera.PositionEcl;
var savedLocalPos = camera.LocalPosition;

camera.SetMatrix(rotMatrix);

camera.LocalPosition = savedLocalPos;
camera.PositionEcl = savedPos;
camera.WorldRotation = newRot;  // Force consistency
```

### 2. Flag-Driven Update Pipeline

Process controls in priority order:

```csharp
void UpdateCamera(double dt)
{
    // 1. Animation (highest priority - overrides everything)
    if (_animationManager.IsPlaying)
    {
        var animData = _animationManager.Update(dt);
        if (animData.HasValue) ApplyAnimationData(animData.Value);
        return;  // Skip other controls
    }

    // 2. External input (e.g., UDP)
    if (_enableExternalInput)
    {
        ApplyExternalInput();
    }

    // 3. Manual position offset
    if (_applyPositionOffset && _positionOffset.Length() > 0.001)
    {
        camera.PositionEcl = camera.PositionEcl + _positionOffset;
    }

    // 4. Manual rotation (YPR)
    if (_applyYPR)
    {
        ApplyYawPitchRoll();
    }

    // 5. Look-at (mutually exclusive with YPR)
    if (_isLookingAt)
    {
        camera.LookAt(_lookAtTarget, new double3(0, 0, 1));
    }

    // 6. Orbiting
    if (_isRotating)
    {
        UpdateCircleOrbit(dt);
    }
}
```

### 3. Lazy Camera Acquisition

Only fetch camera when needed:

```csharp
if (_isRotating || _isTranslating || _applyYPR || /* other controls */ || _camera == null)
{
    _camera = GetCamera();
}
```

### 4. Quaternion Composition Order

Left-to-right multiplication for extrinsic rotations:

```csharp
var combined = yawQuat * pitchQuat * rollQuat;  // ZXY order
```

### 5. Mutual Exclusivity of Controls

Some controls conflict and should disable each other:

```csharp
if (_applyYPR)
{
    _isLookingAt = false;  // YPR conflicts with look-at
}

if (_isLookingAt)
{
    _applyYPR = false;
    _isRotating = false;
}
```

---

## Complete Code Examples

### Minimal Camera Controller

```csharp
using Brutal.Numerics;
using KSA;
using StarMap.API;
using System.Reflection;

[StarMapMod]
public class MinimalCameraController
{
    private Camera? _camera;
    private double3 _positionOffset = double3.Zero;
    private float _yaw, _pitch, _roll;

    [StarMapAfterGui]
    public void OnAfterGui(double dt)
    {
        _camera = GetCamera();
        if (_camera == null) return;

        // Apply position offset
        if (_positionOffset.Length() > 0.001)
        {
            _camera.PositionEcl = _camera.PositionEcl + _positionOffset;
        }

        // Apply rotation
        ApplyRotation();
    }

    private Camera? GetCamera()
    {
        var ksaAssembly = typeof(Camera).Assembly;
        var programType = ksaAssembly.GetType("KSA.Program");
        var method = programType?.GetMethod("GetMainCamera",
            BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, null) as Camera;
    }

    private void ApplyRotation()
    {
        var yawRad = _yaw * (Math.PI / 180.0);
        var pitchRad = _pitch * (Math.PI / 180.0);
        var rollRad = _roll * (Math.PI / 180.0);

        var yawQ = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), yawRad);
        var pitchQ = doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), pitchRad);
        var rollQ = doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), rollRad);
        var rotation = yawQ * pitchQ * rollQ;

        var fQuat = new floatQuat((float)rotation.X, (float)rotation.Y,
                                   (float)rotation.Z, (float)rotation.W);
        var matrix = float4x4.CreateFromQuaternion(fQuat);

        var savedPos = _camera.PositionEcl;
        var savedLocal = _camera.LocalPosition;

        _camera.SetMatrix(matrix);

        _camera.LocalPosition = savedLocal;
        _camera.PositionEcl = savedPos;
        _camera.WorldRotation = rotation;
    }
}
```

### Flyby Animation Generator

```csharp
private void GenerateFlybyAnimation(Camera camera, Astronomical target)
{
    if (!target.HasOrbit()) return;

    var currentTime = Universe.GetElapsedSimTime();
    double flybyDuration = 10.0;
    int keyframeCount = 10;
    double cameraDistance = 200.0;

    double3 objectPosAtTime0 = target.GetPositionEcl(currentTime);
    double3 initialOffset = camera.PositionEcl - objectPosAtTime0;

    for (int i = 0; i < keyframeCount; i++)
    {
        float timeOffset = (float)(i * flybyDuration / (keyframeCount - 1));
        SimTime futureTime = currentTime + timeOffset;

        double3 objectPos = target.GetPositionEcl(futureTime);
        double3 velocity = target.GetVelocityEcl(futureTime);
        double3 velocityDir = velocity.Normalized();

        // Create camera position with cinematic variation
        double progress = (double)i / (keyframeCount - 1);
        double3 upEcl = new double3(0, 0, 1);
        double3 right = double3.Cross(velocityDir, upEcl).Normalized();
        double3 up = double3.Cross(right, velocityDir).Normalized();

        double aheadOffset = cameraDistance * (0.3 + 0.4 * Math.Sin(progress * Math.PI));
        double sideOffset = cameraDistance * Math.Sin(progress * Math.PI * 2.0);
        double verticalOffset = cameraDistance * 0.2 * (1.0 - Math.Abs(progress - 0.5) * 2.0);

        double3 relativeCameraOffset =
            velocityDir * aheadOffset +
            right * sideOffset +
            up * verticalOffset;

        double3 positionOffset = relativeCameraOffset - initialOffset;
        double3 cameraPosEcl = objectPos + relativeCameraOffset;

        // Calculate look direction
        SimTime lookAheadTime = futureTime + 1.0;
        double3 lookAtPos = target.GetPositionEcl(lookAheadTime);
        double3 lookDirection = (lookAtPos - cameraPosEcl).Normalized();

        doubleQuat lookAtQuat = Camera.LookAtRotation(lookDirection, upEcl);
        var (yaw, pitch, roll) = QuaternionToYPR(lookAtQuat);

        // Vary FOV for cinematic effect
        float baseFov = camera.GetFieldOfView() * 57.2958f;
        float fov = baseFov + (float)(15.0 * Math.Sin(progress * Math.PI));
        fov = Math.Clamp(fov, 15.0f, 120.0f);

        _animationManager.AddKeyframe(timeOffset, positionOffset, yaw, pitch, roll, fov);
    }
}
```

---

## Summary

### Essential Imports

```csharp
using Brutal.Numerics;      // double3, doubleQuat, floatQuat, float4x4
using KSA;                  // Camera, Astronomical, Universe
using System.Reflection;    // Camera acquisition
```

### Key Camera Properties

- `PositionEcl` - ECL position (double3)
- `WorldRotation` - World rotation (doubleQuat)
- `Following` - Current follow target

### Key Camera Methods

- `GetFieldOfView()` / `SetFieldOfView()` - FOV control
- `LookAt()` - Orient toward target
- `SetMatrix()` - Apply rotation matrix
- `SetFollow()` / `Unfollow()` - Object following
- `GetForward()` / `GetRight()` / `GetUp()` - Direction vectors

### Key Patterns

1. Use reflection to get camera from `KSA.Program`
2. Save/restore position when using `SetMatrix()`
3. Convert angles: degrees ↔ radians (multiply/divide by 57.2958)
4. FOV: `GetFieldOfView()` returns radians, `SetFieldOfView()` accepts degrees
5. Use Catmull-Rom splines for smooth animation interpolation
6. ECL frame: X=right, Y=forward, Z=up

---

*Document generated from analysis of the StarMap SimpleMod camera control implementation.*
