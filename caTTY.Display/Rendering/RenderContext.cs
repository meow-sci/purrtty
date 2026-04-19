using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace caTTY.Display.Rendering;

/// <summary>
///     Context object containing shared resources for terminal rendering.
///     Passed to render strategies to provide access to draw targets and render state.
/// </summary>
public class RenderContext
{
    /// <summary>
    ///     The ImGui draw list for direct rendering.
    /// </summary>
    public required ImDrawListPtr DrawList { get; init; }

    /// <summary>
    ///     The current render key identifying the visual state.
    /// </summary>
    public required TerminalRenderKey RenderKey { get; init; }

    /// <summary>
    ///     Direct ImGui draw target for immediate rendering.
    /// </summary>
    public required ITerminalDrawTarget DirectTarget { get; init; }
}
