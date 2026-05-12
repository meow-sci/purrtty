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
    public float QuadWidthMeters  { get; set; } = 1.6f;
    public float QuadHeightMeters { get; set; } = 1.0f;
    public float QuadDistanceMeters { get; set; } = 2.0f;            // For quad path: distance in front of camera at spawn
    public ImGuiKey ToggleKey { get; set; } = ImGuiKey.F11;          // separate from main terminal toggle

    // Render-to-texture sub-rect (texture-space UV coords) and font scale.
    // Defaults render the whole terminal across the entire texture at 1x font.
    public float RenderWindowOffsetU { get; set; } = 0.0f;
    public float RenderWindowOffsetV { get; set; } = 0.0f;
    public float RenderWindowSizeU   { get; set; } = 1.0f;
    public float RenderWindowSizeV   { get; set; } = 1.0f;
    public float RenderFontScale     { get; set; } = 1.0f;

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
