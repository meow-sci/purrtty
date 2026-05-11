namespace purrTTY.GameMod.InWorld.Input;

/// <summary>
///     Phase 7A — minimal focus-state holder. Single instance owned by
///     <see cref="InWorldTerminalManager"/>; read by the Harmony GLFW patches in
///     <see cref="purrTTY.GameMod.InWorld.Patches.FramePatches"/> and by the game
///     input-blocking prefix in <c>Patcher.Patch01.Prefix1</c>.
///     <para>
///         No event/observer surface: every reader polls the state directly. The
///         manager flips it via <see cref="QuadPicker"/> and the Esc handler each
///         frame.
///     </para>
/// </summary>
public enum InWorldFocusState
{
    NotFocused,
    Focused,
}

/// <inheritdoc cref="InWorldFocusState"/>
public sealed class InWorldFocus
{
    public InWorldFocusState State { get; set; } = InWorldFocusState.NotFocused;

    public bool IsFocused => State == InWorldFocusState.Focused;
}
