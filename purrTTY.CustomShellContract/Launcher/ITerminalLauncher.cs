namespace purrTTY.Core.Terminal;

/// <summary>
///     Lets another mod open and manage purrTTY terminals from code. Obtain the live instance
///     from <see cref="TerminalLauncherRegistry.Current"/> (null when purrTTY is not loaded or not
///     initialised yet; watch <see cref="TerminalLauncherRegistry.Changed"/> to attach late).
///     <para>
///         All real work runs on the game's main thread inside the frame loop, so
///         <see cref="LaunchAsync"/> only completes while the game is rendering frames and purrTTY
///         is running.
///     </para>
/// </summary>
public interface ITerminalLauncher
{
    /// <summary>True while purrTTY is initialised and able to open terminals.</summary>
    bool IsAvailable { get; }

    /// <summary>
    ///     Opens a terminal. Callable from any thread; the work runs at the start of the next frame
    ///     on the main thread. Faults with <see cref="ArgumentNullException"/> for a null request,
    ///     <see cref="InvalidOperationException"/> when the placement is unavailable or creation
    ///     failed, and is cancelled when <paramref name="ct"/> is cancelled before the work runs.
    /// </summary>
    /// <param name="request">What to open and where.</param>
    /// <param name="ct">Cancels the request while it is still queued.</param>
    /// <returns>A handle to the new terminal.</returns>
    Task<ITerminalHandle> LaunchAsync(TerminalLaunchRequest request, CancellationToken ct = default);

    /// <summary>Snapshot of the open terminals this launcher created. Safe from any thread.</summary>
    IReadOnlyList<ITerminalHandle> Terminals { get; }

    /// <summary>Finds an open terminal created through this launcher by name (case-insensitive); null when none.</summary>
    ITerminalHandle? Find(string name);

    /// <summary>
    ///     Raised on the main thread for every lifecycle transition of every terminal created through
    ///     this launcher, after the handle's own <see cref="ITerminalHandle.Lifecycle"/> event.
    /// </summary>
    event Action<ITerminalHandle, TerminalLifecycleEvent>? Lifecycle;
}
