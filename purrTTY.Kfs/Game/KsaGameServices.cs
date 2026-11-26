using System.Reflection;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Game;

/// <summary>
/// The game services <c>purrTTY.GameMod</c> installs the extension with (see
/// <see cref="IKfsGameServices"/>): <see cref="HostDirectory"/> resolved at
/// <see cref="Prepare"/> (a required capability: a game build without
/// <c>KfsVehicleHost.AllHosts</c> refuses the install); at <see cref="Attach"/> a
/// <see cref="MainThreadPump"/> over <see cref="GameHostSource"/> becomes the one the
/// <c>BeforeInputEvents</c> postfix runs, and its <see cref="MainThreadPump.BindFromDispatch"/>
/// becomes <see cref="PartsRegistry.Fallback"/>; <see cref="Detach"/> clears both and restores
/// every private light template still alive (<see cref="LightOverrides.RestoreAll"/>).
/// <see cref="Create"/> also brings the game console (<see cref="KsaConsoleHost"/>) for the
/// <c>purr</c> command, which the installer registers as its step 9.
/// </summary>
public sealed class KsaGameServices : IKfsGameServices
{
    private readonly Func<FieldInfo?>? _findHosts;
    private HostDirectory? _directory;
    private MainThreadPump? _pump;

    /// <summary>
    /// The services, finding <c>AllHosts</c> through <paramref name="findHosts"/> (default:
    /// <see cref="HostDirectory.FindField"/>; tests inject a failure), with
    /// <paramref name="console"/> for the <c>purr</c> command (default none: offline there is no
    /// game console to register with).
    /// </summary>
    public KsaGameServices(Func<FieldInfo?>? findHosts = null, IKfsConsoleHost? console = null)
    {
        _findHosts = findHosts;
        Console = console;
    }

    /// <summary>The services as the game uses them, the game console included (<see cref="KsaConsoleHost"/>).</summary>
    public static KsaGameServices Create() => new(console: new KsaConsoleHost());

    /// <inheritdoc/>
    public string Name => "game back end";

    /// <inheritdoc/>
    public IKfsConsoleHost? Console { get; }

    /// <summary>The pump while attached.</summary>
    public MainThreadPump? Pump => Volatile.Read(ref _pump);

    /// <summary>The resolved host directory, after a successful <see cref="Prepare"/>.</summary>
    public HostDirectory? Directory => _directory;

    /// <inheritdoc/>
    public string? Prepare()
    {
        _directory = HostDirectory.Resolve(_findHosts, out string? reason);
        return _directory is null ? reason ?? "host directory unavailable" : null;
    }

    /// <inheritdoc/>
    public void Attach()
    {
        HostDirectory directory = _directory ?? throw new InvalidOperationException("Prepare did not succeed");
        var pump = new MainThreadPump(new GameHostSource(directory));
        Volatile.Write(ref _pump, pump);
        MainThreadPump.Activate(pump);
        PartsRegistry.Fallback = pump.BindFromDispatchDelegate;
    }

    /// <inheritdoc/>
    public void Detach()
    {
        MainThreadPump? pump = Interlocked.Exchange(ref _pump, null);
        try
        {
            if (pump is not null)
            {
                if (ReferenceEquals(PartsRegistry.Fallback, pump.BindFromDispatchDelegate)) PartsRegistry.Fallback = null;
                MainThreadPump.Deactivate(pump);
            }
        }
        catch (Exception e)
        {
            KfsLog.Once("detach game services", e);
        }
        LightOverrides.RestoreAll();   // never throws
    }
}
