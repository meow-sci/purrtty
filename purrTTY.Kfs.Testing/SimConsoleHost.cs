using purrTTY.Kfs.Commands;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// An offline <see cref="IKfsConsoleHost"/> for the <c>purr</c> command: a fake console that
/// records registrations, the printed lines, a controlled vehicle the test chooses, and an
/// in-memory workspace. Nothing here touches the game's console, vehicles or Documents folder.
/// </summary>
public sealed class SimConsoleHost : IKfsConsoleHost
{
    private readonly List<Action<string>> _registered = [];

    /// <summary>Every line printed, in order.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>The workspace: workspace-relative path → text.</summary>
    public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

    /// <summary>Every <see cref="WriteWorkspaceFile"/> call, in order.</summary>
    public List<string> Writes { get; } = [];

    /// <summary>The commands registered with this console now (a real console would hold at most one <c>purr</c>).</summary>
    public IReadOnlyList<Action<string>> Registered => _registered;

    /// <summary>Calls of <see cref="RegisterCommand"/>, failed ones included.</summary>
    public int RegisterCalls { get; private set; }

    /// <summary>Calls of <see cref="UnregisterCommand"/>.</summary>
    public int UnregisterCalls { get; private set; }

    /// <summary>When set, <see cref="RegisterCommand"/> throws this.</summary>
    public Exception? RegisterFailure { get; set; }

    /// <summary>False for a console that cannot remove commands (<see cref="UnregisterCommand"/> then returns false and keeps it).</summary>
    public bool CanUnregister { get; set; } = true;

    /// <summary>The controlled vehicle, or null for none (see <see cref="Control(SimVehicle, SimVehicleSpec?)"/>).</summary>
    public Func<ControlledParts?>? Vehicle { get; set; }

    /// <summary>Every vehicle's parts <see cref="ResetAllLightOverrides"/> reaches: each vehicle given to <see cref="Control(SimVehicle, SimVehicleSpec?)"/>, plus any a test adds.</summary>
    public List<SimVehicleParts> AllParts { get; } = [];

    /// <summary>Paths <see cref="WriteWorkspaceFile"/> fails for, with an <see cref="IOException"/>.</summary>
    public HashSet<string> FailingPaths { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Makes <paramref name="vehicle"/> the controlled vehicle, resolved as the game resolves it
    /// (<see cref="ControlledParts.For"/> over its computer): its binding's lists while it is bound,
    /// else a preview over fresh simulated parts from <paramref name="previewSpec"/>.
    /// </summary>
    public SimConsoleHost Control(SimVehicle vehicle, SimVehicleSpec? previewSpec = null)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        string name = vehicle.Name;
        if (!AllParts.Contains(vehicle.Parts)) AllParts.Add(vehicle.Parts);
        Vehicle = () => ControlledParts.For(name, vehicle.Computer, () => new SimVehicleParts(previewSpec ?? new SimVehicleSpec()));
        return this;
    }

    /// <summary>Makes a vehicle with no kFS computer the controlled one: a preview over fresh simulated parts.</summary>
    public SimConsoleHost ControlWithoutComputer(string name, SimVehicleSpec spec)
    {
        Vehicle = () => ControlledParts.For(name, null, () => new SimVehicleParts(spec));
        return this;
    }

    /// <inheritdoc/>
    public object ConsoleIdentity => this;

    /// <inheritdoc/>
    public void RegisterCommand(Action<string> command)
    {
        RegisterCalls++;
        if (RegisterFailure is { } failure) throw failure;
        _registered.Add(command);
    }

    /// <inheritdoc/>
    public bool UnregisterCommand()
    {
        UnregisterCalls++;
        if (!CanUnregister) return false;
        _registered.Clear();
        return true;
    }

    /// <summary>Runs <paramref name="args"/> as the console would run <c>purr args</c>: through the registered command (the console's own path).</summary>
    /// <exception cref="InvalidOperationException">No command is registered.</exception>
    public void Type(string args)
    {
        if (_registered.Count == 0) throw new InvalidOperationException("no purr command is registered with this console");
        _registered[^1](args);
    }

    /// <inheritdoc/>
    public void Print(string line) => Lines.Add(line);

    /// <inheritdoc/>
    public ControlledParts? ControlledVehicle() => Vehicle?.Invoke();

    /// <inheritdoc/>
    public int ResetAllLightOverrides()
    {
        int n = 0;
        foreach (SimVehicleParts parts in AllParts)
        {
            foreach (SimLight light in parts.AllLights)
            {
                if (!light.Overridden) continue;
                light.ResetAppearance();
                n++;
            }
        }
        return n;
    }

    /// <inheritdoc/>
    public string WorkspaceFolder => "<sim workspace>";

    /// <inheritdoc/>
    public bool WorkspaceFileExists(string path) => Files.ContainsKey(path);

    /// <inheritdoc/>
    public void WriteWorkspaceFile(string path, string text)
    {
        Writes.Add(path);
        if (FailingPaths.Contains(path)) throw new IOException($"injected failure writing {path}");
        Files[path] = text;
    }
}
