using System.Collections.ObjectModel;
using Brutal.ImGuiApi;
using purrTTY.Core.Terminal;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Launcher;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.Launcher;

/// <summary>
/// purrTTY's implementation of the cross-mod <see cref="ITerminalLauncher"/> contract. It opens 2D
/// windows, tabs and in-world terminals on behalf of another mod and hands back
/// <see cref="ITerminalHandle"/>s.
/// <para>
/// Every real operation runs on the game's main thread inside the ImGui frame: callers (any thread)
/// enqueue work on the <see cref="MainThreadPump"/>, which <c>TerminalMod.OnAfterUi</c> drains at the
/// very top of the frame, before the 2D controller renders and the in-world coordinator updates.
/// <see cref="Tick"/> runs right after the drain to poll the things that have no event (session
/// binding, in-world removal, grid size). Lifecycle events are raised from there, on the main thread.
/// </para>
/// </summary>
internal sealed class TerminalLauncher : ITerminalLauncher, IDisposable
{
    // The geometry a window gets when a request fixes only one of position / size and no window is open
    // to cascade from. Matches TerminalWindow's own first-use defaults.
    private static readonly float2 DefaultWindowPosition = new(80f, 80f);
    private static readonly float2 DefaultWindowSize = new(880f, 520f);

    // Same cascade step as GhosttyTerminalController.OpenWindow.
    private static readonly float2 CascadeOffset = new(40f, 40f);

    private readonly GhosttyTerminalController _controller;
    private readonly MainThreadPump _pump;
    private readonly object _gate = new();

    private InWorldTerminalManager? _inWorld; // main thread
    private TerminalHandle[] _live = Array.Empty<TerminalHandle>(); // writes under _gate
    private IReadOnlyList<ITerminalHandle> _published = Array.Empty<ITerminalHandle>();
    private volatile bool _disposed;

