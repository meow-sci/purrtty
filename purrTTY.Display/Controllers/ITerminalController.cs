namespace purrTTY.Display.Controllers;

/// <summary>
///     The host-facing lifecycle surface of the terminal controller: the game
///     mod toggles visibility and drives the per-frame tick/render; everything
///     else (windows, themes, sessions) is reached through the concrete
///     <c>GhosttyTerminalController</c>.
/// </summary>
public interface ITerminalController : IDisposable
{
    /// <summary>
    ///     Gets or sets whether the terminal window is visible.
    /// </summary>
    bool IsVisible { get; set; }

    /// <summary>
    ///     Updates the controller state. Should be called each frame (even while
    ///     hidden — it drains hidden sessions and advances the blink phase).
    /// </summary>
    /// <param name="deltaTime">Time elapsed since last update in seconds</param>
    void Update(float deltaTime);

    /// <summary>
    ///     Renders the terminal ImGui windows and handles input. Must be called
    ///     every frame, even while hidden — its early-out is what clears the
    ///     game-key gate (CLAUDE.md gotcha 21).
    /// </summary>
    void Render();
}
