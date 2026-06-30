using Brutal.Numerics;
using purrTTY.Core.Terminal;
using purrTTY.Display.Configuration;
using purrTTY.Display.Controllers;
using purrTTY.Display.Theming;
using purrTTY.Logging;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Multi-window terminal controller. Manages a set of <see cref="TerminalWindow"/>s
/// (each owning its sessions/tabs and per-window theme), routes the game-menu
/// actions (new tab/window, theme application, font/opacity changes) to the
/// focused window, and persists display defaults plus the first window's
/// geometry through <see cref="ThemeConfiguration"/>.
/// </summary>
public sealed class GhosttyTerminalController : ITerminalController
{
    private readonly ThemeConfiguration _config;
    private readonly ThemeCatalog _catalog;
    private readonly List<TerminalWindow> _windows = new();

    private TerminalWindow? _lastFocusedWindow;
    private int _nextWindowId = 1;
    private bool _isVisible;
    private bool _disposed;

    private double _blinkTimer;
    private bool _cursorOn = true;
    private float _hiddenDrainTimer;

    private static volatile bool _anyTerminalActive;

    /// <summary>
    /// True when a terminal window is visible and focused. Used by the host's
    /// <c>KSA.Program.OnKey</c> Harmony patch to suppress game key handling while
    /// a terminal is capturing input.
    /// </summary>
    public static bool IsAnyTerminalActive => _anyTerminalActive;

    /// <summary>Optional host hook to suppress keyboard input for a frame (e.g. when the toggle hotkey fires).</summary>
    public Func<bool>? KeyboardSuppression { get; set; }

    public GhosttyTerminalController(ThemeConfiguration config, ThemeCatalog catalog)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public ThemeCatalog Catalog => _catalog;
    public ThemeConfiguration Configuration => _config;
    public IReadOnlyList<TerminalWindow> Windows => _windows;

    /// <summary>
    /// The window menu actions act on: the focused window when there is one,
    /// otherwise the most recently focused (or first) open window.
    /// </summary>
    public TerminalWindow? FocusTarget
    {
        get
        {
            // Plain loops (not LINQ): this is read every frame while a purrTTY
            // menu is open, and per menu action.
            TerminalWindow? firstOpen = null;
            for (int i = 0; i < _windows.Count; i++)
            {
                var window = _windows[i];
                if (!window.IsOpen)
                {
                    continue;
                }

                if (window.HasFocus)
                {
                    return window;
                }

                firstOpen ??= window;
            }

            if (_lastFocusedWindow is { IsOpen: true })
            {
                return _lastFocusedWindow;
            }

            return firstOpen;
        }
    }

    /// <summary>Opens a new terminal window and starts a session in it.</summary>
    public TerminalWindow OpenWindow(ProcessLaunchOptions? launchOptions = null, string? sessionTitle = null)
    {
        var sessions = GhosttySessionManagerFactory.CreateSessionManager(_config);
        var settings = CreateWindowSettingsFromDefaults();

        float2? position = null;
        float2? size = null;
        if (_windows.Count == 0 && _config.TryGetTerminalWindowState(out var savedPos, out var savedSize))
        {
            position = savedPos;
            size = savedSize;
        }
        else if (FocusTarget is { HasObservedGeometry: true } reference)
        {
            position = reference.LastKnownPosition + new float2(40f, 40f);
            size = reference.LastKnownSize;
        }

        var window = new TerminalWindow(_nextWindowId++, sessions, settings, position, size);
        window.KeyboardSuppression = () => KeyboardSuppression?.Invoke() ?? false;
        window.FocusChanged += OnWindowFocusChanged;
        window.InputSent += () =>
        {
            // Typing resets the shared blink phase so the cursor is solid while
            // the user types (standard terminal behavior).
            _blinkTimer = 0;
            _cursorOn = true;
        };
        _windows.Add(window);
        _lastFocusedWindow ??= window;

        StartSession(window, launchOptions, sessionTitle);
        window.RequestFocus();
        return window;
    }

