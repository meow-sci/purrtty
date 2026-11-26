namespace purrTTY.Kfs;

/// <summary>
/// The game-only part of an install: what exists only inside the running game and so cannot exist
/// in the offline test runtime — in the game, host discovery (<c>KfsVehicleHost.AllHosts</c>), the
/// per-frame main-thread pump that binds and drains every host, the dispatch-thread fallback that
/// binds a host the pump has not seen yet, and restoring private light templates at uninstall
/// (<see cref="Game.KsaGameServices"/>). <c>purrTTY.GameMod</c> passes one to
/// <see cref="KfsExtension.Install(HarmonyLib.Harmony, KfsExtensionOptions?, IKfsGameServices?)"/>; the test runtime
/// passes none and binds its simulated vehicles itself.
/// </summary>
/// <remarks>
/// It takes part in the installer's all-or-nothing rule: <see cref="Prepare"/> runs with the
/// required patch targets, before anything changes, and a reason refuses the install;
/// <see cref="Attach"/> runs as the last step before bindings may be created, and an exception
/// there undoes the whole install; <see cref="Detach"/> runs first at uninstall and whenever an
/// install that attached is undone.
/// </remarks>
public interface IKfsGameServices
{
    /// <summary>A short name for the status line (<c>game back end</c>).</summary>
    string Name { get; }

    /// <summary>Resolves what the services need without changing anything; null when ready, else the reason in words.</summary>
    string? Prepare();

    /// <summary>Starts the services (main thread). May throw: the install is then undone.</summary>
    void Attach();

    /// <summary>Stops the services and undoes their effects (main thread). Never throws; idempotent.</summary>
    void Detach();

    /// <summary>
    /// The game's console, vehicles and workspace for the <c>purr</c> command (install step 9;
    /// in the game <see cref="Game.KsaConsoleHost"/>), or null for none (the default). Unlike the
    /// rest of the services it is not part of the all-or-nothing rule: a failure to register the
    /// command is reported in the status and the install stays active.
    /// </summary>
    Commands.IKfsConsoleHost? Console => null;

    /// <summary>
    /// Why the game itself may be reading <c>SystemHeaders</c> off the main thread now, or null when it
    /// is not: in the game, the stock kFS editor's background build (<c>Workbench.Editor.Building</c>,
    /// read only when the workbench already exists — it is never created here; when the probe cannot
    /// read it, the answer is "may be building", see <see cref="Tables.StockEditorProbe"/>). The uninstall
    /// leaves <c>&lt;purrtty.h&gt;</c> registered while it is. Main thread; never throws. Offline fakes override it.
    /// </summary>
    string? SystemHeadersInUse() => Tables.StockEditorProbe.Building();
}
