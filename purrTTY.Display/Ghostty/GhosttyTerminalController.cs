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
    private bool _hadAnyFocus;
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

    public event EventHandler<DataInputEventArgs>? DataInput;
    public event EventHandler<FocusChangedEventArgs>? FocusChanged;

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
            var focused = _windows.FirstOrDefault(w => w.IsOpen && w.HasFocus);
            if (focused != null)
            {
                return focused;
            }

            if (_lastFocusedWindow is { IsOpen: true })
            {
                return _lastFocusedWindow;
            }

            return _windows.FirstOrDefault(w => w.IsOpen);
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
        window.DataInput += bytes =>
        {
            // Typing resets the shared blink phase so the cursor is solid while
            // the user types (standard terminal behavior).
            _blinkTimer = 0;
            _cursorOn = true;
            DataInput?.Invoke(this, new DataInputEventArgs(bytes));
        };
        _windows.Add(window);
        _lastFocusedWindow ??= window;

        StartSession(window, launchOptions, sessionTitle);
        window.RequestFocus();
        return window;
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

        _config.SyncRuntimeDisplaySettings(
            window.Settings.ThemeName,
            window.Settings.FontFamily,
            window.Settings.FontSize,
            window.Settings.BackgroundOpacity,
            window.Settings.ForegroundOpacity,
            window.Settings.CellBackgroundOpacity);
        _config.SyncRuntimeFocusSettings(
            window.Settings.CursorStyle,
            window.Settings.CursorBlink,
            window.Settings.BorderOnFocus,
            window.Settings.BorderOnHover,
            window.Settings.BorderOpacity,
            window.Settings.LockMode,
            window.Settings.HotZoneEnabled,
            window.Settings.HotZonePlacement,
            window.Settings.HotZoneWidth,
            window.Settings.HotZoneHeight,
            window.Settings.HotZoneColor,
            window.Settings.HotZoneOpacity,
            window.Settings.HotZoneHoverOpacity);
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
        if (theme.FontFamily is { } family)
        {
            settings.FontFamily = family;
        }

        if (theme.FontSize is { } size)
        {
            settings.FontSize = Math.Clamp(size, LayoutConstants.MIN_FONT_SIZE, LayoutConstants.MAX_FONT_SIZE);
        }

        if (theme.BackgroundOpacity is { } bg)
        {
            settings.BackgroundOpacity = Math.Clamp(bg, 0f, 1f);
        }

        if (theme.ForegroundOpacity is { } fg)
        {
            settings.ForegroundOpacity = Math.Clamp(fg, 0f, 1f);
        }

        if (theme.CellBackgroundOpacity is { } cell)
        {
            settings.CellBackgroundOpacity = Math.Clamp(cell, 0f, 1f);
        }

        if (theme.CursorStyle is { } cursorStyle)
        {
            settings.CursorStyle = cursorStyle;
        }

        if (theme.CursorBlink is { } cursorBlink)
        {
            settings.CursorBlink = cursorBlink;
        }

        if (theme.BorderOnFocus is { } borderOnFocus)
        {
            settings.BorderOnFocus = borderOnFocus;
        }

        if (theme.BorderOnHover is { } borderOnHover)
        {
            settings.BorderOnHover = borderOnHover;
        }

        if (theme.BorderOpacity is { } borderOpacity)
        {
            settings.BorderOpacity = Math.Clamp(borderOpacity, 0f, 1f);
        }

        if (theme.LockMode is { } lockMode)
        {
            settings.LockMode = lockMode;
        }

        if (theme.HotZoneEnabled is { } hotZoneEnabled)
        {
            settings.HotZoneEnabled = hotZoneEnabled;
        }

        if (theme.HotZonePlacement is { } hotZonePlacement)
        {
            settings.HotZonePlacement = hotZonePlacement;
        }

        if (theme.HotZoneWidth is { } hotZoneWidth)
        {
            settings.HotZoneWidth = Math.Clamp(hotZoneWidth, TerminalWindow.MinHotZoneSize, TerminalWindow.MaxHotZoneSize);
        }

        if (theme.HotZoneHeight is { } hotZoneHeight)
        {
            settings.HotZoneHeight = Math.Clamp(hotZoneHeight, TerminalWindow.MinHotZoneSize, TerminalWindow.MaxHotZoneSize);
        }

        if (theme.HotZoneColor is { } hotZoneColor)
        {
            settings.HotZoneColor = hotZoneColor;
        }

        if (theme.HotZoneOpacity is { } hotZoneOpacity)
        {
            settings.HotZoneOpacity = Math.Clamp(hotZoneOpacity, 0f, 1f);
        }

        if (theme.HotZoneHoverOpacity is { } hotZoneHoverOpacity)
        {
            settings.HotZoneHoverOpacity = Math.Clamp(hotZoneHoverOpacity, 0f, 1f);
        }

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

    public bool HasFocus => _windows.Any(w => w.IsOpen && w.HasFocus);
    public bool IsInputCaptureActive => _isVisible && HasFocus;

    public bool ShouldCaptureInput() => IsInputCaptureActive;

    public void ForceFocus() => FocusTarget?.RequestFocus();

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
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].IsOpen)
            {
                if (ReferenceEquals(_lastFocusedWindow, _windows[i]))
                {
                    _lastFocusedWindow = null;
                }

                _windows[i].Dispose();
                _windows.RemoveAt(i);
            }
        }

        if (_windows.Count == 0)
        {
            // Every window was closed; hide until the next toggle recreates one.
            _isVisible = false;
            _anyTerminalActive = false;
            return;
        }

        bool hideChrome = _config.HideUiWhenNotHovered;
        bool anyFocused = false;
        bool anyActive = false;
        foreach (var window in _windows)
        {
            window.Render(hideChrome, _cursorOn);
            anyFocused |= window.HasFocus;
            // The grid context menu steals ImGui focus from its window while
            // open, but the user is still interacting with the terminal — keep
            // the game-key gate engaged or hotkeys fire under the popup.
            anyActive |= window.HasFocus || window.IsContextMenuOpen;
        }

        _anyTerminalActive = anyActive;
        if (anyFocused != _hadAnyFocus)
        {
            FocusChanged?.Invoke(this, new FocusChangedEventArgs(anyFocused, _hadAnyFocus));
            _hadAnyFocus = anyFocused;
        }
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

    public bool CopySelectionToClipboard() => FocusTarget?.CopySelectionToClipboard() ?? false;

    public void PasteFromClipboard() => FocusTarget?.PasteFromClipboard();

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
