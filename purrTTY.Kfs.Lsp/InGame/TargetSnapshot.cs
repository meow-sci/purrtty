using KSA.Kfs.Host.Services;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Lsp.InGame;

/// <summary>
/// The in-game host's build target (plan §6.7): refreshed once per frame on the main thread from the
/// workbench's target — the part, the layout object and the name the stock editor captures before a
/// background build — and read by the build scheduler on any thread as an immutable
/// <see cref="BuildTarget"/>.
/// </summary>
/// <remarks>
/// <see cref="Refresh"/> compares by reference (the part and layout objects, the name string, then
/// the name's text): an unchanged target costs four compares and allocates nothing. A change
/// publishes a new snapshot and raises <see cref="Changed"/> on the refreshing thread (the language
/// servers then re-diagnose their open projects — a lock and a timer each).
/// </remarks>
public sealed class TargetSnapshot
{
    private BuildTarget _current;

    /// <summary>A holder starting at <paramref name="initial"/>.</summary>
    public TargetSnapshot(BuildTarget initial)
    {
        _current = initial ?? throw new ArgumentNullException(nameof(initial));
    }

    /// <summary>The current target (any thread).</summary>
    public BuildTarget Current => Volatile.Read(ref _current);

    /// <summary>Raised after a real change, on the thread that refreshed. Handlers' exceptions are swallowed.</summary>
    public event Action? Changed;

    /// <summary>How many times the target changed.</summary>
    public int Changes { get; private set; }

    /// <summary>Publishes the target unless it is the current one; true when it changed.</summary>
    public bool Refresh(PartDescription part, IoLayout layout, string name, bool isVehicle)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(name);
        BuildTarget current = _current;
        if (ReferenceEquals(current.Part, part) && ReferenceEquals(current.Layout, layout) && current.IsVehicle == isVehicle
            && (ReferenceEquals(current.DisplayName, name) || string.Equals(current.DisplayName, name, StringComparison.Ordinal)))
            return false;
        Volatile.Write(ref _current, new BuildTarget(part, layout, name) { IsVehicle = isVehicle });
        Changes++;
        Action? handlers = Changed;
        if (handlers is null) return true;
        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try { handler(); }
            catch { /* one server must not stop the others */ }
        }
        return true;
    }
}
