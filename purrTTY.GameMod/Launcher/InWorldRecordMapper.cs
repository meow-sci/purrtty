using purrTTY.Core.Terminal;
using purrTTY.Display.Launcher;
using purrTTY.GameMod.InWorld.Settings;

namespace purrTTY.GameMod.Launcher;

/// <summary>
/// Copies a resolved <see cref="InWorldPlacement"/> (pure data from <c>purrTTY.Display</c>) into an
/// <see cref="InWorldTerminalRecord"/> — the one type the pure mapper cannot produce because the
/// record lives in this assembly. The placement carries one shared set of transform fields; the
/// record has separate part and billboard fields, so the mode decides which set is written.
/// </summary>
internal static class InWorldRecordMapper
{
    /// <summary>Builds the record for a new in-world terminal.</summary>
    public static InWorldTerminalRecord ToRecord(InWorldPlacement placement, ProcessLaunchOptions launch)
    {
        var record = new InWorldTerminalRecord
        {
            Name = placement.Name,
            Cols = placement.Cols,
            Rows = placement.Rows,
            Launch = launch,
            ThemeName = placement.ThemeName,
            Mode = placement.IsBillboard ? InWorldTerminalRecord.ModeBillboard : InWorldTerminalRecord.ModePart,
        };

        ApplyPlacement(record, placement, placement.IsBillboard);
        return record;
    }

    /// <summary>
    /// Writes the anchor ids and the transform of one mode into <paramref name="record"/> (the quad
    /// reads the record every frame, so this is a live edit). The record's own mode is not changed.
    /// </summary>
    /// <param name="record">The record to update.</param>
    /// <param name="placement">The resolved placement.</param>
    /// <param name="billboard">Which transform set to write: the billboard fields or the part fields.</param>
    public static void ApplyPlacement(InWorldTerminalRecord record, InWorldPlacement placement, bool billboard)
    {
        record.TargetVehicleId = placement.VehicleId;
        record.TargetPartId = placement.PartId;
        record.TargetPartInstanceId = placement.PartInstanceId;
        record.TargetSubPartId = placement.SubPartId;

        if (billboard)
        {
            record.BillboardOffsetX = placement.OffsetX;
            record.BillboardOffsetY = placement.OffsetY;
            record.BillboardRotationX = placement.RotationX;
            record.BillboardRotationY = placement.RotationY;
            record.BillboardRotationZ = placement.RotationZ;
            record.BillboardWidthMeters = placement.WidthMeters;
            record.BillboardHeightMeters = placement.HeightMeters;
            record.BillboardDistance = placement.BillboardDistance;
            record.BillboardAlwaysOnTop = placement.BillboardAlwaysOnTop;
            record.BillboardClickToFocus = placement.BillboardClickToFocus;
        }
        else
        {
            record.PartOffsetX = placement.OffsetX;
            record.PartOffsetY = placement.OffsetY;
            record.PartOffsetZ = placement.OffsetZ;
            record.PartRotationX = placement.RotationX;
            record.PartRotationY = placement.RotationY;
            record.PartRotationZ = placement.RotationZ;
            record.PartWidthMeters = placement.WidthMeters;
            record.PartHeightMeters = placement.HeightMeters;
        }
    }
}
