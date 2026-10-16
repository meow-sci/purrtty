using purrTTY.Core.Terminal;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Launcher;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld;
using purrTTY.Logging;

namespace purrTTY.GameMod.Launcher;

/// <summary>
/// A handle to one in-world (render-to-texture) terminal. The instance is addressed through the
/// <see cref="InWorldTerminalManager"/>; its session is picked up by polling (it starts on a pool
/// thread). The handle closes when the instance leaves the manager — through
/// <see cref="CloseAsync"/>, purrTTY's own "Destroy" button, or a GPU failure that retires it.
/// </summary>
internal sealed class InWorldTerminalHandle : TerminalHandle
{
    private readonly InWorldTerminalManager _manager;
    private readonly InWorldTerminalInstance _instance;
    private readonly ThemeCatalog _themes;
    private bool _startFailureReported;

    public InWorldTerminalHandle(
        TerminalLauncher launcher,
        MainThreadPump pump,
        InWorldTerminalManager manager,
        InWorldTerminalInstance instance,
        ThemeCatalog themes)
        : base(launcher, pump)
    {
        _manager = manager;
        _instance = instance;
        _themes = themes;

        // Raised from inside the coordinator's per-frame update: queue the real work.
        _instance.FocusChanged += OnInstanceFocusChanged;
    }

    public override string Name => _instance.Name;

    // The anchor mode is read live from the record and never changed by the launcher.
    public override TerminalPlacementKind Placement
        => _instance.IsBillboard ? TerminalPlacementKind.InWorldBillboard : TerminalPlacementKind.InWorldPart;

    protected override bool IsGridReady => _instance.Content?.GridInitialized ?? false;

    protected override void TickCore()
    {
        // Removal by purrTTY's own UI (Destroy), or the coordinator pruning a failed instance, has no
        // event: the instance simply leaves the manager's list.
        if (!ContainsInstance())
        {
            CloseState();
            return;
        }

        if (Session is null && _instance.Content is { } content)
        {
            if (content.ActiveSession is { } session)
            {
                BindSession(session);
            }
            else if (content.SessionStartError is { } error && !_startFailureReported)
            {
                _startFailureReported = true;
                ModLog.Log.Error($"purrTTY launcher: in-world terminal '{Name}' failed to start: {error}");
                MarkExited(null);
            }
        }
    }

    protected override void FocusCore() => _manager.Focus(_instance);

    protected override void CloseCore() => _manager.Remove(_instance);

    protected override bool RenameCore(string name) => _instance.TryRename(name);

    protected override bool ResizeCore(int cols, int rows) => _instance.TrySetGridSize(cols, rows);

    protected override void SetPlacementCore(TerminalInWorldOptions options)
    {
        // The anchor mode is fixed at launch (the Billboard flag is ignored here): map with the
        // handle's current kind and write that mode's record fields. The quad reads the record every
        // frame, so the change is visible immediately; changing anchor ids drops its cached part.
        var placement = TerminalLaunchMapper.ToInWorldPlacement(options, Placement, Name);
        InWorldRecordMapper.ApplyPlacement(_instance.Record, placement, billboard: _instance.IsBillboard);

        if (placement.HasOpacityOverride)
        {
            _instance.SetOpacities(
                placement.BackgroundOpacity ?? _instance.BackgroundOpacity,
                placement.ForegroundOpacity ?? _instance.ForegroundOpacity,
                placement.CellBackgroundOpacity ?? _instance.CellBackgroundOpacity);
        }
    }

    protected override void ApplyThemeCore(string themeName)
    {
        var theme = _themes.Find(themeName) ?? throw new KeyNotFoundException($"No theme named '{themeName}'.");
        _instance.ApplyTheme(theme);
    }

    protected override void DetachCore() => _instance.FocusChanged -= OnInstanceFocusChanged;

    private void OnInstanceFocusChanged(InWorldTerminalInstance instance, bool focused)
        => Pump.Post(() =>
        {
            if (IsOpenOnMain)
            {
                ObserveFocus(_instance.HasFocus);
            }
        });

    private bool ContainsInstance()
    {
        var instances = _manager.Instances;
        for (int i = 0; i < instances.Count; i++)
        {
            if (ReferenceEquals(instances[i], _instance))
            {
                return true;
            }
        }

        return false;
    }
}
