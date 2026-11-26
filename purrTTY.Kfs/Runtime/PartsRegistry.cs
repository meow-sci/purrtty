using System.Runtime.CompilerServices;
using KSA.Kfs.Host;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// <see cref="VehicleComputer"/> → <see cref="VehicleBinding"/> for the whole process (plan §4.7).
/// Lookup is a <see cref="ConditionalWeakTable{TKey, TValue}"/> (lock-free, allocation-free, never
/// keeps a computer alive); <see cref="Live"/> is a strong copy-on-write list the pump iterates.
/// Every creation and removal happens under one lock, because a binding can be asked for from the
/// pump (main), the console (main, UI phase) and <see cref="Find"/> (dispatch) at once and must be
/// created exactly once.
/// </summary>
/// <remarks>
/// <para>Binding touches nothing of the computer: in particular it does not wrap
/// <see cref="VehicleComputer.ImageLoaded"/> (the game's host owns that single delegate slot). A new
/// run is detected by the binding itself (<see cref="VehicleBinding.SyncRun"/>).</para>
/// <para>Bindings are created only while the registry is open: <see cref="KfsExtension"/> opens it
/// as the last step of a successful install and closes it, under the same lock, before unbinding
/// everything at uninstall. So a dispatch thread that read <see cref="Fallback"/> just before an
/// uninstall cannot leave behind a binding that no pump would ever prune: its
/// <see cref="Bind"/> throws. The test runtime installs through <see cref="KfsExtension"/> too, so
/// it binds while open.</para>
/// </remarks>
public static class PartsRegistry
{
    private static readonly object Gate = new();
    private static readonly ConditionalWeakTable<VehicleComputer, VehicleBinding> Table = new();
    private static VehicleBinding[] _live = [];
    private static Func<VehicleComputer, VehicleBinding?>? _fallback;
    private static bool _open;

    /// <summary>Every binding not yet unbound, in creation order. A snapshot: safe to iterate while others bind.</summary>
    public static IReadOnlyList<VehicleBinding> Live => Volatile.Read(ref _live);

    /// <summary>True while bindings may be created (the extension is installed).</summary>
    public static bool IsOpen
    {
        get { lock (Gate) return _open; }
    }

    /// <summary>
    /// Consulted by <see cref="Find"/> for a computer that has no binding yet — in the game, host
    /// discovery on the dispatch thread for a host created, loaded and ticked before the pump saw
    /// it. It should locate the computer's vehicle and call <see cref="Bind"/>; it runs on the
    /// dispatch thread, so it must not block on main-thread work. Null (the default) for none.
    /// An exception from it is caught, logged once and treated as "not found".
    /// </summary>
    public static Func<VehicleComputer, VehicleBinding?>? Fallback
    {
        get => Volatile.Read(ref _fallback);
        set => Volatile.Write(ref _fallback, value);
    }

    /// <summary>
    /// Binds <paramref name="computer"/> to <paramref name="parts"/>. When the computer is already
    /// bound the existing binding is returned and <paramref name="parts"/> is not used.
    /// </summary>
    /// <exception cref="InvalidOperationException">The registry is closed (the extension is not installed).</exception>
    public static VehicleBinding Bind(VehicleComputer computer, IVehicleParts parts)
    {
        ArgumentNullException.ThrowIfNull(computer);
        ArgumentNullException.ThrowIfNull(parts);
        lock (Gate)
        {
            if (!_open) throw new InvalidOperationException("the purrTTY kFS extension is not installed: no vehicle can be bound");
            if (Table.TryGetValue(computer, out VehicleBinding? existing)) return existing;

            var binding = new VehicleBinding(computer, parts);
            Table.Add(computer, binding);
            Volatile.Write(ref _live, [.. _live, binding]);
            return binding;
        }
    }

    /// <summary>
    /// The binding of <paramref name="computer"/>, or — when it has none — whatever
    /// <see cref="Fallback"/> produces; null when neither knows it. Allocation-free on a hit.
    /// </summary>
    public static VehicleBinding? Find(VehicleComputer computer)
    {
        if (Table.TryGetValue(computer, out VehicleBinding? binding)) return binding;
        var fallback = Fallback;
        if (fallback is null) return null;
        try
        {
            return fallback(computer);
        }
        catch (Exception e)
        {
            KfsLog.Once("registry fallback", e);
            return null;
        }
    }

    /// <summary>
    /// Removes the binding of <paramref name="computer"/>: lookups no longer find it and its queued
    /// commands are discarded. False when it was not bound.
    /// </summary>
    public static bool Unbind(VehicleComputer computer)
    {
        lock (Gate)
        {
            if (!Table.TryGetValue(computer, out VehicleBinding? binding)) return false;
            Table.Remove(computer);
            binding.MarkUnbound();
            Volatile.Write(ref _live, _live.Where(b => !ReferenceEquals(b, binding)).ToArray());
            return true;
        }
    }

    /// <summary>Unbinds every live binding (end of a test runtime); <see cref="Close"/> also does.</summary>
    public static void UnbindAll()
    {
        lock (Gate) UnbindAllLocked();
    }

    /// <summary>Opens the registry for bindings (the installer's last step).</summary>
    internal static void Open()
    {
        lock (Gate) _open = true;
    }

    /// <summary>Closes the registry and unbinds everything, atomically with respect to <see cref="Bind"/> (uninstall).</summary>
    internal static void Close()
    {
        lock (Gate)
        {
            _open = false;
            UnbindAllLocked();
        }
    }

    private static void UnbindAllLocked()
    {
        foreach (VehicleBinding binding in _live)
        {
            Table.Remove(binding.Computer);
            binding.MarkUnbound();
        }
        Volatile.Write(ref _live, []);
    }
}
