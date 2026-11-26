using System.Globalization;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// Every text the runtime writes to a program's log: host rejections (plan §4.9), the rescan
/// event, apply-time reports and the <c>x_parts_describe</c> table. Invariant culture throughout.
/// </summary>
public static class PartsText
{
    /// <summary>Rejection when a computer has no parts binding (should not happen).</summary>
    public const string NoBinding = "no vehicle parts are bound";

    /// <summary>Rejection of a second <c>x_parts_describe</c> within one second of computer time.</summary>
    public const string DescribeLimited = "at most one table per second";

    /// <summary>Rejection of an id in the block that this version does not define.</summary>
    public const string UnknownService = "not a purrTTY service in this version";

    /// <summary>Rejection of a request whose handling failed inside purrTTY (a back-end or router fault, logged once).</summary>
    public const string InternalError = "internal error";

    /// <summary><c>light</c>, <c>deployable</c>, <c>actuator</c>.</summary>
    public static string Noun(PartKind kind) => kind switch
    {
        PartKind.Light => "light",
        PartKind.Deployable => "deployable",
        PartKind.Actuator => "actuator",
        _ => "part",
    };

    /// <summary><c>1 light</c>, <c>4 lights</c>.</summary>
    public static string Count(int count, PartKind kind) =>
        count.ToString(CultureInfo.InvariantCulture) + " " + Noun(kind) + (count == 1 ? "" : "s");

    /// <summary><c>light 9 out of range (this vehicle has 4)</c>.</summary>
    public static string OutOfRange(PartKind kind, uint index, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{Noun(kind)} {index} out of range (this vehicle has {count})");

    /// <summary><c>light 2 is no longer on this vehicle</c>.</summary>
    public static string Departed(PartSlot slot) =>
        string.Create(CultureInfo.InvariantCulture, $"{slot.Noun} {slot.Index} is no longer on this vehicle");

    /// <summary><c>light 5 has no switch</c>.</summary>
    public static string NoSwitch(LightSlot slot) =>
        string.Create(CultureInfo.InvariantCulture, $"light {slot.Index} has no switch");

    /// <summary><c>parts rescanned: 4 lights, 2 deployables, 1 actuator (generation 3)</c>.</summary>
    public static string Rescanned(PartSets sets) =>
        string.Create(CultureInfo.InvariantCulture,
            $"parts rescanned: {Count(sets.Lights.Length, PartKind.Light)}, {Count(sets.Deployables.Length, PartKind.Deployable)}, {Count(sets.Actuators.Length, PartKind.Actuator)} (generation {sets.Generation})");

    /// <summary><c>x_light_set_on: light 2 left the vehicle before the command was applied</c>.</summary>
    public static string LeftBeforeApply(in PartCommand command, PartSlot slot) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{command.Name}: {slot.Noun} {slot.Index} left the vehicle before the command was applied");

    /// <summary><c>x_light_set_on: not applied (reason)</c>.</summary>
    public static string NotApplied(in PartCommand command, Exception e) =>
        command.Name + ": not applied (" + (e is PartCommandException ? e.Message : e.GetType().Name + ": " + e.Message) + ")";

    /// <summary><c>x_ commands: queue full, dropped the oldest (more than 4096 waiting)</c>.</summary>
    public static string QueueFull(int limit) =>
        string.Create(CultureInfo.InvariantCulture, $"x_ commands: queue full, dropped the oldest (more than {limit} waiting)");

    /// <summary>
    /// The index table <c>x_parts_describe</c> writes: a heading, then one line per slot of the
    /// requested kind (every kind for <see cref="PartKind.All"/>). Built from the captured
    /// descriptors plus live reads through <paramref name="parts"/>, so it is safe on the dispatch thread.
    /// </summary>
    public static List<string> Describe(IVehicleParts parts, PartKind kind)
    {
        ArgumentNullException.ThrowIfNull(parts);
        PartSets sets = parts.Sets;
        var lines = new List<string>(1 + sets.Count(kind));
        string dirty = parts.Dirty ? "; the vehicle has changed since, x_parts_rescan() renumbers" : "";
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"parts (generation {sets.Generation}): {Count(sets.Lights.Length, PartKind.Light)}, {Count(sets.Deployables.Length, PartKind.Deployable)}, {Count(sets.Actuators.Length, PartKind.Actuator)}{dirty}"));

        if (kind is PartKind.All or PartKind.Light)
        {
            foreach (LightSlot slot in sets.Lights)
            {
                string state;
                if (!parts.IsPresent(slot)) state = "gone";
                else
                {
                    string type = (LightType)parts.LightGetI(slot.Index, LightProp.Type) == LightType.Point ? "point" : "spot";
                    state = parts.HasSwitch(slot)
                        ? string.Create(CultureInfo.InvariantCulture,
                            $"{type}, switch group {slot.SwitchGroup}, {(parts.LightGetI(slot.Index, LightProp.On) != 0 ? "on" : "off")}")
                        : type + ", no switch";
                }
                lines.Add(Head(slot) + state);
            }
        }
        if (kind is PartKind.All or PartKind.Deployable)
        {
            foreach (AnimSlot slot in sets.Deployables)
            {
                string state = !parts.IsPresent(slot)
                    ? "gone"
                    : string.Create(CultureInfo.InvariantCulture,
                        $"{StateName((DeployState)parts.DeployGetI(slot.Index, DeployProp.State))}, {parts.DeployGetF(slot.Index, DeployProp.DurationS):0.##} s{LinksText(slot.Links)}");
                lines.Add(Head(slot) + state);
            }
        }
        if (kind is PartKind.All or PartKind.Actuator)
        {
            foreach (AnimSlot slot in sets.Actuators)
            {
                string state = !parts.IsPresent(slot)
                    ? "gone"
                    : string.Create(CultureInfo.InvariantCulture,
                        $"at {parts.ActuatorGetF(slot.Index, ActuatorProp.Position):0.00} of {parts.ActuatorGetF(slot.Index, ActuatorProp.DurationS):0.##} s{LinksText(slot.Links)}");
                lines.Add(Head(slot) + state);
            }
        }
        return lines;
    }

    /// <summary><c>retracted</c>, <c>deploying</c>, …</summary>
    public static string StateName(DeployState state) => state switch
    {
        DeployState.Deployed => "deployed",
        DeployState.Retracted => "retracted",
        DeployState.Deploying => "deploying",
        DeployState.Retracting => "retracting",
        DeployState.Broken => "broken",
        DeployState.None => "none",
        _ => "?",
    };

    /// <summary>The link flags as text, each prefixed with <c>, </c>; empty for none.</summary>
    public static string LinksText(uint links)
    {
        if (links == 0) return "";
        string text = "";
        if ((links & AnimSlot.LinkSolarPanel) != 0) text += ", drives a solar panel";
        if ((links & AnimSlot.LinkParachute) != 0) text += ", also driven by a parachute";
        if ((links & AnimSlot.LinkLight) != 0) text += ", aims a light";
        return text;
    }

    private static string Head(PartSlot slot) =>
        string.Create(CultureInfo.InvariantCulture, $"{slot.Noun} {slot.Index}: {slot.PartName} (part {slot.PartId}) ");
}
