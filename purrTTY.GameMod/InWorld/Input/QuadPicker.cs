using System;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.GameMod.InWorld.Display;

namespace purrTTY.GameMod.InWorld.Input;

/// <summary>
///     Phase 7A — per-frame ray-vs-quad pick that decides whether the in-world
///     terminal should hold keyboard focus.
///     <para>
///         <see cref="Cursor.InputRay"/> is in ego space (origin at the camera,
///         direction normalized; built by <c>Camera.ScreenToEgoRay</c> each frame
///         in <c>Program.OnFrame</c>). <see cref="QuadDisplay"/>'s anchored
///         <c>_modelEgo</c> is also in ego space, so the ray and the quad share
///         the same frame — no extra transform is required.
///     </para>
///     <para>
///         Called from <see cref="InWorldTerminalManager.OnAfterGui"/> after
///         <c>PerFrameRenderer.Frame</c> returns; that callee runs all of its
///         ImGui mutations under <c>OffscreenContext.With(...)</c> which restores
///         the main context, so the queries here (<c>IsMouseClicked</c>,
///         <c>IsKeyPressed</c>, <c>GetIO().WantCaptureMouse</c>) hit the main
///         context as intended.
///     </para>
/// </summary>
public sealed class QuadPicker
{
    private readonly QuadDisplay _quad;

    public QuadPicker(QuadDisplay quad)
    {
        _quad = quad ?? throw new ArgumentNullException(nameof(quad));
    }

    public void Tick(InWorldFocus focus)
    {
        if (focus is null) throw new ArgumentNullException(nameof(focus));

        bool clicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        if (clicked)
        {
            Ray r = Cursor.InputRay;
            bool hit = _quad.TryRaycast(r, out _);
            if (hit)
            {
                focus.State = InWorldFocusState.Focused;
            }
            else if (!ImGui.GetIO().WantCaptureMouse)
            {
                // Miss in empty space — release. If WantCaptureMouse is set the
                // click landed on another ImGui window; preserve current focus.
                focus.State = InWorldFocusState.NotFocused;
            }
        }

        if (focus.IsFocused && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            focus.State = InWorldFocusState.NotFocused;
        }
    }
}
