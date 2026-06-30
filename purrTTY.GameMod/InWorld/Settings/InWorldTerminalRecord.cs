using purrTTY.Core.Terminal;

namespace purrTTY.GameMod.InWorld.Settings;

/// <summary>
///     In-memory configuration for one in-world (render-to-texture) terminal: its
///     name, fixed grid size, anchor mode, and the per-mode placement. <b>Session
///     only</b> — created via the menu / manager dialog and never persisted, so a
///     created terminal does not survive a game reload. Two anchor modes share the
///     same quad: <see cref="ModePart"/> anchors to a vehicle Part/SubPart (occludes
///     correctly), <see cref="ModeBillboard"/> pins the quad in the camera's view
///     (a HUD panel).
///     <para>
///         The grid is fixed at <see cref="Cols"/>×<see cref="Rows"/>; the off-screen
///         texture extent is derived from it and the configured font's cell size at
///         build time (so the terminal is exactly that many columns/rows), rather
///         than the reverse.
///     </para>
/// </summary>
public sealed class InWorldTerminalRecord
{
    public const string ModePart = "part";
    public const string ModeBillboard = "billboard";

    /// <summary>
    ///     User-facing name; the unique addressing key once instances register with
    ///     the terminal target registry (phase 6). One instance today defaults to this.
    /// </summary>
    public string Name { get; set; } = "In-World";

    /// <summary>Fixed terminal grid width in columns (drives the off-screen texture width).</summary>
    public int Cols { get; set; } = 100;

    /// <summary>Fixed terminal grid height in rows (drives the off-screen texture height).</summary>
    public int Rows { get; set; } = 30;

    /// <summary>Shell launch options for this terminal's session; null = the configured default shell.</summary>
    public ProcessLaunchOptions? Launch { get; set; }

    /// <summary>Named theme applied to this terminal; null = the global selected theme.</summary>
    public string? ThemeName { get; set; }

    /// <summary>Anchor mode: <see cref="ModePart"/> or <see cref="ModeBillboard"/>.</summary>
    public string Mode { get; set; } = ModePart;

    // --- Part-anchored mode ---

    /// <summary>Id of the anchor Part/SubPart. Empty = the controlled vessel's first part.</summary>
    public string TargetPartId { get; set; } = "";

    public float PartOffsetX { get; set; }
    public float PartOffsetY { get; set; }
    public float PartOffsetZ { get; set; } = 2f;
    public float PartRotationX { get; set; }
    public float PartRotationY { get; set; }
    public float PartRotationZ { get; set; }
    public float PartWidthMeters { get; set; } = 2f;
    public float PartHeightMeters { get; set; } = 2f;

    // --- Camera-billboard mode ---

    /// <summary>Distance from the camera, in metres, the billboard panel sits at.</summary>
    public float BillboardDistance { get; set; } = 5f;
    public float BillboardOffsetX { get; set; }
    public float BillboardOffsetY { get; set; }
    public float BillboardWidthMeters { get; set; } = 3f;
    public float BillboardHeightMeters { get; set; } = 2f;

    /// <summary>When true the billboard ignores depth (always-on-top HUD); otherwise it is occluded.</summary>
    public bool BillboardAlwaysOnTop { get; set; } = true;

    /// <summary>True when <see cref="Mode"/> selects the camera-billboard anchor.</summary>
    public bool IsBillboard => string.Equals(Mode, ModeBillboard, StringComparison.OrdinalIgnoreCase);

    /// <summary>A field-by-field copy — used to recreate an instance with a changed size/shell.</summary>
    public InWorldTerminalRecord Clone() => new()
    {
        Name = Name,
        Cols = Cols,
        Rows = Rows,
        Launch = Launch,
        ThemeName = ThemeName,
        Mode = Mode,
        TargetPartId = TargetPartId,
        PartOffsetX = PartOffsetX,
        PartOffsetY = PartOffsetY,
        PartOffsetZ = PartOffsetZ,
        PartRotationX = PartRotationX,
        PartRotationY = PartRotationY,
        PartRotationZ = PartRotationZ,
        PartWidthMeters = PartWidthMeters,
        PartHeightMeters = PartHeightMeters,
        BillboardDistance = BillboardDistance,
        BillboardOffsetX = BillboardOffsetX,
        BillboardOffsetY = BillboardOffsetY,
        BillboardWidthMeters = BillboardWidthMeters,
        BillboardHeightMeters = BillboardHeightMeters,
        BillboardAlwaysOnTop = BillboardAlwaysOnTop,
    };
}
