using System.Collections.Generic;
using caTTY.SkunkworksGameMod.Camera.Animation;

namespace caTTY.SkunkworksGameMod.Camera.Actions;

/// <summary>
/// Base interface for camera actions that generate animation keyframes.
/// Provides a blueprint for extensible camera animations (orbit, zoom, pan, etc.).
/// </summary>
public interface ICameraAction
{
    /// <summary>
    /// Name of this camera action (e.g., "orbit", "zoom").
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Generates keyframes for this camera action.
    /// </summary>
    /// <param name="context">Execution context containing camera state and parameters.</param>
    /// <returns>Sequence of keyframes to animate.</returns>
    IEnumerable<CameraKeyframe> GenerateKeyframes(CameraActionContext context);

    /// <summary>
    /// Validates the action parameters before execution.
    /// </summary>
    /// <param name="context">Execution context to validate.</param>
    /// <returns>Validation result indicating success or failure with error message.</returns>
    ValidationResult Validate(CameraActionContext context);
}
