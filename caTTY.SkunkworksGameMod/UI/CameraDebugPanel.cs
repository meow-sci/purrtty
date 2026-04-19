using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.SkunkworksGameMod.Camera;
using caTTY.SkunkworksGameMod.Camera.Animation;
using caTTY.SkunkworksGameMod.Rpc.Actions;
using KSA;

namespace caTTY.SkunkworksGameMod.UI;

/// <summary>
/// ImGui debug panel for camera control and animation testing.
/// </summary>
public class CameraDebugPanel
{
    private readonly ICameraService _cameraService;
    private readonly ICameraAnimationPlayer _animationPlayer;
    private readonly CameraOrbitRpcAction _orbitAction;
    private readonly KeyframePreviewPanel _previewPanel;
    private readonly AlexsTestPanel _alexsTestPanel;
    private readonly CameraBasicsPanel _cameraBasicsPanel;

    // Orbit action parameters (UI state)
    private float _duration = 5.0f;
    private float _distance = 100.0f;

    // Start lerp
    private bool _useStartLerp = false;
    private float _startLerpTime = 1.0f;
    private int _startLerpEasingIndex = 3; // EaseInOut

    // End lerp
    private bool _useEndLerp = false;
    private float _endLerpTime = 1.0f;
    private int _endLerpEasingIndex = 3; // EaseInOut

    // Main animation
    private bool _counterClockwise = false;
    private int _easingIndex = 3; // EaseInOut

    public CameraDebugPanel(
        ICameraService cameraService,
        ICameraAnimationPlayer animationPlayer)
    {
        _cameraService = cameraService;
        _animationPlayer = animationPlayer;
        _orbitAction = new CameraOrbitRpcAction(cameraService, animationPlayer);
        _previewPanel = new KeyframePreviewPanel();
        _alexsTestPanel = new AlexsTestPanel();
        _cameraBasicsPanel = new CameraBasicsPanel(cameraService);
    }

    /// <summary>
    /// Renders the camera debug panel.
    /// </summary>
    public void Render()
    {

        ImGui.SeparatorText("Camera Info");
        RenderCameraInfo();

        // Alex's test panel — open by default and shown first
        if (ImGui.CollapsingHeader("Alexs Test Panel", ImGuiTreeNodeFlags.DefaultOpen))
        {
            _alexsTestPanel.Render();
        }


        ImGui.Spacing();

        // Camera Basics section (closed by default)
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

    private void RenderCameraInfo()
    {
        if (!_cameraService.IsAvailable)
        {
            ImGui.TextColored(new float4(1, 0, 0, 1), "Camera not available");
            return;
        }

        var camera = Program.GetCamera();

        var pos = camera.PositionEcl;

        ImGui.Text($"Position (default): ({pos.X:F1}, {pos.Y:F1}, {pos.Z:F1})");

        var fov = camera.GetFieldOfView()  * 57.2958f; // radians to deg

        ImGui.Text($"FOV: {fov:F1}°");

        var target = _cameraService.FollowTarget;
        if (target != null)
        {
            ImGui.TextColored(new float4(0, 1, 0, 1), "Following target");
            if (_cameraService.IsManualFollowing)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(manual)");
            }
        }
        else
        {
            ImGui.TextColored(new float4(1, 0.5f, 0, 1), "No follow target");
        }
    }

