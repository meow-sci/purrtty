# KSA Camera API Reference

## Obtaining the Camera Instance

Retrieved via reflection from `KSA.Program`:
- `Program.GetMainCamera()` (preferred)
- `Program.GetCamera()` (fallback)

### Example Code

```csharp
var ksaAssembly = typeof(KSA.Camera).Assembly;
var programType = ksaAssembly.GetType("KSA.Program");

if (programType != null)
{
    var getMainCameraMethod = programType.GetMethod("GetMainCamera", BindingFlags.Public | BindingFlags.Static);
    var getCameraMethod = programType.GetMethod("GetCamera", BindingFlags.Public | BindingFlags.Static);
    MethodInfo? methodToUse = getMainCameraMethod ?? getCameraMethod;
    
    if (methodToUse != null)
    {
        var camera = methodToUse.Invoke(null, null) as Camera;
        // ...
    }
}
```

> **Note:** There is very likely a better way than reflection to access the camera.

## Position Properties

- `PositionEcl` (get/set) — Position in Ecliptic (ECL) coordinates (`double3`)
- `LocalPosition` (get/set) — Local position (`double3`)

## Rotation Properties

- `WorldRotation` (get/set) — World rotation quaternion (`doubleQuat`)
- `SetMatrix(float4x4)` — Sets rotation via a 4x4 matrix (also affects local position)

## Orientation Methods

Direction vectors (return `double3`):
- `GetForward()` — Forward direction vector
- `GetRight()` — Right direction vector
- `GetUp()` — Up direction vector

## Follow/Unfollow Methods

- `Following` (property) — Currently followed object (dynamic/Astronomical)
- `SetFollow(Astronomical, bool, bool changeControl, bool alert)` — Follows an astronomical object
- `Unfollow()` — Stops following

## Look At Methods

- `LookAt(double3 target, double3 up)` — Orients camera to look at a target position
- `Camera.LookAtRotation(double3 direction, double3 up)` (static) — Returns a quaternion for a look-at rotation

## Translation Method

- `Translate(double3 offset)` — Moves the camera by an offset vector

## Field of View Methods

- `GetFieldOfView()` — Returns FOV in radians (`float`)
- `SetFieldOfView(float degrees)` — Sets FOV (input in degrees)

## Key Usage Patterns

- **Position updates:** Direct assignment to `PositionEcl`
- **Rotation updates:** Build quaternions from YPR, convert to `float4x4`, use `SetMatrix()`, then restore position
- **Direction vectors:** Use `GetForward()`, `GetRight()`, `GetUp()` for camera-relative movement
- **Following:** Track `Following` property and use `SetFollow()`/`Unfollow()` to manage follow state

## Coordinate System

All coordinates use the ECL (Ecliptic) frame, with positions in meters (`double3`) and rotations as quaternions (`doubleQuat`). 