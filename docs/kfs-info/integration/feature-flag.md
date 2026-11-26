---
type: Subsystem Reference
title: The KfsPrototype feature flag
description: The single process-wide switch that gates kFS — how it is initialised and changed, every place that reads it, and exactly what turning it off and back on does to running vehicle computers.
resource: ksa-decomp:KSA.Kfs/KfsPrototype.cs
tags: [kfs, integration, feature-flag, lifecycle]
sources:
  - id: kfs-prototype
    resource: ksa-decomp:KSA.Kfs/KfsPrototype.cs
    title: KfsPrototype (decompiled)
  - id: kfs-console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-dispatch
    resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
    title: KfsDispatch (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-debug-window
    resource: ksa-decomp:KSA.Kfs/KfsDebugWindow.cs
    title: KfsDebugWindow (decompiled)
  - id: kfs-reference-window
    resource: ksa-decomp:KSA.Kfs/KfsReferenceWindow.cs
    title: KfsReferenceWindow (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:47:37Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The class

The complete source:[^kfs-prototype]

```csharp
namespace KSA.Kfs;

public static class KfsPrototype
{
    private static bool _enabled = Environment.GetEnvironmentVariable("KFS_PROTOTYPE") == "1";
    private static int _offCount;

    public static bool Enabled
    {
        get { return _enabled; }
        set
        {
            if (_enabled && !value)
                Interlocked.Increment(ref _offCount);
            _enabled = value;
        }
    }

    public static int OffCount => Volatile.Read(in _offCount);
}
```

- The initial value is `true` only if the environment variable `KFS_PROTOTYPE` is exactly the string `"1"` when the type is first used.
  **Default: off.**
- `Enabled` is a plain, non-volatile `bool`, readable and writable from anywhere (public setter).
- `OffCount` counts on→off transitions. It exists so that a dispatch thread can tell "the flag was off at some point since I last looked"
  even if it is on again by the time it looks.
- The flag is process-wide: not per save, not per vehicle, not stored anywhere.

# How it is turned on and off

| Route | Code | Notes |
| --- | --- | --- |
| Environment variable at process start | `KFS_PROTOTYPE=1` | Read once, in the static field initialiser. |
| Console: `kfs on` / `kfs off` | `KfsConsoleCommands.SetFlag(bool on)` (private), verbs declared with `NeedsFlag: false` so they work while the flag is off | The only route that also performs the off-time clean-up (below).[^kfs-console-commands] |
| Debug window button "kFS ON" | `KfsConsoleCommands.Execute("on"); KfsIntegration.ShowEditor();` | The window is opened from the debug menu item "Show kFS Debug" (`Program.cs:4423`); with the flag off it shows only this button.[^kfs-debug-window][^program] |
| Debug window button "kFS OFF" | `KfsConsoleCommands.Execute("off");` | |
| Programmatic | `KfsPrototype.Enabled = value;` | Bumps `OffCount` on on→off but does **not** run `SetFlag`'s clean-up. |

There is no entry for it in the game settings UI (no reference to kFS exists in `GameSettings`).

`SetFlag` in full:[^kfs-console-commands]

```csharp
private static void SetFlag(bool on)
{
    bool enabled = KfsPrototype.Enabled;
    KfsPrototype.Enabled = on;
    if (enabled && !on)
    {
        KfsVehicleHost.ReleaseDead();
        KfsVehicleHost.HandBackAtFlagOff();
        KfsIntegration.Print("kfs is off: every vehicle is back on the stock flight computer; after kfs on, kfs start boots a loaded image again");
    }
    else
    {
        KfsIntegration.Print("kfs is " + (on ? "on" : "off"));
    }
}
```

# Every reader of the flag

| Reader | File:line | Effect when off |
| --- | --- | --- |
| `KfsDispatch.TryComputeControl` | `KfsDispatch.cs:9-12` | Returns `false` before touching the vehicle: **no dispatch, no ticks, no queue drain**; the stock flight computer runs.[^kfs-dispatch] |
| `KfsIntegration.BeforeInputEvents` | `KfsIntegration.cs:160` | No frame counter increment, no `ReleaseDead`, **no `Services.MainThread()`** — main-thread work already queued stays queued.[^kfs-integration] |
| `KfsIntegration.FrameHook.IsShown` | `KfsIntegration.cs:17-27` | `Workbench.Frame` is not called: the bench machine does not advance, editor polling and pending console lines stop. |
| `KfsConsoleCommands.On()` | `KfsConsoleCommands.cs:234-242` | Every verb except `help`, `on`, `off` prints "kfs is off (kfs on turns the prototype on)" and does nothing. |
| `KfsConsoleCommands.SetFlag`, `Help` | `:246-247`, `:262` | Read the old value / report it. |
| `KfsDebugWindow.OnDrawUi` | `KfsDebugWindow.cs:95-102` | Draws the "kFS ON" button instead of the performance view.[^kfs-debug-window] |
| `KfsReferenceWindow.IsShown` | `KfsReferenceWindow.cs:103-113` | The reference window is hidden.[^kfs-reference-window] |
| `KfsVehicleHost._offCount` initialiser and `DispatchCore` | `KfsVehicleHost.cs:43`, `:491-502` | Read `OffCount`, not `Enabled` (below).[^kfs-vehicle-host] |

`KfsEditorWindow` has no flag check of its own; it is reached through verbs and buttons that do.

Not gated by the flag: `KfsIntegration.RegisterCommands` (always registers the `kfs` command and the two static windows),
`KfsVehicleHost.For` (a host can be created while off), `Vehicle.KfsHost`.

# What turning it off does

At the moment of `kfs off` (main thread, during the UI phase):[^kfs-console-commands][^kfs-vehicle-host][^kfs-services]

1. `KfsPrototype.Enabled = false` → `OffCount` increments.
2. `KfsVehicleHost.ReleaseDead()` — drop dead hosts.
3. `KfsVehicleHost.HandBackAtFlagOff()` — for every host whose `Computer.UnderKfsControl` is true:
   - `host.Services.HandBackPilot(host.Vehicle)`: `KfsWriteBack.PilotDefaults(vehicle.FlightComputer, _profileSet)` on the **live**
     flight computer (burn mode Manual, rate hold in `EclBody`, roll Decoupled, custom target zero, profile Balanced if a service had
     changed it), then `vehicle.SetEnum(VehicleEngine.MainShutdown)`;
   - `Volatile.Write(ref host._handedBackAtOff, 1)`.

From the next physics step on, `TryComputeControl` returns `false` for every vehicle: the stock computer flies everything. The
computers themselves are **not touched yet** — their state, loaded image, memory and handle tables are exactly as the last tick left them,
and they still report `UnderKfsControl`.

*Inferred:* because the hand-back in step 3 writes the live flight computer in the UI phase, the settings it writes can be overwritten by
the worker copy when the running frame's results are applied; the engine switch (`SetEnum(MainShutdown)`) is a vehicle field and is not
affected. See [dispatch and threading](/integration/dispatch-and-threading.md). Also note that the stock controller's internal state is not
reset at this point (`CSharpReset` runs only in a dispatch).

# What turning it back on does

Nothing immediately. At each host's **first dispatch after the flag is on again** (`KfsVehicleHost.DispatchCore`, `:491-502`):[^kfs-vehicle-host]

```csharp
int offCount = KfsPrototype.OffCount;
bool pilotDone = false;
if (offCount != _offCount)
{
    _offCount = offCount;
    pilotDone = Interlocked.Exchange(ref _handedBackAtOff, 0) != 0;
    if (Computer.UnderKfsControl)
    {
        Computer.Stop();
        Computer.Event("stopped: kfs was turned off; kfs start boots the image again");
    }
}
```

- `Computer.Stop()` puts the computer in `Standby`, clears the tick grid and calls `LeaveControl()` (clears the service override, raises
  the C#-reset flag if a full-control program had been flying and the handback flag if it had been in control).[^vehicle-computer]
- Later in the same dispatch: `KfsWriteBack.CSharpReset(fc)` if requested, and `Services.HandBack(fc, pilotDone)`. With `pilotDone == true`
  (the console route already applied pilot defaults) `HandBack` only ends the live burn handles; with `pilotDone == false` (the flag was
  cleared programmatically) it also applies `PilotDefaults` and queues the engine shutdown **now**.
- The image stays loaded. `kfs start` boots it again from reset.

So a flag cycle never resumes a program where it left off: it always ends in `Standby`.

# Edge cases read from the code

| Case | Behaviour |
| --- | --- |
| Flag off while a `stage` request is pending | The queued `StageWork` is not applied (no `MainThread()`); it is applied in the first `BeforeInputEvents` after the flag returns — after which the computer is stopped anyway. |
| Flag off, vehicle destroyed, flag on | The dead host is swept by the first `BeforeInputEvents`. |
| Flag set to `false` directly, never through `SetFlag` | No pilot defaults, no engine shutdown, no stock-controller reset at off time: the stock computer simply takes over with whatever settings the program left (pilot mode) or with stale internal state (full control). The clean-up happens only if and when the flag is turned on again. |
| Flag toggled off and on within one frame, between two dispatches | Detected through `OffCount`; the computer is stopped as above. |
| Worker threads reading `Enabled` | The field is not volatile; a dispatch already in progress completes, and a worker may observe the change slightly late. *Inferred* from the absence of any synchronisation. |

# Modding notes

- A mod that depends on kFS should treat `KfsPrototype.Enabled` as the master switch for its own per-frame and per-dispatch work, the way
  `BeforeInputEvents` does — a Harmony postfix on `BeforeInputEvents` still runs when the method returned early.
- To turn kFS on or off **use `KfsConsoleCommands.Execute("on")` / `Execute("off")`** (public static). Writing `KfsPrototype.Enabled`
  directly skips `ReleaseDead` and `HandBackAtFlagOff`.
- `OffCount` is the pattern to copy if a mod caches per-host state that must be invalidated across a flag cycle: remember the count,
  compare in the dispatch.
- With the flag off nothing drains: neither the per-computer `Enqueue` queue (dispatch side) nor the `MainWork` queue (main side). A mod
  that queues alongside kFS should decide whether its own queue is flushed or dropped at flag-off; `SetFlag` is private static, so the
  hook for "kFS was just turned off" is a postfix on `KfsVehicleHost.HandBackAtFlagOff()` (public static, parameterless).
- The environment variable is read in a static field initialiser of a `beforefieldinit` class; a mod loaded before the first access can
  still set the process environment variable, but setting `Enabled` is simpler and has the same effect.

[^kfs-prototype]: KfsPrototype.cs:6-29.
[^kfs-console-commands]: KfsConsoleCommands.cs — `on`/`off` verbs 21-28, `Execute` 192-232, `On` 234-242, `SetFlag` 244-258, `Help` 260-269.
[^kfs-integration]: KfsIntegration.cs:15-37, 146-176.
[^kfs-dispatch]: KfsDispatch.cs:7-12.
[^kfs-vehicle-host]: KfsVehicleHost.cs:43-45, 212-228, 488-521.
[^kfs-services]: KfsServices.cs:254-284.
[^kfs-debug-window]: KfsDebugWindow.cs:80, 89-135.
[^kfs-reference-window]: KfsReferenceWindow.cs:103-113.
[^vehicle-computer]: VehicleComputer.cs:408-428.
[^program]: Program.cs:4423.