    public TerminalLauncher(GhosttyTerminalController controller, MainThreadPump pump)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
    }

    /// <inheritdoc/>
    public bool IsAvailable => !_disposed;

    /// <inheritdoc/>
    public IReadOnlyList<ITerminalHandle> Terminals => Volatile.Read(ref _published);

    /// <inheritdoc/>
    public event Action<ITerminalHandle, TerminalLifecycleEvent>? Lifecycle;

    /// <summary>
    /// Binds (or, with null, unbinds) the in-world coordinator. Called on the main thread from
    /// <c>TryInitializeInWorld</c>; in-world requests fault until it is attached.
    /// </summary>
    public void AttachInWorld(InWorldTerminalManager? inWorld) => _inWorld = inWorld;

    /// <inheritdoc/>
    public ITerminalHandle? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (ITerminalHandle handle in Volatile.Read(ref _published))
        {
            if (string.Equals(handle.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return handle;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public Task<ITerminalHandle> LaunchAsync(TerminalLaunchRequest request, CancellationToken ct = default)
    {
        if (request is null)
        {
            return Task.FromException<ITerminalHandle>(new ArgumentNullException(nameof(request)));
        }

        if (_disposed)
        {
            return Task.FromException<ITerminalHandle>(
                new InvalidOperationException("The purrTTY terminal launcher is shut down."));
        }

        return _pump.RunAsync(() => LaunchOnMainThread(request), ct);
    }

    /// <summary>
    /// Per-frame poll of every open handle (called right after the pump drain, on the main thread).
    /// </summary>
    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        var handles = Volatile.Read(ref _live);
        if (handles.Length == 0)
        {
            return;
        }

        // Pumping, so a Lifecycle subscriber that launches or controls a terminal runs inline.
        using var scope = _pump.EnterPumpScope();
        for (int i = 0; i < handles.Length; i++)
        {
            try
            {
                handles[i].Tick();
            }
            catch (Exception ex)
            {
                ModLog.Log.Error($"purrTTY launcher: polling terminal '{handles[i].Name}' threw: {ex}");
            }
        }
    }

    /// <summary>
    /// Shuts the launcher down: it stops being available, every handle is closed without raising
    /// events, and queued requests are cancelled. Idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        TerminalHandle[] handles;
        lock (_gate)
        {
            handles = _live;
            _live = Array.Empty<TerminalHandle>();
            _published = Array.Empty<ITerminalHandle>();
        }

        foreach (var handle in handles)
        {
            handle.Shutdown();
        }

        _pump.Shutdown();
    }

    // ---- handle bookkeeping ----

    internal void Unregister(TerminalHandle handle)
    {
        lock (_gate)
        {
            int index = Array.IndexOf(_live, handle);
            if (index < 0)
            {
                return;
            }

            _live = _live.Where(h => !ReferenceEquals(h, handle)).ToArray();
            _published = Publish(_live);
        }
    }

    /// <summary>True when another open handle already uses <paramref name="name"/> (case-insensitive).</summary>
    internal bool IsNameInUse(string name, TerminalHandle? except)
    {
        foreach (var handle in Volatile.Read(ref _live))
        {
            if (!ReferenceEquals(handle, except) && string.Equals(handle.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void Register(TerminalHandle handle)
    {
        lock (_gate)
        {
            _live = [.. _live, handle];
            _published = Publish(_live);
        }

        handle.Initialize();
    }

    private static IReadOnlyList<ITerminalHandle> Publish(TerminalHandle[] live)
        => new ReadOnlyCollection<ITerminalHandle>(Array.ConvertAll(live, h => (ITerminalHandle)h));

    // ---- events ----

    /// <summary>Raises the handle's own event first, then the launcher-wide one; each subscriber is isolated.</summary>
    internal void Raise(TerminalHandle handle, TerminalLifecycleEvent lifecycleEvent)
    {
        handle.InvokeLifecycle(lifecycleEvent);
        InvokeIsolated(Lifecycle, handle, lifecycleEvent, handle.Name);
    }

    internal static void InvokeIsolated(
        Action<ITerminalHandle, TerminalLifecycleEvent>? subscribers,
        ITerminalHandle handle,
        TerminalLifecycleEvent lifecycleEvent,
        string terminalName)
    {
        if (subscribers is null)
        {
            return;
        }

        foreach (Delegate subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<ITerminalHandle, TerminalLifecycleEvent>)subscriber)(handle, lifecycleEvent);
            }
            catch (Exception ex)
            {
                ModLog.Log.Error($"purrTTY launcher: a Lifecycle subscriber threw on {lifecycleEvent} for '{terminalName}': {ex}");
            }
        }
    }

    // ---- launching (main thread, ImGui frame active) ----

    private ITerminalHandle LaunchOnMainThread(TerminalLaunchRequest request)
    {
        if (_disposed)
        {
            throw new InvalidOperationException("The purrTTY terminal launcher is shut down.");
        }

        request.Program ??= new TerminalProgramSpec();

        return request.Placement switch
        {
            TerminalPlacementKind.Window => LaunchWindow(request),
            TerminalPlacementKind.Tab => LaunchTab(request),
            TerminalPlacementKind.InWorldPart or TerminalPlacementKind.InWorldBillboard => LaunchInWorld(request),
            _ => throw new ArgumentException($"Unknown placement {request.Placement}.", nameof(request)),
        };
    }

    private ITerminalHandle LaunchWindow(TerminalLaunchRequest request)
    {
        var options = request.Window;
        var launch = TerminalLaunchMapper.ToLaunchOptions(request.Program, _controller.Configuration.CreateLaunchOptions);
        var theme = ResolveTheme(options?.ThemeName);

        // Cols x rows -> pixels, from the theme's font cell and the title strip height. Skipped when an
        // explicit pixel size wins anyway.
        (float W, float H)? gridSize = null;
        if (options is { Cols: { } cols, Rows: { } rows } && !(options.Width.HasValue && options.Height.HasValue))
        {
            var (cellWidth, cellHeight) = InWorldTerminalRenderer.MeasureCell(
                _controller.Configuration, _controller.Catalog, theme?.Name ?? options.ThemeName);
            gridSize = TerminalLaunchMapper.WindowPixelSize(
                cols, rows, cellWidth, cellHeight, ImGui.GetFrameHeight(), TerminalWindow.ContentPadding);
        }

        var record = TerminalLaunchMapper.ToWindowRecord(options, launch, "Terminal", gridSize);
        CompleteWindowGeometry(record);

        bool focus = options?.Focus ?? true;

        // The window must exist BEFORE the terminal UI is made visible: IsVisible = true with no open
        // window spawns a default-shell window (gotcha: IsVisible ordering).
        var window = _controller.CreateConfiguredWindow(record, theme, options?.Title, requestFocus: focus);
        if (options?.LockMode is { } lockMode)
        {
            window.Settings.LockMode = lockMode;
        }

        if (options?.HideChrome is { } hideChrome)
        {
            window.Settings.HideChromeWhenNotHovered = hideChrome;
        }

        if (options?.NoResize is { } noResize)
        {
            window.Settings.NoResize = noResize;
        }

        if (!focus)
        {
            window.Settings.NoFocusOnAppearing = true;
        }

        if (options?.ShowTerminal ?? true)
        {
            _controller.IsVisible = true;
        }

        var handle = new WindowTerminalHandle(this, _pump, _controller, window, record.Position ?? DefaultWindowPosition);
        Register(handle);
        return handle;
    }

    private ITerminalHandle LaunchTab(TerminalLaunchRequest request)
    {
        var target = _controller.FocusTarget;
        if (target is null)
        {
            // Nothing to add a tab to: open a window instead (documented; the handle reports Window).
            return LaunchWindow(request);
        }

        var options = request.Window;
        var launch = TerminalLaunchMapper.ToLaunchOptions(request.Program, _controller.Configuration.CreateLaunchOptions);

        var pending = _controller.OpenTabIn(target, launch, options?.Title, requestFocus: options?.Focus ?? true);
        if (options?.ShowTerminal ?? true)
        {
            _controller.IsVisible = true;
        }

        var handle = new TabTerminalHandle(this, _pump, target, UniqueTabName(options?.Name, target), pending);
        Register(handle);
        return handle;
    }

    private ITerminalHandle LaunchInWorld(TerminalLaunchRequest request)
    {
        var manager = _inWorld ?? throw new InvalidOperationException("in-world terminals are unavailable");

        var options = request.InWorld ?? new TerminalInWorldOptions();
        var launch = TerminalLaunchMapper.ToLaunchOptions(request.Program, _controller.Configuration.CreateLaunchOptions);
        var placement = TerminalLaunchMapper.ToInWorldPlacement(options, request.Placement, "In-World");

        if (!string.IsNullOrWhiteSpace(placement.ThemeName) && _controller.Catalog.Find(placement.ThemeName) is null)
        {
            ModLog.Log.Warning($"purrTTY launcher: unknown theme '{placement.ThemeName}' for an in-world terminal; using the default theme.");
            placement = placement with { ThemeName = null };
        }

        var instance = manager.Create(InWorldRecordMapper.ToRecord(placement, launch))
            ?? throw new InvalidOperationException(
                "purrTTY could not create the in-world terminal (see the game log for the cause).");

        if (placement.HasOpacityOverride)
        {
            // Missing values keep what the theme gave the instance.
            instance.SetOpacities(
                placement.BackgroundOpacity ?? instance.BackgroundOpacity,
                placement.ForegroundOpacity ?? instance.ForegroundOpacity,
                placement.CellBackgroundOpacity ?? instance.CellBackgroundOpacity);
        }

        if (placement.Focus)
        {
            manager.Focus(instance);
        }

        var handle = new InWorldTerminalHandle(this, _pump, manager, instance, _controller.Catalog);
        Register(handle);
        return handle;
    }

    // ---- helpers ----

    private ThemeDefinition? ResolveTheme(string? themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
        {
            return null;
        }

        var theme = _controller.Catalog.Find(themeName);
        if (theme is null)
        {
            ModLog.Log.Warning($"purrTTY launcher: unknown theme '{themeName}'; using the default theme.");
        }

        return theme;
    }

    /// <summary>
    /// A window is created with position and size together or not at all, so make them a pair: copy the
    /// cascade used by the menus (the focused window's geometry, offset) when neither was requested, and
    /// fill the missing half from the same reference otherwise.
    /// </summary>
    private void CompleteWindowGeometry(WindowLayoutRecord record)
    {
        var reference = _controller.FocusTarget is { HasObservedGeometry: true } focus ? focus : null;

        if (record.Position is null && record.Size is null)
        {
            if (reference is not null)
            {
                record.Position = reference.LastKnownPosition + CascadeOffset;
                record.Size = reference.LastKnownSize;
            }

            return;
        }

        record.Position ??= reference is not null ? reference.LastKnownPosition + CascadeOffset : DefaultWindowPosition;
        record.Size ??= reference is not null ? reference.LastKnownSize : DefaultWindowSize;
    }

    private string UniqueTabName(string? requested, TerminalWindow window)
    {
        // The tab handle id is not known yet; a short random suffix keeps default names unique.
        string baseName = string.IsNullOrWhiteSpace(requested)
            ? $"{window.Name}#{Guid.NewGuid().ToString("N")[..8]}"
            : requested.Trim();

        string name = baseName;
        for (int i = 2; IsNameInUse(name, except: null) || TerminalTargetRegistry.Resolve(name) is not null; i++)
        {
            name = $"{baseName} {i}";
        }

        return name;
    }
}
