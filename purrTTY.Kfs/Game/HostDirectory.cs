using System.Reflection;
using KSA.Kfs;
using KSA.Kfs.Host;

namespace purrTTY.Kfs.Game;

/// <summary>
/// The game's live kFS hosts: <c>KfsVehicleHost.AllHosts</c>
/// (<c>internal static readonly List&lt;KfsVehicleHost&gt;</c>), reached once by reflection and
/// then always read under that list's own lock — the game takes <c>lock (AllHosts)</c> for every
/// access (host creation on the main thread in the UI phase, <c>ReleaseDead</c> and the per-frame
/// copy in <c>KfsIntegration.BeforeInputEvents</c>), so ours never races it.
/// </summary>
public sealed class HostDirectory
{
    /// <summary>The field's name in <c>KSA.Kfs.KfsVehicleHost</c>.</summary>
    public const string FieldName = "AllHosts";

    private readonly List<KfsVehicleHost> _hosts;

    private HostDirectory(List<KfsVehicleHost> hosts) => _hosts = hosts;

    /// <summary>The game's own field, or null when this build has none.</summary>
    public static FieldInfo? FindField() =>
        typeof(KfsVehicleHost).GetField(FieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

    /// <summary>
    /// Resolves the directory through <paramref name="findField"/> (default <see cref="FindField"/>):
    /// the field must exist, be static, hold a <c>List&lt;KfsVehicleHost&gt;</c> and not be null.
    /// Returns null and the reason in words otherwise. Changes nothing.
    /// </summary>
    public static HostDirectory? Resolve(Func<FieldInfo?>? findField, out string? reason)
    {
        FieldInfo? field;
        try
        {
            field = (findField ?? FindField)();
        }
        catch (Exception e)
        {
            reason = $"field KfsVehicleHost.{FieldName} could not be resolved: {e.Message}";
            return null;
        }
        if (field is null)
        {
            reason = $"field KfsVehicleHost.{FieldName} not found";
            return null;
        }
        if (!field.IsStatic || !typeof(List<KfsVehicleHost>).IsAssignableFrom(field.FieldType))
        {
            reason = $"field KfsVehicleHost.{FieldName} is {(field.IsStatic ? "" : "not static and ")}of type {field.FieldType.Name}, not a static List<KfsVehicleHost>";
            return null;
        }
        object? value;
        try
        {
            value = field.GetValue(null);
        }
        catch (Exception e)
        {
            reason = $"field KfsVehicleHost.{FieldName} could not be read: {e.Message}";
            return null;
        }
        if (value is not List<KfsVehicleHost> hosts)
        {
            reason = $"field KfsVehicleHost.{FieldName} is null";
            return null;
        }
        reason = null;
        return new HostDirectory(hosts);
    }

    /// <summary>
    /// Copies the hosts into <paramref name="into"/> (cleared first) under the list's lock. No
    /// allocation once <paramref name="into"/> has grown to the host count. Main thread.
    /// </summary>
    public void Snapshot(List<KfsVehicleHost> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        lock (_hosts)
        {
            into.AddRange(_hosts);
        }
    }

    /// <summary>
    /// The host whose <see cref="KfsVehicleHost.Computer"/> is <paramref name="computer"/>, or null
    /// (any thread: the dispatch-thread fallback for a host the pump has not seen yet). Scans under
    /// the lock without allocating.
    /// </summary>
    public KfsVehicleHost? FindByComputer(VehicleComputer computer)
    {
        lock (_hosts)
        {
            for (int i = 0; i < _hosts.Count; i++)
            {
                if (ReferenceEquals(_hosts[i].Computer, computer)) return _hosts[i];
            }
        }
        return null;
    }

    /// <summary>The number of hosts now.</summary>
    public int Count
    {
        get { lock (_hosts) return _hosts.Count; }
    }
}
