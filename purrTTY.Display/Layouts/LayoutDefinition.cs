using purrTTY.Display.Ghostty;

namespace purrTTY.Display.Layouts;

/// <summary>
/// A saved <b>set</b> of terminals — one TOML file under
/// <c>&lt;config&gt;/.purrTTY/layouts/</c> (see <see cref="LayoutCatalog"/> /
/// <see cref="LayoutTomlFormat"/>). Applied and torn down as a unit by the GameMod
/// <c>LayoutManager</c>; nothing is ever applied automatically.
/// </summary>
public sealed class TerminalLayout
{
    public LayoutHeader Header { get; set; } = new();
    public List<TerminalEntry> Terminals { get; set; } = new();
}

/// <summary>The <c>[layout]</c> header: display name + optional description.</summary>
public sealed class LayoutHeader
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

/// <summary>
/// One terminal in a layout. Kind-specific placement fields are nullable so a window
/// entry never carries in-world fields on disk and vice-versa (the GameMod mapper
/// supplies defaults for absent values). 2D windows persist <b>position + size only</b>
/// — cols/rows and font are derived live by the emulator from the window's pixel size
/// and the resolved theme; in-world terminals persist cols/rows (authoritative texture
/// extent) plus the anchor and transform.
/// </summary>
public sealed class TerminalEntry
{
    public string Name { get; set; } = "";

    public TerminalKind Kind { get; set; } = TerminalKind.Window;

    /// <summary>Named theme to apply (resolved via the ThemeCatalog); null = the global default.</summary>
    public string? Theme { get; set; }

    // ---- 2D window placement: position + size (px) ONLY. Null on in-world entries. ----
    public float? PosX { get; set; }
    public float? PosY { get; set; }
    public float? Width { get; set; }
    public float? Height { get; set; }

    // ---- in-world placement. Null on window entries. cols/rows are authoritative. ----
    public int? Cols { get; set; }
    public int? Rows { get; set; }
    public string? Mode { get; set; }            // "part" | "billboard"
    public string? VehicleId { get; set; }
    public string? PartId { get; set; }
    public string? PartName { get; set; }         // informational only (human re-pick aid)
    public string? SubPartId { get; set; }
    public float? OffsetX { get; set; }
    public float? OffsetY { get; set; }
    public float? OffsetZ { get; set; }
    public float? RotationX { get; set; }
    public float? RotationY { get; set; }
    public float? RotationZ { get; set; }
    public float? WidthMeters { get; set; }
    public float? HeightMeters { get; set; }
    public float? BillboardDistance { get; set; }
    public float? BillboardOffsetX { get; set; }
    public float? BillboardOffsetY { get; set; }
    public float? BillboardWidthMeters { get; set; }
    public float? BillboardHeightMeters { get; set; }
    public float? BillboardRotationX { get; set; }
    public float? BillboardRotationY { get; set; }
    public float? BillboardRotationZ { get; set; }
    public bool? BillboardAlwaysOnTop { get; set; }

    /// <summary>Shell + startup command for this terminal.</summary>
    public ShellSpec Shell { get; set; } = new();
}
