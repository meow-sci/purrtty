using purrTTY.Display.Ghostty;
using purrTTY.Display.Layouts;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.Layouts;

/// <summary>Outcome of applying a layout: how many terminals were created and which were skipped (with reasons).</summary>
public sealed record LayoutApplyResult(int Created, IReadOnlyList<(string Name, string Reason)> Skipped);

/// <summary>
/// Orchestrates saved <b>sets</b> of terminals on top of the two coordinators — the 2D
/// <see cref="GhosttyTerminalController"/> and the <see cref="InWorldTerminalManager"/>.
/// Applies a layout (creating every terminal, skipping name collisions), tracks which
/// live terminals each loaded set owns, and tears a whole set down again. Every action
/// is user-initiated; nothing is ever applied automatically.
///
/// Must be driven on the main/GUI thread: in-world <see cref="InWorldTerminalManager.Create"/>
/// needs an active ImGui frame to allocate its off-screen target.
/// </summary>
public sealed class LayoutManager
{
    private readonly GhosttyTerminalController _controller;
    private readonly InWorldTerminalManager? _inWorld;
    private readonly ThemeCatalog _themes;
    private readonly LayoutCatalog _catalog = new();

    // Which still-live terminals each applied layout created, so a set can be torn down
    // as a unit. Concrete objects (not just INamedTerminal) so teardown dispatches by type.
    private readonly Dictionary<string, List<INamedTerminal>> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public LayoutManager(GhosttyTerminalController controller, InWorldTerminalManager? inWorld, ThemeCatalog themes)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _inWorld = inWorld;
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
    }

    public LayoutCatalog Catalog => _catalog;

    /// <summary>The layouts currently applied (and still tracking ≥1 live terminal).</summary>
    public IReadOnlyCollection<string> LoadedLayouts => _loaded.Keys;

    public bool IsLoaded(string layoutName) => _loaded.ContainsKey(layoutName);

    /// <summary>
    /// Creates every terminal in the named layout. A terminal whose name collides with a
    /// live terminal is logged and skipped (the rest still load). Created terminals are
    /// tracked so the set can be torn down later.
    /// </summary>
    public LayoutApplyResult Apply(string layoutName)
    {
        var skipped = new List<(string Name, string Reason)>();
        var layout = _catalog.Load(layoutName);
        if (layout is null)
        {
            ModLog.Log.Error($"Layout '{layoutName}' could not be loaded.");
            return new LayoutApplyResult(0, skipped);
        }

        var created = new List<INamedTerminal>();
        bool anyWindow = false;

        foreach (var entry in layout.Terminals)
        {
            string name = entry.Name?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                skipped.Add(("(unnamed)", "blank terminal name"));
                continue;
            }

            // Name-collision policy: log + skip; never rename or replace a live terminal.
            if (!TerminalTargetRegistry.IsNameAvailable(name))
            {
                ModLog.Log.Error($"Layout '{layoutName}': terminal name '{name}' collides with a live terminal — skipped.");
                skipped.Add((name, "name already in use by a live terminal"));
                continue;
            }

            var theme = _themes.Find(entry.Theme);

            if (entry.Kind == TerminalKind.InWorld)
            {
                if (_inWorld is null)
                {
                    skipped.Add((name, "in-world subsystem unavailable"));
                    continue;
                }

                var instance = _inWorld.Create(ToInWorldRecord(entry, name));
                if (instance is null)
                {
                    skipped.Add((name, "in-world create failed (see log)"));
                    continue;
                }

                created.Add(instance);
            }
            else
            {
                created.Add(_controller.CreateConfiguredWindow(ToWindowRecord(entry, name), theme));
                anyWindow = true;
            }
        }

        if (anyWindow)
        {
            _controller.IsVisible = true;
        }

        if (created.Count > 0)
        {
            if (_loaded.TryGetValue(layoutName, out var existing))
            {
                existing.AddRange(created);
            }
            else
            {
                _loaded[layoutName] = created;
            }
        }

        return new LayoutApplyResult(created.Count, skipped);
    }

    /// <summary>
    /// Tears down every terminal a previously-applied layout created and is still live.
    /// Terminals the user already closed/destroyed are skipped (no double-teardown).
    /// In-world instances go through the manager's safe deferred GPU teardown; 2D windows
    /// are closed immediately. Returns how many terminals were actually torn down.
    /// </summary>
    public int TeardownSet(string layoutName)
    {
        if (!_loaded.TryGetValue(layoutName, out var terminals))
        {
            return 0;
        }

        int removed = 0;
        foreach (var terminal in terminals)
        {
            // Skip anything the user already closed (it unregistered itself).
            if (!TerminalTargetRegistry.All.Contains(terminal))
            {
                continue;
            }

            switch (terminal)
            {
                case TerminalWindow window:
                    _controller.CloseWindow(window);
                    removed++;
                    break;
                case InWorldTerminalInstance instance:
                    _inWorld?.Remove(instance);
                    removed++;
                    break;
            }
        }

        _loaded.Remove(layoutName);
        return removed;
    }

    /// <summary>Tears down every currently-loaded layout set.</summary>
    public void TeardownAllLoaded()
    {
        foreach (var name in _loaded.Keys.ToList())
        {
            TeardownSet(name);
        }
    }

    /// <summary>
    /// Captures the currently-live terminals (in-world instances + 2D windows) as a named
    /// layout and saves it. Appearance is captured by <b>theme name</b>; custom colours/
    /// opacities persist only if first saved as a named theme (consistent with the rest of
    /// the app). Overwrites any existing layout of the same name.
    /// </summary>
    public void CaptureCurrentAs(string name, string? description = null)
    {
        var layout = new TerminalLayout
        {
            Header = new LayoutHeader
            {
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description!.Trim(),
            },
        };

        if (_inWorld is not null)
        {
            foreach (var instance in _inWorld.Instances)
            {
                layout.Terminals.Add(FromInWorld(instance.Record));
            }
        }

        foreach (var window in _controller.CaptureWindows())
        {
            layout.Terminals.Add(FromWindow(window));
        }

        _catalog.Save(layout);
    }

    private static TerminalEntry FromInWorld(InWorldTerminalRecord r) => new()
    {
        Name = r.Name,
        Kind = TerminalKind.InWorld,
        Theme = r.ThemeName,
        Shell = ShellSpec.From(r.Launch),
        Cols = r.Cols,
        Rows = r.Rows,
        Mode = r.Mode,
        VehicleId = r.TargetVehicleId,
        PartId = r.TargetPartId,
        SubPartId = r.TargetSubPartId,
        OffsetX = r.PartOffsetX,
        OffsetY = r.PartOffsetY,
        OffsetZ = r.PartOffsetZ,
        RotationX = r.PartRotationX,
        RotationY = r.PartRotationY,
        RotationZ = r.PartRotationZ,
        WidthMeters = r.PartWidthMeters,
        HeightMeters = r.PartHeightMeters,
        BillboardDistance = r.BillboardDistance,
        BillboardOffsetX = r.BillboardOffsetX,
        BillboardOffsetY = r.BillboardOffsetY,
        BillboardWidthMeters = r.BillboardWidthMeters,
        BillboardHeightMeters = r.BillboardHeightMeters,
        BillboardAlwaysOnTop = r.BillboardAlwaysOnTop,
    };

    private static TerminalEntry FromWindow(WindowLayoutRecord w) => new()
    {
        Name = w.Name,
        Kind = TerminalKind.Window,
        Theme = w.ThemeName,
        Shell = ShellSpec.From(w.Launch),
        PosX = w.Position?.X,
        PosY = w.Position?.Y,
        Width = w.Size?.X,
        Height = w.Size?.Y,
    };

    private static InWorldTerminalRecord ToInWorldRecord(TerminalEntry e, string name) => new()
    {
        Name = name,
        Cols = e.Cols ?? 100,
        Rows = e.Rows ?? 30,
        ThemeName = e.Theme,
        Launch = e.Shell.ToLaunchOptions(),
        Mode = string.Equals(e.Mode, InWorldTerminalRecord.ModeBillboard, StringComparison.OrdinalIgnoreCase)
            ? InWorldTerminalRecord.ModeBillboard
            : InWorldTerminalRecord.ModePart,
        TargetVehicleId = e.VehicleId ?? string.Empty,
        TargetPartId = e.PartId ?? string.Empty,
        TargetSubPartId = e.SubPartId ?? string.Empty,
        PartOffsetX = e.OffsetX ?? 0f,
        PartOffsetY = e.OffsetY ?? 0f,
        PartOffsetZ = e.OffsetZ ?? 2f,
        PartRotationX = e.RotationX ?? 0f,
        PartRotationY = e.RotationY ?? 0f,
        PartRotationZ = e.RotationZ ?? 0f,
        PartWidthMeters = e.WidthMeters ?? 2f,
        PartHeightMeters = e.HeightMeters ?? 2f,
        BillboardDistance = e.BillboardDistance ?? 5f,
        BillboardOffsetX = e.BillboardOffsetX ?? 0f,
        BillboardOffsetY = e.BillboardOffsetY ?? 0f,
        BillboardWidthMeters = e.BillboardWidthMeters ?? 3f,
        BillboardHeightMeters = e.BillboardHeightMeters ?? 2f,
        BillboardAlwaysOnTop = e.BillboardAlwaysOnTop ?? true,
    };

    private static WindowLayoutRecord ToWindowRecord(TerminalEntry e, string name) => new()
    {
        Name = name,
        Position = e.PosX is { } px && e.PosY is { } py ? new float2(px, py) : null,
        Size = e.Width is { } w && e.Height is { } h ? new float2(w, h) : null,
        ThemeName = e.Theme,
        Launch = e.Shell.ToLaunchOptions(),
    };
}
