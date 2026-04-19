# Overview

This is for a set of new camera related RPC actions.

These actions are to be implemented in the caTTY.SkunkworksGameMod project as an initial test area first.


## Instructions

- For each action, execute in a sub agent with a clean context.
- After each action is implemented, ensure the project compiles and tests pass
- After validating compilation and tests, create a descriptive git commit

## Implementation

- In caTTY.SkunkworksGameMod introduce a copy of ISocketRpcAction 
- All actions implemented must adhere to the same ISocketRpcAction contract so they can be moved to the real game mod later
- All actions should be invokable via the test ImGui panel.  A button to execute and other inputs for settings as needed

## Actions

### Orbit action

The orbit action should keep the camera focused on the target and move the camera around the target in an orbital kind of motion, so that the person watching this has the appearance of circling around the target.

- Inputs
    - time
        - unit: a float or other appropriate fractional unit to represent fractional seconds
    - distance
        - unit: a float or other appropriate dractional unit to represent meters
    - lerp - if true, when the animation starts, the current camera position should smoothly transition to the target distance needed for the animation we plan to play, if false, just snap instantly.
        - unit: boolean for true/false
    - lerpTime - if lerp is true this must be set.  this is the length of time used to smoothly lerp to the start of the animation
        - unit: a float or other appropriate fractional unit to represent fractional seconds
        