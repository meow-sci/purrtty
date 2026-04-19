using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.SkunkworksGameMod.Camera;
using caTTY.SkunkworksGameMod.Camera.Animation;

namespace caTTY.SkunkworksGameMod.UI;

/// <summary>
/// ImGui panel for inspecting generated keyframes before or during animation playback.
/// </summary>
public class KeyframePreviewPanel
{
    private List<CameraKeyframe> _previewKeyframes = new();

    /// <summary>
    /// Sets keyframes to preview.
    /// </summary>
    public void SetPreviewKeyframes(IEnumerable<CameraKeyframe> keyframes)
    {
        _previewKeyframes.Clear();
        _previewKeyframes.AddRange(keyframes);
    }

    /// <summary>
    /// Clears the preview.
    /// </summary>
    public void ClearPreview()
    {
        _previewKeyframes.Clear();
    }

    /// <summary>
    /// Renders the keyframe preview panel.
    /// </summary>
    public void Render(ICameraService cameraService)
    {
        if (_previewKeyframes.Count == 0)
        {
            ImGui.TextDisabled("No keyframes to preview");
            return;
        }

        ImGui.Text($"Keyframes: {_previewKeyframes.Count}");
        ImGui.Separator();

        // Scrollable region for keyframes
        if (ImGui.BeginChild("KeyframeList", new float2(0, 300)))
        {
            for (int i = 0; i < _previewKeyframes.Count; i++)
            {
                var kf = _previewKeyframes[i];

                ImGui.PushID(i);

                // Format: [time] Offset(x, y, z) YPR(y, p, r) FOV(f)
                string label = kf.DebugLabel != null
                    ? $"[{kf.Timestamp:F2}s] {kf.DebugLabel}"
                    : $"[{kf.Timestamp:F2}s]";

                ImGui.Text(label);
                ImGui.SameLine();
                ImGui.TextDisabled($"Pos({kf.Offset.X:F1}, {kf.Offset.Y:F1}, {kf.Offset.Z:F1})");
                ImGui.SameLine();
                ImGui.TextDisabled($"YPR({kf.Yaw:F1}, {kf.Pitch:F1}, {kf.Roll:F1})");
                ImGui.SameLine();
                ImGui.TextDisabled($"FOV({kf.Fov:F1})");

                ImGui.SameLine();
                if (ImGui.SmallButton("Apply"))
                {
                    ApplyKeyframe(kf, cameraService);
                }

                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        if (ImGui.Button("Clear Preview"))
        {
            ClearPreview();
        }
    }

    private void ApplyKeyframe(CameraKeyframe keyframe, ICameraService cameraService)
    {
        if (!cameraService.IsAvailable)
        {
            Console.WriteLine("KeyframePreviewPanel: Camera not available");
            return;
        }

        try
        {
            // Apply position offset (relative to target)
            var targetPos = cameraService.GetTargetPosition();
            cameraService.Position = targetPos + keyframe.Offset;

            // Apply rotation
            cameraService.ApplyRotation(keyframe.Yaw, keyframe.Pitch, keyframe.Roll);

            // Apply FOV
            cameraService.FieldOfView = keyframe.Fov;

            Console.WriteLine($"KeyframePreviewPanel: Applied keyframe at {keyframe.Timestamp}s");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"KeyframePreviewPanel: Error applying keyframe: {ex.Message}");
        }
    }
}
