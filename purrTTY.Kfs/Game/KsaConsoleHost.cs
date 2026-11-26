using Brutal.ImGuiApi.Abstractions;
using KSA;
using KSA.Kfs;
using purrTTY.Kfs.Commands;

namespace purrTTY.Kfs.Game;

/// <summary>
/// The game's side of the <c>purr</c> command (<see cref="IKfsConsoleHost"/>): the game console
/// (<c>Program.TerminalInterface</c>), <c>kfs</c>'s output (<c>KfsIntegration.Print</c>), the
/// controlled vehicle (<c>Program.ControlledVehicle</c> and its <c>KfsVehicleHost</c>) and the kFS
/// workspace (<c>KfsIntegration.Workbench.Workspace</c>). Main thread only.
/// </summary>
/// <remarks>
/// <para><b>Registration.</b> <c>TerminalInterface.RegisterCommand(delegate, owner)</c> files the
/// command under <paramref name="owner"/>; registering the same name under the same owner replaces
/// the entry, and <c>UnregisterObject(owner)</c> removes every command of that owner — so the
/// command is filed under one process-wide owner object of its own (<see cref="Owner"/>), a
/// re-registration can never add a second <c>purr</c>, and uninstall removes exactly it. (Two
/// owners holding the same name would break the game's <c>help</c>, which collects every name into
/// one dictionary.)</para>
/// <para><b>Reads only</b> for <c>purr parts</c>: <c>KfsVehicleHost.Find</c> (never <c>For</c>,
/// which would create a host), the registry lookup, and for a vehicle without a bound computer a
/// fresh <see cref="KsaVehicleParts"/>, whose list building only reads modules. The
/// workspace is touched only by <c>purr examples</c>: <c>KfsIntegration.Workbench</c> is created on
/// first use and its workspace folder (under the player's Documents) on the first write.</para>
/// </remarks>
public sealed class KsaConsoleHost : IKfsConsoleHost
{
    /// <summary>The <see cref="AppDomain"/> data key under which <see cref="Owner"/> is kept.</summary>
    internal const string OwnerDataKey = "purrTTY.Kfs.PurrCommandOwner";

    /// <summary>
    /// The owner the command is filed under in the game's console: one per process, kept in
    /// <see cref="AppDomain.CurrentDomain"/>'s data, so a second load of this assembly (a fresh load
    /// context after an unload that never ran) reuses it — registering the same name under the same
    /// owner replaces the entry, where a second owner would make the game's <c>help</c> throw.
    /// </summary>
    internal static readonly object Owner = SharedOwner(OwnerDataKey, static () => new CommandOwner());

    /// <summary>The object stored under <paramref name="key"/> in the process's data, created by <paramref name="create"/> and stored first when there is none.</summary>
    internal static object SharedOwner(string key, Func<object> create)
    {
        AppDomain domain = AppDomain.CurrentDomain;
        lock (domain)   // the one AppDomain object is shared by every load context
        {
            if (domain.GetData(key) is { } existing) return existing;
            object owner = create();
            domain.SetData(key, owner);
            return owner;
        }
    }

    /// <inheritdoc/>
    /// <remarks>The game's <c>TerminalInterface</c> (<c>Program.ConsoleWindow.Terminal</c>); throws while the console window does not exist yet.</remarks>
    public object ConsoleIdentity => Terminal();

    /// <inheritdoc/>
    public void RegisterCommand(Action<string> command) => Terminal().RegisterCommand(command, Owner);

    /// <inheritdoc/>
    public bool UnregisterCommand()
    {
        Terminal().UnregisterObject(Owner);
        return true;
    }

    /// <inheritdoc/>
    public void Print(string line) => KfsIntegration.Print(line);

    /// <inheritdoc/>
    public ControlledParts? ControlledVehicle()
    {
        Vehicle? vehicle = Program.ControlledVehicle;
        if (vehicle is null || vehicle.IsDisposed) return null;
        return ControlledParts.For(vehicle.Id ?? "", KfsVehicleHost.Find(vehicle)?.Computer, () => new KsaVehicleParts(vehicle));
    }

    /// <inheritdoc/>
    /// <remarks>A template reference write per light; the renderer reads templates on this (the main) thread.</remarks>
    public int ResetAllLightOverrides() => LightOverrides.RestoreAll();

    /// <inheritdoc/>
    public string WorkspaceFolder => KfsIntegration.Workbench.Workspace.Folder;

    /// <inheritdoc/>
    public bool WorkspaceFileExists(string path) => KfsIntegration.Workbench.Workspace.Exists(path);

    /// <inheritdoc/>
    public void WriteWorkspaceFile(string path, string text) => KfsIntegration.Workbench.Workspace.Write(path, text);

    private static TerminalInterface Terminal() =>
        (Program.ConsoleWindow is null ? null : Program.TerminalInterface)
        ?? throw new InvalidOperationException("the game console does not exist yet");

    private sealed class CommandOwner
    {
        public override string ToString() => "purrTTY kFS extension (purr command)";
    }
}
