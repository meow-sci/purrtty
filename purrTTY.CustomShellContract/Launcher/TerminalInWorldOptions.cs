namespace purrTTY.Core.Terminal;

/// <summary>
///     Options for an in-world terminal (<see cref="TerminalPlacementKind.InWorldPart"/> /
///     <see cref="TerminalPlacementKind.InWorldBillboard"/>): a render-to-texture quad drawn in
///     the 3D scene, anchored to a vehicle part or pinned in the camera's view.
///     <para>
///         The offset/rotation/size members describe the quad in the anchor's frame. For a part
///         anchor they are metres/degrees in the part's local frame; for a billboard
///         <see cref="OffsetX"/>/<see cref="OffsetY"/> are screen offsets in metres at
///         <see cref="BillboardDistance"/> (and <see cref="OffsetZ"/> is unused).
///     </para>
/// </summary>
public sealed class TerminalInWorldOptions
{
    /// <summary>Registry name; null picks an automatic one. A name in use is made unique with a numeric suffix.</summary>
    public string? Name { get; set; }

    /// <summary>Name of the colour theme to apply; null (or an unknown name) uses the user's default.</summary>
    public string? ThemeName { get; set; }

    /// <summary>
    ///     Grid width in columns (clamped to 8..400). The texture is sized from this and the theme's
    ///     font cell, so the shell sees about this many columns (a small font on a small grid can
    ///     yield a few more rows because of a minimum texture size). Default 40.
    /// </summary>
    public int Cols { get; set; } = 40;

    /// <summary>Grid height in rows (clamped to 4..200). Default 12.</summary>
    public int Rows { get; set; } = 12;

    /// <summary>Id of the anchor vehicle; empty (default) means the controlled vehicle.</summary>
    public string VehicleId { get; set; } = "";

    /// <summary>
    ///     Id of the anchor top-level part within the vehicle (the part's instance name, or its
    ///     template id). Several parts can share one id; the first match wins. Empty means the
    ///     vehicle's first part. Prefer <see cref="PartInstanceId"/> when it is known.
    /// </summary>
    public string PartId { get; set; } = "";

    /// <summary>
    ///     The game's per-session unique id of the anchor part (<c>Part.InstanceId</c>). When set it
    ///     is preferred over <see cref="PartId"/> and is looked up across all vehicles, so it keeps
    ///     working after the part moves to another vehicle. Not stable across game sessions.
    /// </summary>
    public uint? PartInstanceId { get; set; }

    /// <summary>Id of a sub-part of the anchor part to anchor to instead; empty means the part itself.</summary>
    public string SubPartId { get; set; } = "";

    /// <summary>Quad offset along X (part-local metres, or screen metres for a billboard). Default 0.</summary>
    public float OffsetX { get; set; }

    /// <summary>Quad offset along Y (part-local metres, or screen metres for a billboard). Default 0.</summary>
    public float OffsetY { get; set; }

    /// <summary>
    ///     Quad offset along Z in the part's local frame, in metres (positive faces out of the
    ///     part's +Z side). Part anchors only. Default 0.05.
    /// </summary>
    public float OffsetZ { get; set; } = 0.05f;

    /// <summary>Rotation about X in degrees (applied X, then Y, then Z). Default 0.</summary>
    public float RotationX { get; set; }

    /// <summary>Rotation about Y in degrees. Default 0.</summary>
    public float RotationY { get; set; }

    /// <summary>Rotation about Z in degrees. Default 0.</summary>
    public float RotationZ { get; set; }

    /// <summary>Quad width in metres (clamped to 0.05..100). Default 0.6.</summary>
    public float WidthMeters { get; set; } = 0.6f;

    /// <summary>Quad height in metres (clamped to 0.05..100). Default 0.2.</summary>
    public float HeightMeters { get; set; } = 0.2f;

    /// <summary>Background opacity 0..1 (the see-through level of the whole panel); null keeps the theme's value.</summary>
    public float? BackgroundOpacity { get; set; }

    /// <summary>Text opacity 0..1; null keeps the theme's value.</summary>
    public float? ForegroundOpacity { get; set; }

    /// <summary>Per-cell background opacity 0..1; null keeps the theme's value.</summary>
    public float? CellBackgroundOpacity { get; set; }

    /// <summary>
    ///     Pin the quad in the camera's view instead of anchoring it to a part. Equivalent to
    ///     using <see cref="TerminalPlacementKind.InWorldBillboard"/>; either selects billboard mode.
    /// </summary>
    public bool Billboard { get; set; }

    /// <summary>Billboard distance from the camera in metres (clamped to 0.2..200). Default 5.</summary>
    public float BillboardDistance { get; set; } = 5f;

    /// <summary>Billboard ignores scene depth (always drawn on top). Default true.</summary>
    public bool BillboardAlwaysOnTop { get; set; } = true;

    /// <summary>
    ///     A billboard can be focused (and forwards mouse input to the program) by clicking it.
    ///     Part-anchored terminals are always clickable. Default true.
    /// </summary>
    public bool BillboardClickToFocus { get; set; } = true;

    /// <summary>Give the new terminal input focus after creation. Default false.</summary>
    public bool Focus { get; set; }
}
