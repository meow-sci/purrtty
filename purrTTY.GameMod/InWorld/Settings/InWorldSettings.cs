using Brutal.ImGuiApi;
using purrTTY.GameMod.InWorld.Display;

namespace purrTTY.GameMod.InWorld.Settings;

/// <summary>
///     Settings for the in-world (render-to-texture) terminal feature.
///     Phase 1 holds these in memory only; Phase 8 will wire persistence
///     through <c>ThemeConfiguration</c> (or a sibling <c>InWorldConfiguration</c>).
/// </summary>
public sealed class InWorldSettings
{
    public bool Enabled { get; set; } = false;                       // Master toggle
    public string TargetPartName { get; set; } = "";                 // If set, try SubPart path; else quad path
    public OverrideMode TargetOverrideMode { get; set; } = OverrideMode.PerTemplate;
    public int TextureWidth  { get; set; } = 1024;
    public int TextureHeight { get; set; } = 1024;
    public ImGuiKey ToggleKey { get; set; } = ImGuiKey.F11;          // separate from main terminal toggle

    // Mesh size (quad path only — a SubPart's mesh is fixed by the part definition
    // and not modifiable from CPU side without shader changes). The quad is built
    // in local space as a 1×1 unit square and scaled per-frame by these values.
    public float QuadWidthMeters    { get; set; } = 1.6f;
    public float QuadHeightMeters   { get; set; } = 1.0f;
    public float QuadDistanceMeters { get; set; } = 2.0f;            // distance in front of camera at spawn

    // UV sub-rect: which portion of the off-screen texture is sampled onto the
    // mesh. Smaller size → more zoomed in (less of the texture stretched across
    // the same surface). Offsets pan within the texture. Texture coords use
    // ImGui convention: (0,0) = top-left, (1,1) = bottom-right.
    //
    // Quad path: applied to the quad's vertex UVs (rebuilt when these change).
    // SubPart path: NOT applied — the part mesh has fixed UVs we cannot
    // intercept without shader changes; the part samples the full texture.
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