    /// <summary>
    /// Creates a window from a saved-layout record: exact name, explicit geometry, the
    /// resolved theme, and the record's shell. The caller (layout manager) does the
    /// registry name-collision pre-check, so the requested name is honored exactly.
    /// </summary>
    public TerminalWindow CreateConfiguredWindow(WindowLayoutRecord spec, ThemeDefinition? theme)
    {
        var sessions = GhosttySessionManagerFactory.CreateSessionManager(_config);
        var settings = CreateWindowSettingsFromDefaults();
        if (theme != null)
        {
            settings.ThemeName = theme.Name;
            settings.Colors = theme.Colors.Clone();
            settings.ApplyThemeOverrides(theme);
        }

        var window = new TerminalWindow(_nextWindowId++, sessions, settings, spec.Position, spec.Size, spec.Name);
        window.KeyboardSuppression = () => KeyboardSuppression?.Invoke() ?? false;
        window.FocusChanged += OnWindowFocusChanged;
        window.InputSent += () =>
        {
            _blinkTimer = 0;
            _cursorOn = true;
        };
        _windows.Add(window);
        _lastFocusedWindow ??= window;

        StartSession(window, spec.Launch, title: null);
        window.RequestFocus();
        return window;
    }

    /// <summary>Closes a specific window now (dispose → unregister → drop from the list).</summary>
    public void CloseWindow(TerminalWindow window)
    {
        if (!_windows.Remove(window))
        {
            return;
        }

        if (ReferenceEquals(_lastFocusedWindow, window))
        {
            _lastFocusedWindow = null;
        }

        window.Dispose();
    }

    /// <summary>Starts a session as a new tab in the focused window (or a new window when none exists).</summary>
    public void OpenTab(ProcessLaunchOptions? launchOptions = null, string? sessionTitle = null)
    {
        var target = FocusTarget;
        if (target == null)
        {
            OpenWindow(launchOptions, sessionTitle);
            return;
        }

        StartSession(target, launchOptions, sessionTitle);
        target.RequestFocus();
    }

