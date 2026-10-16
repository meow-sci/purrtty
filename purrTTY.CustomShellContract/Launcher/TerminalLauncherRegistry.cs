using purrTTY.Logging;

namespace purrTTY.Core.Terminal;

/// <summary>
///     Process-wide rendezvous between purrTTY (which publishes its <see cref="ITerminalLauncher"/>
///     once initialised and clears it on unload) and other mods (which read it). Because the
///     contract assembly is loaded once and shared, both sides see the same static.
///     <para>
///         A consumer should treat <see cref="Current"/> being null as "terminals unavailable" and
///         subscribe to <see cref="Changed"/> to attach when purrTTY finishes loading after it, and
///         to detach when purrTTY unloads.
///     </para>
/// </summary>
public static class TerminalLauncherRegistry
{
    private static readonly object Gate = new();
    private static ITerminalLauncher? _current;

    // Subscription is guarded by the same lock as Current, so the setter can take a
    // consistent snapshot of the handlers and raise them outside the lock.
    private static Action<ITerminalLauncher?>? _changed;

    /// <summary>
    ///     The live launcher, or null while purrTTY is absent or not initialised. Setting a value
    ///     different from the current one (reference comparison) raises <see cref="Changed"/>;
    ///     setting the same value again does nothing. Thread-safe.
    /// </summary>
    public static ITerminalLauncher? Current
    {
        get
        {
            lock (Gate)
            {
                return _current;
            }
        }
        set
        {
            Action<ITerminalLauncher?>? handlers;
            lock (Gate)
            {
                if (ReferenceEquals(_current, value))
                {
                    return;
                }

                _current = value;
                handlers = _changed;
            }

            // Invoked outside the lock so a handler may read or set Current without deadlocking.
            RaiseChanged(handlers, value);
        }
    }

    /// <summary>
    ///     Raised after <see cref="Current"/> changed, with the new value (null when cleared), on the
    ///     thread that set it. Handlers run outside any lock; a handler that throws is logged and
    ///     does not stop the remaining handlers.
    /// </summary>
    public static event Action<ITerminalLauncher?>? Changed
    {
        add
        {
            lock (Gate)
            {
                _changed += value;
            }
        }
        remove
        {
            lock (Gate)
            {
                _changed -= value;
            }
        }
    }

    private static void RaiseChanged(Action<ITerminalLauncher?>? handlers, ITerminalLauncher? value)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<ITerminalLauncher?>)handler)(value);
            }
            catch (Exception ex)
            {
                SafeLogError($"TerminalLauncherRegistry: a Changed handler threw: {ex.Message}");
            }
        }
    }

    private static void SafeLogError(string message)
    {
        try
        {
            LogViaModLog(message);
        }
        catch
        {
            // A broken handler must never be able to take the publisher down, and logging is optional here.
        }
    }

    // The ModLog reference lives in its own non-inlined method: resolving it can throw
    // FileNotFoundException when the game logging assembly chain (purrTTY.Logging ->
    // Brutal.Logging) is absent, e.g. in a test host, and a JIT-time failure inside
    // SafeLogError itself would escape its try/catch (same pattern as CustomShellRegistry).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void LogViaModLog(string message) => ModLog.Log.Error(message);
}