    private void RenderOrbitControls()
    {
        // Main animation parameters
        ImGui.Text("Main Animation:");
        ImGui.SliderFloat("Duration (s)", ref _duration, 0.1f, 30.0f);
        ImGui.SliderFloat("Distance (m)", ref _distance, 10.0f, 1000.0f);
        ImGui.Checkbox("Counter-clockwise", ref _counterClockwise);

        ImGui.Text("Main Easing:");
        ImGui.RadioButton("Linear##main", ref _easingIndex, 0);
        ImGui.SameLine();
        ImGui.RadioButton("Ease In##main", ref _easingIndex, 1);
        ImGui.SameLine();
        ImGui.RadioButton("Ease Out##main", ref _easingIndex, 2);
        ImGui.SameLine();
        ImGui.RadioButton("Ease In-Out##main", ref _easingIndex, 3);

        ImGui.Spacing();
        ImGui.Separator();

        // Start lerp controls
        ImGui.Text("Start Lerp (to animation start):");
        ImGui.Checkbox("Enable Start Lerp", ref _useStartLerp);

        if (_useStartLerp)
        {
            ImGui.SliderFloat("Start Lerp Time (s)", ref _startLerpTime, 0.1f, 10.0f);
            ImGui.Text("Start Lerp Easing:");
            ImGui.RadioButton("Linear##start", ref _startLerpEasingIndex, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In##start", ref _startLerpEasingIndex, 1);
            ImGui.SameLine();
            ImGui.RadioButton("Ease Out##start", ref _startLerpEasingIndex, 2);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In-Out##start", ref _startLerpEasingIndex, 3);
        }
        else
        {
            ImGui.BeginDisabled();
            float disabledStartTime = _startLerpTime;
            ImGui.SliderFloat("Start Lerp Time (s)", ref disabledStartTime, 0.1f, 10.0f);
            ImGui.Text("Start Lerp Easing:");
            int disabledStartEasing = _startLerpEasingIndex;
            ImGui.RadioButton("Linear##start", ref disabledStartEasing, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In##start", ref disabledStartEasing, 1);
            ImGui.SameLine();
            ImGui.RadioButton("Ease Out##start", ref disabledStartEasing, 2);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In-Out##start", ref disabledStartEasing, 3);
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.Separator();

        // End lerp controls
        ImGui.Text("End Lerp (back to original position):");
        ImGui.Checkbox("Enable End Lerp", ref _useEndLerp);

        if (_useEndLerp)
        {
            ImGui.SliderFloat("End Lerp Time (s)", ref _endLerpTime, 0.1f, 10.0f);
            ImGui.Text("End Lerp Easing:");
            ImGui.RadioButton("Linear##end", ref _endLerpEasingIndex, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In##end", ref _endLerpEasingIndex, 1);
            ImGui.SameLine();
            ImGui.RadioButton("Ease Out##end", ref _endLerpEasingIndex, 2);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In-Out##end", ref _endLerpEasingIndex, 3);
        }
        else
        {
            ImGui.BeginDisabled();
            float disabledEndTime = _endLerpTime;
            ImGui.SliderFloat("End Lerp Time (s)", ref disabledEndTime, 0.1f, 10.0f);
            ImGui.Text("End Lerp Easing:");
            int disabledEndEasing = _endLerpEasingIndex;
            ImGui.RadioButton("Linear##end", ref disabledEndEasing, 0);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In##end", ref disabledEndEasing, 1);
            ImGui.SameLine();
            ImGui.RadioButton("Ease Out##end", ref disabledEndEasing, 2);
            ImGui.SameLine();
            ImGui.RadioButton("Ease In-Out##end", ref disabledEndEasing, 3);
            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Action buttons
        if (ImGui.Button("Preview Keyframes"))
        {
            PreviewOrbitKeyframes();
        }

        ImGui.SameLine();
        if (ImGui.Button("Execute Orbit"))
        {
            ExecuteOrbit();
        }

        ImGui.SameLine();
        if (ImGui.Button("Stop"))
        {
            StopAnimation();
        }
    }

    private void RenderAnimationStatus()
    {
        if (_animationPlayer.IsPlaying)
        {
            ImGui.TextColored(new float4(0, 1, 0, 1), "PLAYING");
            ImGui.SameLine();
            ImGui.Text($"{_animationPlayer.CurrentTime:F2}s / {_animationPlayer.Duration:F2}s");

            // Progress bar
            float progress = _animationPlayer.Duration > 0
                ? _animationPlayer.CurrentTime / _animationPlayer.Duration
                : 0f;
            ImGui.ProgressBar(progress, new float2(0, 0));
        }
        else
        {
            ImGui.TextDisabled("Not playing");
        }

        ImGui.Text($"Keyframes loaded: {_animationPlayer.Keyframes.Count}");
    }

    private void PreviewOrbitKeyframes()
    {
        try
        {
            var context = BuildOrbitContext();
            if (context == null)
            {
                Console.WriteLine("CameraDebugPanel: Cannot preview - no follow target");
                return;
            }

            var orbitAction = new Camera.Actions.OrbitCameraAction();
            var validation = orbitAction.Validate(context);
            if (!validation.IsValid)
            {
                Console.WriteLine($"CameraDebugPanel: Validation failed - {validation.ErrorMessage}");
                return;
            }

            // Generate action keyframes
            var actionKeyframes = orbitAction.GenerateKeyframes(context);

            // Build complete animation with start/end lerps
            var completeKeyframes = Camera.Actions.CameraAnimationBuilder.BuildAnimation(actionKeyframes, context);

            _previewPanel.SetPreviewKeyframes(completeKeyframes);
            Console.WriteLine($"CameraDebugPanel: Generated {System.Linq.Enumerable.Count(completeKeyframes)} preview keyframes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CameraDebugPanel: Error previewing keyframes: {ex.Message}");
        }
    }

    private void ExecuteOrbit()
    {
        try
        {
            // Build JSON params
            var paramsJson = System.Text.Json.JsonSerializer.SerializeToElement(new
            {
                time = _duration,
                distance = _distance,
                useStartLerp = _useStartLerp,
                startLerpTime = _useStartLerp ? _startLerpTime : (float?)null,
                startLerpEasing = GetEasingString(_startLerpEasingIndex),
                useEndLerp = _useEndLerp,
                endLerpTime = _useEndLerp ? _endLerpTime : (float?)null,
                endLerpEasing = GetEasingString(_endLerpEasingIndex),
                counterClockwise = _counterClockwise,
                easing = GetEasingString(_easingIndex)
            });

            var response = _orbitAction.Execute(paramsJson);

            if (response.Success)
            {
                Console.WriteLine("CameraDebugPanel: Orbit started successfully");
            }
            else
            {
                Console.WriteLine($"CameraDebugPanel: Orbit failed - {response.Error}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CameraDebugPanel: Error executing orbit: {ex.Message}");
        }
    }

    private void StopAnimation()
    {
        _animationPlayer.Stop();
        _cameraService.StopManualFollow();
        Console.WriteLine("CameraDebugPanel: Animation stopped");
    }

    private Camera.Actions.CameraActionContext? BuildOrbitContext()
    {
        if (!_cameraService.IsAvailable || _cameraService.FollowTarget == null)
        {
            return null;
        }

        var targetPosition = _cameraService.GetTargetPosition();
        var currentOffset = _cameraService.Position - targetPosition;
        var currentRotation = _cameraService.Rotation;
        var currentFov = _cameraService.FieldOfView;

        // Capture original state
        var (currentYaw, currentPitch, currentRoll) = QuaternionToYPR(currentRotation);
        var originalState = new Camera.Actions.OriginalCameraState(
            currentOffset,
            currentYaw,
            currentPitch,
            currentRoll,
            currentFov
        );

        return new Camera.Actions.CameraActionContext
        {
            Camera = _cameraService,
            TargetPosition = targetPosition,
            CurrentOffset = currentOffset,
            CurrentFov = currentFov,
            CurrentRotation = currentRotation,
            OriginalState = originalState,
            Duration = _duration,
            Distance = _distance,
            UseStartLerp = _useStartLerp,
            StartLerpTime = _startLerpTime,
            StartLerpEasing = GetEasingType(_startLerpEasingIndex),
            UseEndLerp = _useEndLerp,
            EndLerpTime = _endLerpTime,
            EndLerpEasing = GetEasingType(_endLerpEasingIndex),
            Easing = GetEasingType(_easingIndex),
            CounterClockwise = _counterClockwise
        };
    }

    private EasingType GetEasingType(int index)
    {
        return index switch
        {
            0 => EasingType.Linear,
            1 => EasingType.EaseIn,
            2 => EasingType.EaseOut,
            3 => EasingType.EaseInOut,
            _ => EasingType.EaseInOut
        };
    }

    private string GetEasingString(int index)
    {
        return index switch
        {
            0 => "linear",
            1 => "easein",
            2 => "easeout",
            3 => "easeinout",
            _ => "easeinout"
        };
    }

    private static (float yaw, float pitch, float roll) QuaternionToYPR(Brutal.Numerics.doubleQuat q)
    {
        var qw = q.W;
        var qx = q.X;
        var qy = q.Y;
        var qz = q.Z;

        double r00 = 1.0 - 2.0 * (qy * qy + qz * qz);
        double r01 = 2.0 * (qx * qy - qw * qz);
        double r11 = 1.0 - 2.0 * (qx * qx + qz * qz);
        double r20 = 2.0 * (qx * qz - qw * qy);
        double r21 = 2.0 * (qy * qz + qw * qx);
        double r22 = 1.0 - 2.0 * (qx * qx + qy * qy);

        var pitch = Math.Asin(Math.Clamp(r21, -1.0, 1.0));
        var yaw = Math.Atan2(-r01, r11);
        var roll = Math.Atan2(-r20, r22);

        return (
            (float)(yaw * 180.0 / Math.PI),
            (float)(pitch * 180.0 / Math.PI),
            (float)(roll * 180.0 / Math.PI)
        );
    }
}
