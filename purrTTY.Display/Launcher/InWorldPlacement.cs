namespace purrTTY.Display.Launcher;

/// <summary>
/// The fully resolved, clamped form of a launcher in-world request
/// (<c>TerminalInWorldOptions</c> + placement kind): every value the in-world coordinator needs,
/// as plain data. <see cref="TerminalLaunchMapper.ToInWorldPlacement"/> produces it; the
/// GameMod layer — the only one that sees <c>InWorldTerminalRecord</c> — copies it into a
/// record. The part and billboard transforms share one set of fields here
/// (<see cref="OffsetX"/>, <see cref="RotationX"/>, <see cref="WidthMeters"/>, ...); which record
/// fields they land in is decided by <see cref="Mode"/>.
/// </summary>
public sealed record InWorldPlacement
{
    /// <summary>The <see cref="Mode"/> value of a part-anchored terminal (matches <c>InWorldTerminalRecord.ModePart</c>).</summary>
    public const string ModePart = "part";

    /// <summary>The <see cref="Mode"/> value of a camera billboard (matches <c>InWorldTerminalRecord.ModeBillboard</c>).</summary>
    public const string ModeBillboard = "billboard";

    /// <summary>Registry name: the requested name, or the fallback when none was requested.</summary>
    public required string Name { get; init; }

    /// <summary>Theme name to apply; null = the user's default.</summary>
    public string? ThemeName { get; init; }

    /// <summary>Grid width in columns, clamped to the in-world range.</summary>
    public int Cols { get; init; }

    /// <summary>Grid height in rows, clamped to the in-world range.</summary>
    public int Rows { get; init; }

    /// <summary><see cref="ModePart"/> or <see cref="ModeBillboard"/>.</summary>
    public required string Mode { get; init; }

    /// <summary>Anchor vehicle id; empty = the controlled vehicle.</summary>
    public string VehicleId { get; init; } = "";

    /// <summary>Anchor top-level part id; empty = the vehicle's first part.</summary>
    public string PartId { get; init; } = "";

    /// <summary>Game-assigned <c>Part.InstanceId</c> of the anchor part; preferred over <see cref="PartId"/> when set.</summary>
    public uint? PartInstanceId { get; init; }

    /// <summary>Anchor sub-part id; empty = the part itself.</summary>
    public string SubPartId { get; init; } = "";

    /// <summary>Quad offset X (part-local metres, or screen metres for a billboard).</summary>
    public float OffsetX { get; init; }

    /// <summary>Quad offset Y (part-local metres, or screen metres for a billboard).</summary>
    public float OffsetY { get; init; }

    /// <summary>Quad offset Z in the part frame (metres); unused by a billboard.</summary>
    public float OffsetZ { get; init; }

    /// <summary>Rotation about X in degrees.</summary>
    public float RotationX { get; init; }

    /// <summary>Rotation about Y in degrees.</summary>
    public float RotationY { get; init; }

    /// <summary>Rotation about Z in degrees.</summary>
    public float RotationZ { get; init; }

    /// <summary>Quad width in metres, clamped to the in-world range.</summary>
    public float WidthMeters { get; init; }

    /// <summary>Quad height in metres, clamped to the in-world range.</summary>
    public float HeightMeters { get; init; }

    /// <summary>Background opacity 0..1, or null to keep the theme's value.</summary>
    public float? BackgroundOpacity { get; init; }

    /// <summary>Text opacity 0..1, or null to keep the theme's value.</summary>
    public float? ForegroundOpacity { get; init; }

    /// <summary>Cell-background opacity 0..1, or null to keep the theme's value.</summary>
    public float? CellBackgroundOpacity { get; init; }

    /// <summary>Billboard distance from the camera in metres, clamped to the in-world range.</summary>
    public float BillboardDistance { get; init; }

    /// <summary>Billboard ignores scene depth.</summary>
    public bool BillboardAlwaysOnTop { get; init; }

    /// <summary>Billboard can be focused by clicking it.</summary>
    public bool BillboardClickToFocus { get; init; }

    /// <summary>Focus the terminal after creation.</summary>
    public bool Focus { get; init; }

    /// <summary>True when <see cref="Mode"/> is <see cref="ModeBillboard"/>.</summary>
    public bool IsBillboard => string.Equals(Mode, ModeBillboard, StringComparison.Ordinal);

    /// <summary>True when any of the three opacities was requested.</summary>
    public bool HasOpacityOverride =>
        BackgroundOpacity.HasValue || ForegroundOpacity.HasValue || CellBackgroundOpacity.HasValue;
}
