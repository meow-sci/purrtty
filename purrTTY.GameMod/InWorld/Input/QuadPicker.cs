using Brutal.ImGuiApi;
using KSA;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Settings;

namespace purrTTY.GameMod.InWorld.Input;

/// <summary>
///     Click-to-focus for the in-world quad in <b>part</b> mode. On a left click it
///     ray-tests the quad in ego space (<see cref="Cursor.InputRay"/> × the quad's
///     part-mode model): a hit grabs input focus, a click in empty world space
///     releases it. Clicks captured by ImGui (a window/menu) are ignored so the
///     menus don't steal focus.
///     <para>
///         Escape is deliberately NOT a release key — it must reach the shell (vim,
///         less, ...). Billboard mode has no ego-space raycast, so it is focused
///         via the menu's "Focus In-World Terminal" toggle instead and this picker
///         is a no-op for it.
///     </para>
/// </summary>
public sealed class QuadPicker
{
    private readonly InWorldQuad _quad;
    private readonly InWorldSettings _settings;

    public QuadPicker(InWorldQuad quad, InWorldSettings settings)
    {
        _quad = quad;
        _settings = settings;
    }

    /// <summary>Updates in-world focus from this frame's mouse. Main thread.</summary>
    public void Tick()
    {
        // Billboard focus is menu-driven (no spatial click target).
        if (_settings.IsBillboard)
        {
            return;
        }

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            return;
        }

        if (_quad.TryRaycast(Cursor.InputRay, out _))
        {
            InWorldTerminalManager.IsInputFocused = true;
        }
        else if (!ImGui.GetIO().WantCaptureMouse)
        {
            // Clicked empty world space (not over an ImGui widget) → release.
            InWorldTerminalManager.IsInputFocused = false;
        }
    }
}
