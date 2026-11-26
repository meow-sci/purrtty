using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// One accepted command, queued on the dispatch thread and applied by the pump on the main thread.
/// It carries the <see cref="PartSlot"/> resolved at accept time — never an index, because the
/// lists can be rebuilt (a new run, a rescan earlier in the same batch) between accept and apply —
/// and the decoded arguments, unclamped (the back end clamps with <see cref="PartMath"/>).
/// </summary>
/// <remarks>
/// Argument use per service: <c>x_light_set_on</c> <see cref="U"/> (0/1);
/// <c>x_light_set_color</c> <see cref="A"/>, <see cref="B"/>, <see cref="C"/> = r, g, b;
/// <c>x_light_set_cone</c> <see cref="A"/> = inner, <see cref="B"/> = outer;
/// <c>x_light_set_intensity</c>, <c>x_light_set_range</c>, <c>x_actuator_set</c> <see cref="A"/>;
/// <c>x_lights_set_all</c>, <c>x_deploy_set</c> <see cref="U"/> (0/1);
/// <c>x_light_reset</c>, <c>x_deploy_toggle</c>, <c>x_parts_rescan</c> nothing.
/// <c>x_lights_set_all</c> and <c>x_parts_rescan</c> have no slot.
/// </remarks>
/// <param name="Service">The service id (<see cref="Svc"/>).</param>
/// <param name="Slot">The addressed slot, or null for a vehicle-wide command.</param>
/// <param name="U">An integer argument (on/off, deployed).</param>
/// <param name="A">First float argument.</param>
/// <param name="B">Second float argument.</param>
/// <param name="C">Third float argument.</param>
public readonly record struct PartCommand(ushort Service, PartSlot? Slot, uint U = 0, float A = 0f, float B = 0f, float C = 0f)
{
    /// <summary>The service's kC name (<c>x_…</c>), for texts.</summary>
    public string Name => ServiceNames.Of(Service);
}

/// <summary>Service id → kC name, without allocating.</summary>
internal static class ServiceNames
{
    private static readonly string?[] Names = Build();

    /// <summary>The kC name of <paramref name="id"/>, or <c>0x…</c> for an id outside the catalogue.</summary>
    public static string Of(ushort id)
    {
        int slot = id - KfsBuildInfo.ServiceIdFirst;
        return (uint)slot < (uint)Names.Length && Names[slot] is { } name ? name : $"0x{id:X4}";
    }

    private static string?[] Build()
    {
        var names = new string?[KfsBuildInfo.ServiceIdLast - KfsBuildInfo.ServiceIdFirst + 1];
        foreach (var s in XCatalog.Services) names[s.Id - KfsBuildInfo.ServiceIdFirst] = s.Name;
        return names;
    }
}
