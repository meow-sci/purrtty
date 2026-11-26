using KSA.Kfs.Host;
using purrTTY.Kfs.Runtime;

// The folder is Console/, the namespace is not purrTTY.Kfs.Console: a namespace of that name
// would shadow System.Console in every file under purrTTY.Kfs (and purrTTY.Kfs.Tests).
namespace purrTTY.Kfs.Commands;

/// <summary>
/// What the <c>purr</c> console command needs from the game (plan §4.13): the console it is
/// registered with, where its output goes, the controlled vehicle, and the kFS workspace. Supplied
/// by the game services (<see cref="IKfsGameServices.Console"/>; in the game
/// <see cref="Game.KsaConsoleHost"/>), so offline tests supply their own and never touch the
/// game's console, vehicles or Documents folder.
/// </summary>
/// <remarks>Every member is called on the main thread: at install and uninstall, or from a console
/// command or a menu item during the UI phase.</remarks>
public interface IKfsConsoleHost
{
    /// <summary>
    /// The console the command is registered with (the game: <c>Program.TerminalInterface</c>):
    /// <see cref="PurrConsole"/> uses it to recognise a console that still holds the command after
    /// an uninstall that could not remove it, so a re-install does not register it a second time.
    /// </summary>
    object ConsoleIdentity { get; }

    /// <summary>Registers <paramref name="command"/> (<see cref="PurrConsole.Execute"/>, carrying its <c>[TerminalAction]</c>) as the <c>purr</c> command. Throws when the console cannot take it.</summary>
    void RegisterCommand(Action<string> command);

    /// <summary>Removes the command again. False when this console cannot remove commands (the command then stays registered and answers that the extension is not active).</summary>
    bool UnregisterCommand();

    /// <summary>Writes one line where <c>kfs</c> output goes (the game: <c>KfsIntegration.Print</c>).</summary>
    void Print(string line);

    /// <summary>The controlled vehicle's parts as <c>purr parts</c> lists them (<see cref="ControlledParts.For"/>), or null when no vehicle is controlled. Reads only.</summary>
    ControlledParts? ControlledVehicle();

    /// <summary>
    /// Restores the stock appearance of every light a program overrode, on every vehicle (the game:
    /// <c>LightOverrides.RestoreAll</c>); returns how many. <c>purr lights reset all</c>.
    /// </summary>
    int ResetAllLightOverrides();

    /// <summary>The kFS workspace folder, for messages.</summary>
    string WorkspaceFolder { get; }

    /// <summary>True when the workspace-relative <paramref name="path"/> (<c>x_blink/blink.c</c>) exists.</summary>
    bool WorkspaceFileExists(string path);

    /// <summary>Writes a workspace-relative file, creating its folder (the game: the public <c>Workspace.Write</c>).</summary>
    void WriteWorkspaceFile(string path, string text);
}

/// <summary>Where the part lists of a <see cref="ControlledParts"/> come from.</summary>
public enum PartsListingSource
{
    /// <summary>The vehicle's kFS computer is bound and runs a program: the lists that program addresses.</summary>
    RunningProgram,
    /// <summary>The vehicle's kFS computer is bound but runs nothing: the lists it holds; the next run rebuilds them.</summary>
    Computer,
    /// <summary>No bound computer: lists built from the vehicle now, as a program started now would get them.</summary>
    Preview,
}

/// <summary>The controlled vehicle's parts for <c>purr parts</c>.</summary>
/// <param name="VehicleName">The vehicle's name (the game: <c>Vehicle.Id</c>).</param>
/// <param name="Parts">The back end to list.</param>
/// <param name="Source">Whose lists these are.</param>
public sealed record ControlledParts(string VehicleName, IVehicleParts Parts, PartsListingSource Source)
{
    /// <summary>
    /// The one resolution rule, in the game and offline alike: the binding of
    /// <paramref name="computer"/> when it has one (its frozen lists), otherwise the back end
    /// <paramref name="preview"/> builds. Reads only: never binds, never creates a host.
    /// </summary>
    /// <param name="vehicleName">The vehicle's name.</param>
    /// <param name="computer">The vehicle's kFS computer, or null when it has none (the game: no <c>KfsVehicleHost</c>).</param>
    /// <param name="preview">Builds a fresh back end over the vehicle.</param>
    public static ControlledParts For(string vehicleName, VehicleComputer? computer, Func<IVehicleParts> preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (computer is not null && PartsRegistry.TryGet(computer, out VehicleBinding? binding) && binding.IsBound)
        {
            return new ControlledParts(vehicleName ?? "", binding.Parts,
                computer.Grid is not null ? PartsListingSource.RunningProgram : PartsListingSource.Computer);
        }
        return new ControlledParts(vehicleName ?? "", preview(), PartsListingSource.Preview);
    }
}
