using System.Globalization;
using System.Text;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Commands;

/// <summary>
/// The index tables of <c>purr parts</c> (plan §4.13): for each part kind, one row per slot of the
/// frozen lists — index, part name and the facts a program author needs to pick an index. A pure
/// formatter over <see cref="IVehicleParts"/>, so the same code formats a simulated vehicle in the
/// tests and the live one in the game. Fixed-width, deterministic, invariant culture.
/// </summary>
/// <remarks>
/// Only reads: <see cref="IVehicleParts.Sets"/>, <see cref="IVehicleParts.IsPresent"/>,
/// <see cref="IVehicleParts.HasSwitch"/>, <see cref="IVehicleParts.Dirty"/> and the getters, all of
/// which a back end must allow concurrently with the physics step (they are the dispatch thread's
/// reads). Safe on the main thread during the UI phase.
/// </remarks>
public static class PartsListing
{
    /// <summary>The widest part-name column; longer names are cut with <c>…</c>.</summary>
    public const int MaxNameWidth = 32;

    private const int MinNameWidth = 8;

    /// <summary>
    /// The listing: a heading naming the vehicle and whose lists these are, then a section per
    /// requested kind (<see cref="PartKind.All"/> for every kind).
    /// </summary>
    public static IReadOnlyList<string> Lines(ControlledParts vehicle, PartKind kind = PartKind.All)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        IVehicleParts parts = vehicle.Parts;
        PartSets sets = parts.Sets;
        var lines = new List<string>();

        lines.Add(Heading(vehicle.VehicleName, vehicle.Source, sets.Generation));
        if (vehicle.Source != PartsListingSource.Preview && parts.Dirty)
            lines.Add("  the vehicle has changed since these lists were built: x_parts_rescan() (or a new run) renumbers");

