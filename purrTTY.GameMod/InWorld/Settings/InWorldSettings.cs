using Brutal.ImGuiApi;

namespace purrTTY.GameMod.InWorld.Settings;

/// <summary>
///     Settings for the in-world (render-to-texture) terminal feature.
///     Phase 1 holds these in memory only; Phase 8 will wire persistence
///     through <c>ThemeConfiguration</c> (or a sibling <c>InWorldConfiguration</c>).
/// </summary>
public sealed class InWorldSettings
{
    public bool Enabled { get; set; } = false;                       // Master toggle

    /// <summary>
    ///     SubPart (or root Part) Id the quad is anchored against. Empty = no
    ///     anchor selected; toggle is rejected in that state. The quad floats in
    ///     this part's local frame, offset by <see cref="AnchorOffsetX"/>/Y/Z
    ///     and rotated by <see cref="AnchorRotationX"/>/Y/Z.
    /// </summary>
    public string TargetPartName { get; set; } = "";
    public int TextureWidth  { get; set; } = 1024;
    public int TextureHeight { get; set; } = 1024;
    public ImGuiKey ToggleKey { get; set; } = ImGuiKey.F11;          // separate from main terminal toggle

    // Quad size (meters). The quad is built in local space as a 1×1 unit square
    // and scaled per-frame by these values.
    public float QuadWidthMeters    { get; set; } = 1.6f;
    public float QuadHeightMeters   { get; set; } = 1.0f;

    // Subpart-local pose offset for the quad. X/Y/Z are in the chosen SubPart's
    // own frame: rotation is applied first (about the quad's own center), then
    // the rotated quad is translated by the offset, then the result is brought
    // into ego space via the SubPart's ego pose. Defaults of 0/0/0 put the quad
    // co-located with the SubPart origin, facing the SubPart's +Z axis.
    public float AnchorOffsetX { get; set; } = 0.0f;
    public float AnchorOffsetY { get; set; } = 0.0f;
    public float AnchorOffsetZ { get; set; } = 0.0f;

    // Subpart-local Euler rotation (degrees). Intrinsic XYZ — applied in the
    // order X, then Y, then Z about the quad's own axes before translation.
    public float AnchorRotationX { get; set; } = 0.0f;
    public float AnchorRotationY { get; set; } = 0.0f;
    public float AnchorRotationZ { get; set; } = 0.0f;

    // UV sub-rect: which portion of the off-screen texture is sampled onto the
    // mesh. Smaller size → more zoomed in (less of the texture stretched across
    // the same surface). Offsets pan within the texture. Texture coords use
    // ImGui convention: (0,0) = top-left, (1,1) = bottom-right.
    public float UvOffsetU { get; set; } = 0.0f;
    public float UvOffsetV { get; set; } = 0.0f;
    public float UvSizeU   { get; set; } = 1.0f;
    public float UvSizeV   { get; set; } = 1.0f;

    // TODO Phase 8: persist via ThemeConfiguration
    private static InWorldSettings? _cached;

    /// <summary>
    ///     Loads the settings, falling back to defaults if no persisted state exists.
    ///     Phase 1: returns an in-memory singleton default. Phase 8 will read from
    ///     <c>ThemeConfiguration</c> (or a sibling <c>InWorldConfiguration</c>) — the
    ///     API surface is stable so callers won't need to change.
    /// </summary>
    public static InWorldSettings LoadOrDefault()
    {
        // TODO Phase 8: persist via ThemeConfiguration
        return _cached ??= new InWorldSettings();
    }

    /// <summary>
    ///     Persists the current settings.
    ///     Phase 1: in-memory no-op (the live instance retains its values for the process lifetime).
    /// </summary>
    public void Save()
    {
        // TODO Phase 8: persist via ThemeConfiguration
        _cached = this;
    }
}