    private static void StartSession(TerminalWindow window, ProcessLaunchOptions? launchOptions, string? title)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await window.Sessions.CreateSessionAsync(title, launchOptions);
            }
            catch (Exception ex)
            {
                ModLog.Log.Error($"GhosttyTerminalController: failed to start session: {ex.Message}");
                window.NotifySessionStartFailed(ex.Message);
            }
        });
    }

    /// <summary>Applies a theme to the focused window and persists it as the default for new windows.</summary>
    public bool ApplyTheme(ThemeDefinition theme)
    {
        var target = FocusTarget;
        if (target == null)
        {
            return false;
        }

        target.ApplyTheme(theme);
        PersistDisplayDefaults(target);
        return true;
    }

    /// <summary>
    /// Saves the focused window's current settings (colors + font + opacities) as
    /// a user theme TOML file and makes it the window's current theme.
    /// </summary>
    public ThemeDefinition? SaveFocusedWindowAsTheme(string name)
    {
        var target = FocusTarget;
        if (target == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var saved = _catalog.SaveUserTheme(target.SnapshotAsTheme(name.Trim()));
        target.Settings.ThemeName = saved.Name;
        PersistDisplayDefaults(target);
        return saved;
    }

    /// <summary>
    /// Writes a window's display settings into the config as the defaults for
    /// new windows, and saves the config file.
    /// </summary>
    public void PersistDisplayDefaults(TerminalWindow? window = null)
    {
        window ??= FocusTarget;
        if (window == null)
        {
            return;
        }

        _config.SyncRuntimeDisplaySettings(window.Settings);
        _config.Save();
    }

    private TerminalWindowSettings CreateWindowSettingsFromDefaults()
    {
        var theme = _catalog.Find(_config.SelectedThemeName) ?? _catalog.Default;
        var settings = new TerminalWindowSettings
        {
            ThemeName = theme.Name,
            Colors = theme.Colors.Clone(),
            FontFamily = _config.FontFamily ?? "Hack",
            FontSize = Math.Clamp(_config.FontSize ?? 32f, LayoutConstants.MIN_FONT_SIZE, LayoutConstants.MAX_FONT_SIZE),
            BackgroundOpacity = Math.Clamp(_config.BackgroundOpacity, 0f, 1f),
            ForegroundOpacity = Math.Clamp(_config.ForegroundOpacity, 0f, 1f),
            CellBackgroundOpacity = Math.Clamp(_config.CellBackgroundOpacity, 0f, 1f),
            CursorStyle = _config.CursorStyle,
            CursorBlink = _config.CursorBlink,
            BorderOnFocus = _config.BorderOnFocus,
            BorderOnHover = _config.BorderOnHover,
            BorderOpacity = Math.Clamp(_config.BorderOpacity, 0f, 1f),
            LockMode = _config.LockMode,
            HotZoneEnabled = _config.HotZoneEnabled,
            HotZonePlacement = _config.HotZonePlacement,
            HotZoneWidth = Math.Clamp(_config.HotZoneWidth, TerminalWindow.MinHotZoneSize, TerminalWindow.MaxHotZoneSize),
            HotZoneHeight = Math.Clamp(_config.HotZoneHeight, TerminalWindow.MinHotZoneSize, TerminalWindow.MaxHotZoneSize),
            HotZoneColor = _config.HotZoneColor,
            HotZoneOpacity = Math.Clamp(_config.HotZoneOpacity, 0f, 1f),
            HotZoneHoverOpacity = Math.Clamp(_config.HotZoneHoverOpacity, 0f, 1f),
        };

        // A theme that carries display settings (user-saved) overrides the loose defaults.
        settings.ApplyThemeOverrides(theme);
        return settings;
    }

    private void OnWindowFocusChanged(TerminalWindow window, bool focused)
    {
        if (focused)
        {
            _lastFocusedWindow = window;
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }

            _isVisible = value;
            if (value)
            {
                if (!_windows.Any(w => w.IsOpen))
                {
                    OpenWindow();
                }
                else
                {
                    FocusTarget?.RequestFocus();
                }
            }
            else
            {
                PersistWindowState();
            }
        }
    }

    public void Update(float deltaTime)
    {
        _blinkTimer += deltaTime;
        if (_blinkTimer >= 0.53)
        {
            _blinkTimer = 0;
            _cursorOn = !_cursorOn;
        }

        // While hidden, Render() never runs, so the surfaces are never ticked
        // and PTY output would pile up in their inboxes (htop, a running build).
        // Drain them on a low cadence — called on the same (tick) thread as
        // Render, preserving the single-threaded engine invariant.
        if (!_isVisible && !_disposed)
        {
            _hiddenDrainTimer += deltaTime;
            if (_hiddenDrainTimer >= 0.25f)
            {
                _hiddenDrainTimer = 0f;
                foreach (var window in _windows)
                {
                    window.DrainSessions();
                }
            }
        }
        else
        {
            _hiddenDrainTimer = 0f;
        }
    }

    public void Render()
    {
        if (_disposed || !_isVisible)
        {
            _anyTerminalActive = false;
            return;
        }

        // Prune windows closed last frame (close button / last tab closed).
        // Remember the geometry of a pruned window: if it turns out to be the
        // last one, its geometry must be persisted here — the hide path's
        // PersistWindowState only sees *open* windows, so without this the
        // next toggle restored stale geometry from the previous hide.
        (float2 Pos, float2 Size, int Cols, int Rows)? prunedGeometry = null;
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].IsOpen)
            {
                if (ReferenceEquals(_lastFocusedWindow, _windows[i]))
                {
                    _lastFocusedWindow = null;
                }

                if (_windows[i].HasObservedGeometry)
                {
                    var (cols, rows) = _windows[i].Sessions.LastKnownTerminalDimensions;
                    prunedGeometry = (_windows[i].LastKnownPosition, _windows[i].LastKnownSize, cols, rows);
                }

                _windows[i].Dispose();
                _windows.RemoveAt(i);
            }
        }

        if (_windows.Count == 0)
        {
            // Every window was closed; hide until the next toggle recreates one.
            if (prunedGeometry is { } geo)
            {
                _config.SetTerminalWindowState(geo.Pos, geo.Size, geo.Cols, geo.Rows);
                _config.Save();
            }

            _isVisible = false;
            _anyTerminalActive = false;
            return;
        }

        bool hideChrome = _config.HideUiWhenNotHovered;
        bool anyActive = false;
        for (int i = 0; i < _windows.Count; i++)
        {
            var window = _windows[i];
            window.Render(hideChrome, _cursorOn);
            // The grid context menu steals ImGui focus from its window while
            // open, but the user is still interacting with the terminal — keep
            // the game-key gate engaged or hotkeys fire under the popup.
            anyActive |= window.HasFocus || window.IsContextMenuOpen;
        }

        _anyTerminalActive = anyActive;
    }

    /// <summary>Persists the primary window's geometry/grid so the next run restores it.</summary>
    public void PersistWindowState()
    {
        var window = _windows.FirstOrDefault(w => w.IsOpen && w.HasObservedGeometry);
        if (window == null)
        {
            return;
        }

        var (cols, rows) = window.Sessions.LastKnownTerminalDimensions;
        _config.SetTerminalWindowState(window.LastKnownPosition, window.LastKnownSize, cols, rows);
        _config.Save();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        PersistWindowState();

        _disposed = true;
        _anyTerminalActive = false;

        foreach (var window in _windows)
        {
            window.Dispose();
        }

        _windows.Clear();
        _lastFocusedWindow = null;
    }
}