        int width = NameWidth(sets, kind);
        if (kind is PartKind.All or PartKind.Light) Lights(lines, parts, sets, width);
        if (kind is PartKind.All or PartKind.Deployable) Deployables(lines, parts, sets, width);
        if (kind is PartKind.All or PartKind.Actuator) Actuators(lines, parts, sets, width);
        return lines;
    }

    /// <summary>The heading line.</summary>
    public static string Heading(string vehicleName, PartsListingSource source, int generation) => source switch
    {
        PartsListingSource.RunningProgram => Invariant($"vehicle \"{vehicleName}\" — parts as its running program sees them (generation {generation})"),
        PartsListingSource.Computer => Invariant($"vehicle \"{vehicleName}\" — parts as its kFS computer holds them (generation {generation}); a program started now gets fresh lists"),
        _ => $"vehicle \"{vehicleName}\" — parts as a program started now would see them (preview: indices are fixed when a program starts)",
    };

    private static void Lights(List<string> lines, IVehicleParts parts, PartSets sets, int width)
    {
        lines.Add(Section("lights", sets.Lights.Length, "type, switch, on/off, lit/dark"));
        bool anyOverridden = false;
        foreach (LightSlot slot in sets.Lights)
        {
            var row = new StringBuilder(Head(slot, width));
            if (!parts.IsPresent(slot))
            {
                lines.Add(row.Append("not present").ToString());
                continue;
            }
            bool point = (LightType)parts.LightGetI(slot.Index, LightProp.Type) == LightType.Point;
            row.Append(Pad(point ? "point" : "spot", 7));
            if (parts.HasSwitch(slot))
            {
                row.Append(Pad(Invariant($"switch group {slot.SwitchGroup}"), 17));
                row.Append(Pad(parts.LightGetI(slot.Index, LightProp.On) != 0 ? "on" : "off", 5));
            }
            else
            {
                row.Append(Pad("no switch", 17)).Append(Pad("", 5));
            }
            row.Append(parts.LightGetI(slot.Index, LightProp.Lit) != 0 ? "lit " : "dark");
            if (parts.LightGetI(slot.Index, LightProp.Overridden) != 0)
            {
                row.Append("  overridden");
                anyOverridden = true;
            }
            lines.Add(row.ToString().TrimEnd());
        }
        if (anyOverridden) lines.Add(OverriddenNote);
    }

    /// <summary>The line under the lights when any is overridden: such a look outlives the program.</summary>
    public const string OverriddenNote = "  overridden: a program's colour, cone, intensity or range, kept after it stops; purr lights reset restores the stock look";

    private static void Deployables(List<string> lines, IVehicleParts parts, PartSets sets, int width)
    {
        lines.Add(Section("deployables", sets.Deployables.Length, "state, position, travel time"));
        foreach (AnimSlot slot in sets.Deployables)
        {
            var row = new StringBuilder(Head(slot, width));
            if (!parts.IsPresent(slot))
            {
                lines.Add(row.Append("not present").ToString());
                continue;
            }
            var state = (DeployState)parts.DeployGetI(slot.Index, DeployProp.State);
            float position = parts.DeployGetF(slot.Index, DeployProp.Position);
            float duration = parts.DeployGetF(slot.Index, DeployProp.DurationS);
            row.Append(Pad(PartsText.StateName(state), 12));
            row.Append(Invariant($"{Percent(position),4}%  "));
            row.Append(Seconds(duration));
            row.Append(Links(slot.Links));
            lines.Add(row.ToString().TrimEnd());
        }
    }

    private static void Actuators(List<string> lines, IVehicleParts parts, PartSets sets, int width)
    {
        lines.Add(Section("actuators", sets.Actuators.Length, "position → target, travel time"));
        foreach (AnimSlot slot in sets.Actuators)
        {
            var row = new StringBuilder(Head(slot, width));
            if (!parts.IsPresent(slot))
            {
                lines.Add(row.Append("not present").ToString());
                continue;
            }
            float position = parts.ActuatorGetF(slot.Index, ActuatorProp.Position);
            float target = parts.ActuatorGetF(slot.Index, ActuatorProp.Target);
            float duration = parts.ActuatorGetF(slot.Index, ActuatorProp.DurationS);
            row.Append(Invariant($"{position:0.00} → {target:0.00}  "));
            row.Append(Seconds(duration));
            row.Append(Links(slot.Links));
            lines.Add(row.ToString().TrimEnd());
        }
    }

    // ------------------------------------------------------------------ helpers

    private static string Section(string noun, int count, string columns) =>
        count == 0 ? Invariant($"{noun}: none") : Invariant($"{noun} ({count}): {columns}");

    private static string Head(PartSlot slot, int width) =>
        Invariant($"  {slot.Index,3}  ") + Pad(Name(slot.PartName, width), width + 2);

    private static int NameWidth(PartSets sets, PartKind kind)
    {
        int width = MinNameWidth;
        if (kind is PartKind.All or PartKind.Light) foreach (PartSlot s in sets.Lights) width = Math.Max(width, s.PartName.Length);
        if (kind is PartKind.All or PartKind.Deployable) foreach (PartSlot s in sets.Deployables) width = Math.Max(width, s.PartName.Length);
        if (kind is PartKind.All or PartKind.Actuator) foreach (PartSlot s in sets.Actuators) width = Math.Max(width, s.PartName.Length);
        return Math.Min(width, MaxNameWidth);
    }

    private static string Name(string name, int width)
    {
        if (name.Length == 0) return "(unnamed)";
        return name.Length <= width ? name : name[..(width - 1)] + "…";
    }

    private static string Pad(string text, int width) => text.Length >= width ? text + " " : text.PadRight(width);

    private static int Percent(float fraction)
    {
        if (!float.IsFinite(fraction)) return 0;
        return (int)Math.Round(Math.Clamp(fraction, 0f, 1f) * 100f, MidpointRounding.AwayFromZero);
    }

    private static string Seconds(float s) => Invariant($"{(float.IsFinite(s) ? s : 0f),5:0.0} s");

    private static string Links(uint links)
    {
        string text = PartsText.LinksText(links);
        return text.Length == 0 ? "" : "  " + text[2..];   // LinksText prefixes every note with ", "
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
