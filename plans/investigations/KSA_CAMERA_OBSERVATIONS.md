# Observations

These are factual real-world observations I've made by testing various KSA objects and functions
and testing them in the real game to see what happens.

## Functions

```csharp
// Gets the "OnFrameViewport" Camera
// There is also GetRenderCamera(), GetHoveredCamera(), GetMainCamera().  testing shows these all return the same Camera instance.
Program.GetCamera();

// moves camera to the target Astronomical
// NOTE: the previous/saved camera position for the given Astronomical is restored when switching.  Meaning each Astronomical appears to track it's own camera position information somehow (TBD where/how that works).
Universe.MoveCameraTo(hunter); // arg is a Astronomical. all crafts and celestials extend this.
```