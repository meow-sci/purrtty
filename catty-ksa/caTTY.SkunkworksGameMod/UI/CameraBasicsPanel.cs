using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.SkunkworksGameMod.Camera;

namespace caTTY.SkunkworksGameMod.UI;

/// <summary>
/// ImGui panel for testing basic camera control operations.
/// Provides UI for mode switching and manual camera movement.
/// </summary>
public class CameraBasicsPanel
{
    private readonly ICameraService _cameraService;
    
    // Movement parameters (UI state)
    // These fields will be used in Tasks 3.2 and 3.3
#pragma warning disable CS0414 // Field is assigned but its value is never used
    private float _moveDistance = 10.0f; // meters
    private float _rotationDegrees = 15.0f; // degrees

    // SetFollow flag experiment UI state
    private bool _setFollowUnknown0 = false;
    private bool _setFollowChangeControl = false;
    private bool _setFollowAlert = false;
#pragma warning restore CS0414
    
    /// <summary>
    /// Initializes a new instance of the <see cref="CameraBasicsPanel"/> class.
    /// </summary>
    /// <param name="cameraService">The camera service to use for operations.</param>
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
    
    /// <summary>
    /// Renders the camera mode section.
    /// </summary>
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

        // ImGui.SameLine();

        if (ImGui.Button("Exit Manual Follow (keep offset)"))
        {
            _cameraService.ExitManualFollow(ManualFollowExitMode.KeepCurrentOffset);
            Console.WriteLine("[CameraBasicsPanel] Exit manual follow (keep offset)");
        }

        // ImGui.SameLine();

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
    
    /// <summary>
    /// Renders the camera movement section.
    /// To be implemented in Task 3.3.
    /// </summary>
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
}
