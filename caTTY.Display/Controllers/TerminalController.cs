using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Brutal.ImGuiApi;
using caTTY.Core.Input;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using caTTY.Core.Utils;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers.TerminalUi;
using caTTY.Display.Input;
using caTTY.Display.Rendering;
using caTTY.Display.Types;
using caTTY.Display.Utils;
using KSA;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Display.Controllers;

/// <summary>
///     ImGui terminal controller that handles display and input for the terminal emulator.
///     This is the shared controller implementation that is used by both the TestApp and GameMod.
/// </summary>
public class TerminalController : ITerminalController
{
  private TerminalRenderingConfig _config = null!;
  private TerminalFontConfig _fontConfig = null!;
  private MouseWheelScrollConfig _scrollConfig = null!;

  // Input handling
  private readonly StringBuilder _inputBuffer = new();
  private readonly SessionManager _sessionManager;
  private ThemeConfiguration _themeConfig = null!;
  private bool _disposed;

  // Mouse tracking infrastructure
  private MouseTrackingManager _mouseTrackingManager = null!;
  private MouseStateManager _mouseStateManager = null!;
  private CoordinateConverter _coordinateConverter = null!;
  private MouseEventProcessor _mouseEventProcessor = null!;
  private MouseInputHandler _mouseInputHandler = null!;

  // Cursor rendering
  private CursorRenderer _cursorRenderer = null!;

  // Mouse wheel scrolling
  private float _wheelAccumulator = 0.0f;

  // Cached terminal rect for mouse position -> cell coordinate conversion
  internal float2 _lastTerminalOrigin;
  internal float2 _lastTerminalSize;

  // Mouse tracking subsystem
  private TerminalUiMouseTracking _mouseTracking = null!;

  // Selection subsystem
  private TerminalUiSelection _selection = null!;

  // Resize subsystem
  private TerminalUiResize _resize = null!;

  // Font subsystem
  private TerminalUiFonts _fonts = null!;

  // Render subsystem
  private TerminalUiRender _render = null!;

  // Input subsystem
  private TerminalUiInput _input = null!;

  // Tabs subsystem
  private TerminalUiTabs _tabs = null!;

  // Settings panel subsystem
  private TerminalUiSettingsPanel _settingsPanel = null!;

  // Events subsystem
  private TerminalUiEvents _events = null!;

  // Performance measurement
  private readonly Performance.PerformanceStopwatch _perfWatch = new();

  // Font and rendering settings (now config-based)
  private bool _isVisible = true;

  // Terminal settings for current instance (preparation for multi-terminal support)
  private TerminalSettings _currentTerminalSettings = new();

  private static int _numFocusedTerminals = 0;
  private static int _numVisibleTerminals = 0;

    /// <summary>
  ///     Gets whether any terminal is visible and focused
  /// </summary>
  public static bool IsAnyTerminalActive => _numFocusedTerminals > 0 && _numVisibleTerminals > 0;

  /// <summary>
  ///     Gets the performance stopwatch for tracing and analysis.
  /// </summary>
  public Performance.PerformanceStopwatch PerfWatch => _perfWatch;

  /// <summary>
  ///     Initializes the controller with all subsystems.
  ///     Called by the builder to complete initialization.
  /// </summary>
  internal void Initialize(
    TerminalRenderingConfig config,
    TerminalFontConfig fontConfig,
    MouseWheelScrollConfig scrollConfig,
    ThemeConfiguration themeConfig,
    TerminalUiFonts fonts,
    CursorRenderer cursorRenderer,
    TerminalUiRender render,
    TerminalUiInput input,
    MouseTrackingManager mouseTrackingManager,
    MouseStateManager mouseStateManager,
    CoordinateConverter coordinateConverter,
    MouseEventProcessor mouseEventProcessor,
    MouseInputHandler mouseInputHandler,
    TerminalUiMouseTracking mouseTracking,
    TerminalUiSelection selection,
    TerminalUiResize resize,
    TerminalUiTabs tabs,
    TerminalUiSettingsPanel settingsPanel,
    TerminalUiEvents events)
  {
    _config = config;
    _fontConfig = fontConfig;
    _scrollConfig = scrollConfig;
    _themeConfig = themeConfig;
    _fonts = fonts;
    _cursorRenderer = cursorRenderer;
    _render = render;
    _input = input;
    _mouseTrackingManager = mouseTrackingManager;
    _mouseStateManager = mouseStateManager;
    _coordinateConverter = coordinateConverter;
    _mouseEventProcessor = mouseEventProcessor;
    _mouseInputHandler = mouseInputHandler;
    _mouseTracking = mouseTracking;
    _selection = selection;
    _resize = resize;
    _tabs = tabs;
    _settingsPanel = settingsPanel;
    _events = events;

    // Initialize opacity manager
    OpacityManager.Initialize();

    // Initialize cursor style to theme default
    ResetCursorToThemeDefaults();

    // Apply shell configuration to session manager
    _settingsPanel.ApplyShellConfigurationToSessionManager();
  }

  /// <summary>
  ///     Creates a new terminal controller with default configuration.
  ///     This constructor maintains backward compatibility.
  /// </summary>
  /// <param name="sessionManager">The session manager instance</param>
  public TerminalController(SessionManager sessionManager)
      : this(sessionManager, DpiContextDetector.DetectAndCreateConfig(), FontContextDetector.DetectAndCreateConfig(), MouseWheelScrollConfig.CreateDefault())
  {
  }

  /// <summary>
  ///     Creates a new terminal controller with the specified rendering configuration.
  ///     Uses automatic font detection for font configuration and default scroll configuration.
  /// </summary>
  /// <param name="sessionManager">The session manager instance</param>
  /// <param name="config">The rendering configuration to use</param>
  public TerminalController(SessionManager sessionManager, TerminalRenderingConfig config)
      : this(sessionManager, config, FontContextDetector.DetectAndCreateConfig(), MouseWheelScrollConfig.CreateDefault())
  {
  }

  /// <summary>
  ///     Creates a new terminal controller with the specified font configuration.
  ///     Uses automatic DPI detection for rendering configuration and default scroll configuration.
  /// </summary>
  /// <param name="sessionManager">The session manager instance</param>
  /// <param name="fontConfig">The font configuration to use</param>
  public TerminalController(SessionManager sessionManager, TerminalFontConfig fontConfig)
      : this(sessionManager, DpiContextDetector.DetectAndCreateConfig(), fontConfig, MouseWheelScrollConfig.CreateDefault())
  {
  }

  /// <summary>
  ///     Creates a new terminal controller with the specified configurations.
  /// </summary>
  /// <param name="sessionManager">The session manager instance</param>
  /// <param name="config">The rendering configuration to use</param>
  /// <param name="fontConfig">The font configuration to use</param>
  public TerminalController(SessionManager sessionManager, TerminalRenderingConfig config, TerminalFontConfig fontConfig)
      : this(sessionManager, config, fontConfig, MouseWheelScrollConfig.CreateDefault())
  {
  }

  /// <summary>
  ///     Creates a new terminal controller with the specified scroll configuration.
  ///     Uses automatic detection for rendering and font configurations.
  /// </summary>
  /// <param name="sessionManager">The session manager instance</param>
  /// <param name="scrollConfig">The mouse wheel scroll configuration to use</param>
  public TerminalController(SessionManager sessionManager, MouseWheelScrollConfig scrollConfig)
      : this(sessionManager, DpiContextDetector.DetectAndCreateConfig(), FontContextDetector.DetectAndCreateConfig(), scrollConfig)
  {
  }

  /// <summary>
  ///     Creates a new terminal controller with the specified configurations.
  /// </summary>
  /// <param name="sessionManager">The session manager instance</param>
  /// <param name="config">The rendering configuration to use</param>
  /// <param name="fontConfig">The font configuration to use</param>
  /// <param name="scrollConfig">The mouse wheel scroll configuration to use</param>
  public TerminalController(SessionManager sessionManager, TerminalRenderingConfig config, TerminalFontConfig fontConfig, MouseWheelScrollConfig scrollConfig)
  {
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));

    // Use builder to initialize all subsystems
    var builder = new TerminalControllerBuilder(sessionManager, config, fontConfig, scrollConfig);
    builder
      .BuildUiSubsystems(this)
      .BuildInputHandlers(this)
      .BuildRenderComponents(this, TriggerTerminalResizeForAllSessions, ResetCursorToThemeDefaults)
      .WireUpEvents()
      .BuildController(this);
  }

  /// <summary>
  ///     Factory method to create a new terminal controller.
  ///     Uses builder pattern for initialization.
  /// </summary>
  public static TerminalController Create(
    SessionManager sessionManager,
    TerminalRenderingConfig? config = null,
    TerminalFontConfig? fontConfig = null,
    MouseWheelScrollConfig? scrollConfig = null)
  {
    config ??= DpiContextDetector.DetectAndCreateConfig();
    fontConfig ??= FontContextDetector.DetectAndCreateConfig();
    scrollConfig ??= MouseWheelScrollConfig.CreateDefault();

    return new TerminalController(sessionManager, config, fontConfig, scrollConfig);
  }

  /// <summary>
  ///     Gets the current font size for debugging purposes.
  /// </summary>
  public float CurrentFontSize => _fonts.CurrentFontSize;

  /// <summary>
  ///     Gets the current character width for debugging purposes.
  /// </summary>
  public float CurrentCharacterWidth => _fonts.CurrentCharacterWidth;

  /// <summary>
  ///     Gets the current line height for debugging purposes.
  /// </summary>
  public float CurrentLineHeight => _fonts.CurrentLineHeight;


  /// <summary>
  ///     Gets the current font configuration for debugging purposes.
  /// </summary>
  public TerminalFontConfig CurrentFontConfig => _fonts.CurrentFontConfig;

  /// <summary>
  ///     Gets the current regular font name for debugging purposes.
  /// </summary>
  public string CurrentRegularFontName => _fonts.CurrentRegularFontName;

  /// <summary>
  ///     Gets the current bold font name for debugging purposes.
  /// </summary>
  public string CurrentBoldFontName => _fonts.CurrentBoldFontName;

  /// <summary>
  ///     Gets the current italic font name for debugging purposes.
  /// </summary>
  public string CurrentItalicFontName => _fonts.CurrentItalicFontName;

  /// <summary>
  ///     Gets the current bold+italic font name for debugging purposes.
  /// </summary>
  public string CurrentBoldItalicFontName => _fonts.CurrentBoldItalicFontName;

  /// <summary>
  ///     Gets or sets whether the terminal window is visible.
  /// </summary>
  public bool IsVisible
  {
    get => _isVisible;
    set
    {
      _isVisible = value;
      if (value)
      {
        _numVisibleTerminals++;
      }
      else
      {
        _numVisibleTerminals = Math.Max(0, _numVisibleTerminals - 1);
      }
    }
  }

  /// <summary>
  ///     Gets whether the terminal window currently has focus.
  /// </summary>
  public bool HasFocus { get; private set; }

  /// <summary>
  ///     Gets whether the terminal window was focused in the previous frame.
  ///     Used for detecting focus state changes.
  /// </summary>
  private bool _wasFocusedLastFrame = false;

  /// <summary>
  ///     Gets whether the terminal window was hovered in the previous frame.
  ///     Used to determine whether to show menu bar and borders (starts true to show UI initially).
  /// </summary>
  private bool _wasHoveredLastFrame = true;

  /// <summary>
  ///     Tracks whether menus are currently open.
  ///     Used to keep the menu bar visible while navigating menus.
  /// </summary>
  private bool _areMenusOpen = false;

  /// <summary>
  ///     Gets whether input capture is currently active.
  ///     When true, terminal should suppress game hotkeys bound to typing.
  /// </summary>
  public bool IsInputCaptureActive => HasFocus && IsVisible;

  /// <summary>
  ///     Event raised when the terminal focus state changes.
  /// </summary>
  public event EventHandler<FocusChangedEventArgs>? FocusChanged;

  /// <summary>
  ///     Event raised when user input should be sent to the process.
  /// </summary>
  public event EventHandler<DataInputEventArgs>? DataInput;

  /// <summary>
  ///     Raises the DataInput event. Used by subsystems to notify of input.
  /// </summary>
  internal void RaiseDataInput(string text, byte[] bytes)
  {
    DataInput?.Invoke(this, new DataInputEventArgs(text, bytes));
  }

  /// <summary>
  ///     Sends text to the shell process. Used internally and by subsystems.
  /// </summary>
  internal void SendToProcess(string text)
  {
    var activeSession = _sessionManager.ActiveSession;
    if (activeSession?.ProcessManager.IsRunning == true)
    {
      try
      {
        // Send directly to process manager (primary data path)
        activeSession.ProcessManager.Write(text);

        // Also raise the DataInput event for external subscribers (monitoring/logging)
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        RaiseDataInput(text, bytes);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Failed to send input to process: {ex.Message}");
      }
    }
  }

  /// <summary>
  ///     Renders the terminal window using ImGui with multi-session UI layout.
  ///     Includes menu bar and tab area for session management.
  /// </summary>
  public void Render()
  {
   _perfWatch.Start("TerminalController.Render");
    try
    {
      if (!_isVisible)
      {
        return;
      }

      // Ensure fonts are loaded before rendering (deferred loading)
      _fonts.EnsureFontsLoaded();

      // Push UI font for menus (always Hack Regular 32.0f)
      _fonts.PushUIFont(out bool uiFontUsed);

    try
    {
      // Determine if UI should be visible
      // Key insight: When popup menus are open, they take focus away from the main window.
      // so we need to show UI if: (focused AND hovered) OR menus/popups are open
      // The menu/popup checks are OUTSIDE the HasFocus check because popups steal focus

      // Check if ANY popup is open (includes context menus, not just menu bar menus)
      bool isAnyPopupOpen = ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

      // Check if tab area is being interacted with (tracked from previous frame since tabs render after this check)
      bool isTabAreaActive = _tabs.IsTabAreaActive;

      // Determine UI visibility
      // If HideUiWhenNotHovered is false (feature disabled), UI is always visible.
      // Otherwise, UI is visible only when focused and hovered (or menus/popups/tabs active)
      bool shouldShowUI = !_themeConfig.HideUiWhenNotHovered || 
                          (HasFocus && _wasHoveredLastFrame) || 
                          _areMenusOpen || 
                          isAnyPopupOpen || 
                          isTabAreaActive;

      // Determine window background based on UI visibility
      float4 windowBgColor;
      if (shouldShowUI)
      {
          // UI Shown: Use theme background (opaque/semi-transparent)
          float4 themeBg = ThemeManager.GetDefaultBackground();
          windowBgColor = OpacityManager.ApplyBackgroundOpacity(themeBg);
      }
      else
      {
          // UI Hidden: Use fully transparent background so menu/tab areas show game world
          windowBgColor = new float4(0.0f, 0.0f, 0.0f, 0.0f);
      }

      ImGui.PushStyleColor(ImGuiCol.WindowBg, windowBgColor);

      // Only make menu bar transparent when UI is hidden
      // When UI is shown, we want the default opaque/grey menu bar ("solid like before")
      int numColorPushes = 1; // Always pushing WindowBg
      if (!shouldShowUI)
      {
          ImGui.PushStyleColor(ImGuiCol.MenuBarBg, new float4(0.0f, 0.0f, 0.0f, 0.0f));
          numColorPushes++;
      }

      ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0.0f, 0.0f));

      // Hide border when UI should not be shown
      float borderSize = shouldShowUI ? 1.0f : 0.0f;
      ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, borderSize);

      // ALWAYS include MenuBar flag to reserve space - we render it transparent when hidden
      // This keeps the terminal canvas position stable
      var windowFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.MenuBar;

      // Snap window size to exactly fit terminal content after resize operations
      if (_resize.ShouldSnapThisFrame)
      {
        float2 targetSize = _resize.TargetWindowSize;
        ImGui.SetNextWindowSize(targetSize, ImGuiCond.Always);
        _resize.ClearSnap();
      }

      ImGui.Begin("Terminal", ref _isVisible, windowFlags);

      ImGui.PopStyleVar();
      ImGui.PopStyleVar();

      // Track focus state and detect changes
      bool currentFocus = ImGui.IsWindowFocused();
      UpdateFocusState(currentFocus);

      // CRITICAL: Manage ImGui input capture based on terminal focus
      // This ensures the game doesn't process keyboard input when terminal is focused
      ManageInputCapture();

      // Always render menu bar area to reserve space, but only show content when UI should be visible
      if (shouldShowUI)
      {
        _settingsPanel.RenderMenuBar();
      }
      else
      {
        // Render empty menu bar to reserve space (keeps terminal canvas position stable)
        if (ImGui.BeginMenuBar())
        {
          ImGui.EndMenuBar();
        }
        // Reset menu state since we're not rendering actual menus
        _settingsPanel.IsAnyMenuOpen = false;
      }

      // Always render tab area space, but only show content when UI should be visible
      ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new float2(4.0f, 0.0f));
      ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new float2(4.0f, 0.0f));
      if (shouldShowUI)
      {
        RenderTabArea();
      }
      else
      {
        // Only reserve space for tab area when there are 2+ sessions (tab bar is only shown then)
        if (_sessionManager.SessionCount >= 2)
        {
          ImGui.Dummy(new float2(0, LayoutConstants.TAB_AREA_HEIGHT));
        }
        // Reset tab area active state since we're not rendering the tab area
        // This prevents stale state from keeping UI visible indefinitely
        _tabs.IsTabAreaActive = false;
      }
      ImGui.PopStyleVar();
      ImGui.PopStyleVar();

      // Handle window resize detection and terminal resizing
      _resize.HandleWindowResize();

      // Process any pending font-triggered terminal resize
      _resize.ProcessPendingFontResize();

      // Pop UI font before rendering terminal content
      TerminalUiFonts.MaybePopFont(uiFontUsed);
      uiFontUsed = false; // Mark as popped

      // Render terminal canvas
      // Pass !shouldShowUI as drawBackground flag
      // When UI is hidden (window transparent), we MUST draw manual background
      // When UI is shown (window opaque), we should NOT draw manual background (use window bg)
      RenderTerminalCanvas(!shouldShowUI);

      // Push UI font again for focus indicators
      _fonts.PushUIFont(out uiFontUsed);

      // Render focus indicators only when UI should be visible (uses UI font)
      if (shouldShowUI)
      {
        RenderFocusIndicators();
      }

      // Handle input if focused
      if (HasFocus)
      {
        HandleInput();
      }

      // Track hover state for next frame (to determine menu/border visibility)
      // Keep UI visible when: window is hovered, OR any interaction is happening (mouse down, menu open, etc.)

      // Check if mouse is within the window bounds
      float2 mousePos = ImGui.GetMousePos();
      float2 windowMin = ImGui.GetWindowPos();
      float2 windowMax = windowMin + ImGui.GetWindowSize();
      bool isMouseInWindowBounds = mousePos.X >= windowMin.X && mousePos.X <= windowMax.X &&
                                    mousePos.Y >= windowMin.Y && mousePos.Y <= windowMax.Y;

      bool isAnyItemHovered = ImGui.IsAnyItemHovered(); // Detects when mouse is over menu items, buttons, etc.
      // Include mouse released to keep UI visible during click release frame (needed for tab close button, etc.)
      // Only consider mouse buttons as interaction if mouse is within window bounds
      bool isInteracting = (isMouseInWindowBounds && (ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Right) ||
                           ImGui.IsMouseReleased(ImGuiMouseButton.Left) || ImGui.IsMouseReleased(ImGuiMouseButton.Right))) ||
                           ImGui.IsAnyItemActive();

      // Update menu state from settings panel (which now properly tracks BeginMenu return values)
      _areMenusOpen = _settingsPanel.IsAnyMenuOpen;

      // Keep UI visible if mouse is in window, items are hovered, or user is interacting
      _wasHoveredLastFrame = isMouseInWindowBounds || isAnyItemHovered || isInteracting;

      ImGui.End();

        // Pop the window background and (optional) menu bar background color styles
        ImGui.PopStyleColor(numColorPushes);
      }
      finally
      {
        TerminalUiFonts.MaybePopFont(uiFontUsed);
      }
    }
    finally
    {
     _perfWatch.Stop("TerminalController.Render");
      _perfWatch.OnFrameEnd();
    }
  }

  /// <summary>
  ///     Updates the terminal controller state.
  ///     This method handles time-based updates like cursor blinking.
  /// </summary>
  /// <param name="deltaTime">Time elapsed since last update in seconds</param>
  public void Update(float deltaTime)
  {
    // Update cursor blink state using theme configuration
    int blinkInterval = ThemeManager.GetCursorBlinkInterval();
    _cursorRenderer.UpdateBlinkState(blinkInterval);
  }

  /// <summary>
  ///     Disposes the terminal controller and cleans up resources.
  /// </summary>
  public void Dispose()
  {
    if (!_disposed)
    {
      // Unsubscribe from session manager events
      if (_sessionManager != null)
      {
        _sessionManager.SessionCreated -= _events.OnSessionCreated;
        _sessionManager.SessionClosed -= _events.OnSessionClosed;
        _sessionManager.ActiveSessionChanged -= _events.OnActiveSessionChanged;

        // Unsubscribe from all session events
        foreach (var session in _sessionManager.Sessions)
        {
          session.Terminal.ScreenUpdated -= _events.OnScreenUpdated;
          session.Terminal.ResponseEmitted -= _events.OnResponseEmitted;
          session.TitleChanged -= _events.OnSessionTitleChanged;
        }
      }

      // Unsubscribe from theme change events
      ThemeManager.ThemeChanged -= _events.OnThemeChanged;

      _disposed = true;
    }
  }

  /// <summary>
  ///     Updates the rendering configuration at runtime.
  /// </summary>
  /// <param name="newConfig">The new configuration to apply</param>
  /// <exception cref="ArgumentNullException">Thrown when newConfig is null</exception>
  /// <exception cref="ArgumentException">Thrown when newConfig contains invalid values</exception>
  public void UpdateRenderingConfig(TerminalRenderingConfig newConfig)
  {
    if (newConfig == null)
    {
      throw new ArgumentNullException(nameof(newConfig));
    }

    // Validate the new configuration
    newConfig.Validate();

    // Note: Font metrics are now managed by TerminalUiFonts subsystem
    // No need to update CurrentFontSize, CurrentCharacterWidth, CurrentLineHeight here

    // Log the configuration change
    // Console.WriteLine("TerminalController: Runtime configuration updated");
    // LogConfiguration();
  }

  /// <summary>
  ///     Updates the font configuration at runtime.
  /// </summary>
  /// <param name="newFontConfig">The new font configuration to apply</param>
  /// <exception cref="ArgumentNullException">Thrown when newFontConfig is null</exception>
  /// <exception cref="ArgumentException">Thrown when newFontConfig contains invalid values</exception>
  public void UpdateFontConfig(TerminalFontConfig newFontConfig)
  {
    _fonts.UpdateFontConfig(newFontConfig, () =>
    {
      // Callback when font configuration changes
      _sessionManager.ApplyFontConfigToAllSessions(_fonts.CurrentFontConfig);
      TriggerTerminalResizeForAllSessions();
    });
  }

  /// <summary>
  ///     Updates the mouse wheel scroll configuration at runtime.
  ///     Includes comprehensive error handling and validation.
  /// </summary>
  /// <param name="newScrollConfig">The new scroll configuration to apply</param>
  /// <exception cref="ArgumentNullException">Thrown when newScrollConfig is null</exception>
  /// <exception cref="ArgumentException">Thrown when newScrollConfig contains invalid values</exception>
  public void UpdateScrollConfig(MouseWheelScrollConfig newScrollConfig)
  {
    if (newScrollConfig == null)
    {
      throw new ArgumentNullException(nameof(newScrollConfig));
    }

    try
    {
      // Validate the new configuration before applying any changes
      newScrollConfig.Validate();

      // Log the configuration change attempt
      Console.WriteLine("TerminalController: Attempting runtime scroll configuration update");
      Console.WriteLine($"  Previous: {_scrollConfig}");
      Console.WriteLine($"  New: {newScrollConfig}");

      // Reset wheel accumulator when changing configuration to prevent inconsistent state
      float previousAccumulator = _wheelAccumulator;
      _wheelAccumulator = 0.0f;

      // Update scroll configuration
      _scrollConfig = newScrollConfig;

      // Log successful configuration change
      Console.WriteLine("TerminalController: Runtime scroll configuration updated successfully");
      Console.WriteLine($"  Applied: {_scrollConfig}");
      Console.WriteLine($"  Wheel accumulator reset from {previousAccumulator:F3} to 0.0");
    }
    catch (ArgumentException ex)
    {
      // Log validation failure and re-throw with additional context
      Console.WriteLine($"TerminalController: Scroll configuration validation failed: {ex.Message}");
      Console.WriteLine($"  Attempted config: {newScrollConfig}");
      Console.WriteLine($"  Current config preserved: {_scrollConfig}");
      throw;
    }
    catch (Exception ex)
    {
      // Log unexpected errors during scroll configuration update
      Console.WriteLine($"TerminalController: Unexpected error during scroll configuration update: {ex.GetType().Name}: {ex.Message}");
      Console.WriteLine($"  Attempted config: {newScrollConfig}");
      Console.WriteLine($"  Current config preserved: {_scrollConfig}");

#if DEBUG
      Console.WriteLine($"TerminalController: UpdateScrollConfig error stack trace: {ex.StackTrace}");
#endif

      // Re-throw the exception wrapped in InvalidOperationException to provide more context
      throw new InvalidOperationException($"Failed to update scroll configuration: {ex.Message}", ex);
    }
  }

  /// <summary>
  ///     Manages ImGui input capture based on terminal focus state.
  ///     This is critical for preventing game hotkeys from being processed when terminal is focused.
  /// </summary>
  private void ManageInputCapture()
  {
    _input.ManageInputCapture();
  }

  /// <summary>
  ///     Updates the focus state and handles focus change events.
  ///     Provides visual focus indicators and manages input capture priority.
  /// </summary>
  /// <param name="currentFocus">The current focus state from ImGui</param>
  private void UpdateFocusState(bool currentFocus)
  {
    bool focusChanged = currentFocus != _wasFocusedLastFrame;

    if (focusChanged)
    {
      if (currentFocus)
      {
        _numFocusedTerminals++;
      }
      else
      {
        _numFocusedTerminals = Math.Max(0, _numFocusedTerminals - 1);
      }

      // Update focus state
      HasFocus = currentFocus;

      // Handle focus gained
      if (currentFocus && !_wasFocusedLastFrame)
      {
        OnFocusGained();
      }
      // Handle focus lost
      else if (!currentFocus && _wasFocusedLastFrame)
      {
        OnFocusLost();
      }

      // Raise focus changed event
      FocusChanged?.Invoke(this, new FocusChangedEventArgs(currentFocus, _wasFocusedLastFrame));

      _wasFocusedLastFrame = currentFocus;
    }
    else
    {
      // Update focus state even if no change (for consistency)
      HasFocus = currentFocus;
    }
  }

  /// <summary>
  ///     Handles focus gained event.
  ///     Called when the terminal window gains focus.
  /// </summary>
  private void OnFocusGained()
  {
    _events.OnFocusGained();
  }

  /// <summary>
  ///     Handles focus lost event.
  ///     Called when the terminal window loses focus.
  /// </summary>
  private void OnFocusLost()
  {
    _events.OnFocusLost();

    // Reset mouse wheel accumulator to prevent stuck scrolling
    _wheelAccumulator = 0.0f;
  }

  /// <summary>
  ///     Renders visual focus indicators for the terminal window.
  ///     Provides clear visual feedback about focus state to match TypeScript implementation.
  /// </summary>
  private void RenderFocusIndicators()
  {
    try
    {
      // Get window draw list for custom drawing
      ImDrawListPtr drawList = ImGui.GetWindowDrawList();

      // Get window bounds for focus indicator
      float2 windowMin = ImGui.GetWindowPos();
      float2 windowMax = windowMin + ImGui.GetWindowSize();

      if (HasFocus)
      {
        // Draw subtle focus border (matches TypeScript visual feedback)
        float4 focusColor = new float4(0.4f, 0.6f, 1.0f, 0.8f); // Light blue
        uint focusColorU32 = ImGui.ColorConvertFloat4ToU32(focusColor);

        // Draw thin border around window content area
        drawList.AddRect(windowMin, windowMax, focusColorU32, 0.0f, ImDrawFlags.None, 2.0f);
      }
      else
      {
        // Draw subtle unfocused border
        float4 unfocusedColor = new float4(0.3f, 0.3f, 0.3f, 0.5f); // Dark gray
        uint unfocusedColorU32 = ImGui.ColorConvertFloat4ToU32(unfocusedColor);

        // Draw thin border around window content area
        drawList.AddRect(windowMin, windowMax, unfocusedColorU32, 0.0f, ImDrawFlags.None, 1.0f);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error rendering focus indicators: {ex.Message}");
    }
  }

  /// <summary>
  ///     Determines whether the terminal should capture input based on focus and visibility.
  ///     When terminal is focused, it should suppress game hotkeys bound to typing.
  ///     When terminal is unfocused/hidden, all input should pass through to game.
  /// </summary>
  /// <returns>True if terminal should capture input, false if input should pass to game</returns>
  public bool ShouldCaptureInput()
  {
    return _input.ShouldCaptureInput();
  }

  /// <summary>
  ///     Forces the terminal to gain focus.
  ///     This can be used by external systems to programmatically focus the terminal.
  /// </summary>
  public void ForceFocus()
  {
    try
    {
      // Set the window focus (ImGui will handle this on next frame)
      // This may fail in unit test environments where ImGui is not initialized
      ImGui.SetWindowFocus("Terminal");
      Console.WriteLine("TerminalController: Focus forced programmatically");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Cannot force focus - ImGui not available: {ex.Message}");
    }
  }

  /// <summary>
  ///     Switches to a specific session and restores focus to the terminal.
  ///     This is a convenience method that combines session switching with focus management.
  /// </summary>
  /// <param name="sessionId">The ID of the session to switch to</param>
  public void SwitchToSessionAndFocus(Guid sessionId)
  {
    _sessionManager.SwitchToSession(sessionId);
    ForceFocus();
  }

  /// <summary>
  ///     Switches to the next session in tab order and restores focus to the terminal.
  ///     This is a convenience method that combines session switching with focus management.
  /// </summary>
  public void SwitchToNextSessionAndFocus()
  {
    _sessionManager.SwitchToNextSession();
    ForceFocus();
  }

  /// <summary>
  ///     Switches to the previous session in tab order and restores focus to the terminal.
  ///     This is a convenience method that combines session switching with focus management.
  /// </summary>
  public void SwitchToPreviousSessionAndFocus()
  {
    _sessionManager.SwitchToPreviousSession();
    ForceFocus();
  }

  /// <summary>
  ///     Handles window resize detection and triggers terminal resizing when needed.
  ///     Matches the TypeScript implementation's approach of detecting display size changes
  ///     and updating both the headless terminal and the PTY process dimensions.
  /// </summary>
  private void HandleWindowResize() => _resize.HandleWindowResize();

  /// <summary>
  ///     Applies a terminal resize to all sessions.
  ///     This updates the headless terminal dimensions for every session and resizes any running PTY processes.
  /// </summary>
  /// <param name="cols">New terminal width in columns</param>
  /// <param name="rows">New terminal height in rows</param>
  internal void ApplyTerminalDimensionsToAllSessions(int cols, int rows) => _resize.ApplyTerminalDimensionsToAllSessions(cols, rows);

  /// <summary>
  ///     Gets the current terminal dimensions for external access.
  ///     Useful for debugging and integration testing.
  /// </summary>
  /// <returns>Current terminal dimensions (width, height)</returns>
  public (int width, int height) GetTerminalDimensions() => _resize.GetTerminalDimensions();

  /// <summary>
  ///     Gets the current window size for debugging purposes.
  /// </summary>
  /// <returns>Current window content area size</returns>
  public float2 GetCurrentWindowSize() => _resize.GetCurrentWindowSize();

  /// <summary>
  ///     Triggers terminal resize calculation based on current window size and updated character metrics.
  ///     This method is called when font configuration changes to ensure terminal dimensions
  ///     are recalculated with the new character metrics without requiring manual window resize.
  /// </summary>
  private void TriggerTerminalResize() => _resize.TriggerTerminalResize();

  /// <summary>
  ///     Performs the actual terminal resize calculation when font changes are pending.
  ///     Called during render frame when ImGui context is available.
  /// </summary>
  private void ProcessPendingFontResize() => _resize.ProcessPendingFontResize();

  /// <summary>
  ///     Triggers terminal resize for all sessions when font configuration changes.
  ///     This method ensures all sessions recalculate their dimensions with new character metrics.
  /// </summary>
  private void TriggerTerminalResizeForAllSessions() => _resize.TriggerTerminalResizeForAllSessions();

  /// <summary>
  ///     Resets cursor style and blink state to theme defaults.
  ///     Called during initialization and when theme changes.
  /// </summary>
  private void ResetCursorToThemeDefaults()
  {
    try
    {
      // Get theme defaults
      CursorStyle defaultStyle = ThemeManager.GetDefaultCursorStyle();

      // Update terminal state using the new public API for active session
      var activeSession = _sessionManager.ActiveSession;
      if (activeSession != null)
      {
        activeSession.Terminal.SetCursorStyle(defaultStyle);
      }

      // Reset cursor renderer blink state
      _cursorRenderer.ResetBlinkState();

      // Console.WriteLine($"TerminalController: Cursor reset to theme defaults - Style: {defaultStyle}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error resetting cursor to theme defaults: {ex.Message}");
    }
  }

  /// <summary>
  ///     Manually triggers a terminal resize to the specified dimensions.
  ///     This method can be used for testing or external resize requests.
  /// </summary>
  /// <param name="cols">New width in columns</param>
  /// <param name="rows">New height in rows</param>
  /// <exception cref="ArgumentException">Thrown when dimensions are invalid</exception>
  public void ResizeTerminal(int cols, int rows) => _resize.ResizeTerminal(cols, rows);

  /// <summary>
  ///     Renders the terminal screen content.
  /// </summary>
  private void RenderTerminalContent(bool drawBackground)
  {
//    _perfWatch.Start("RenderTerminalContent");
    try
    {
      _render.RenderTerminalContent(
        _sessionManager,
        CurrentCharacterWidth,
        CurrentLineHeight,
        _selection.GetCurrentSelection(),
        out _lastTerminalOrigin,
        out _lastTerminalSize,
        HandleMouseInputForTerminal,
        HandleMouseTrackingForApplications,
        RenderContextMenu,
        drawBackground
      );
    }
    finally
    {
//      _perfWatch.Stop("RenderTerminalContent");
    }
  }

  /// <summary>
  /// Renders the right-click context menu for copy/paste operations.
  /// </summary>
  private void RenderContextMenu()
  {
    _selection.RenderContextMenu();
  }


  /// <summary>
  ///     Handles keyboard input when the terminal has focus.
  ///     Enhanced to match TypeScript implementation with comprehensive key encoding.
  ///     Integrates with game input system to manage input capture priority.
  /// </summary>
  private void HandleInput()
  {
    _input.HandleInput();
  }

  /// <summary>
  /// Handles mouse tracking for applications (separate from local selection).
  /// This method processes mouse events for terminal applications that request mouse tracking.
  /// </summary>
  private void HandleMouseTrackingForApplications()
  {
    _mouseTracking.HandleMouseTrackingForApplications();
  }

  /// <summary>
  /// Handles integrated mouse input for both application tracking and local selection.
  /// This method coordinates between mouse tracking for applications and local selection handling.
  /// </summary>
  private void HandleMouseInputIntegrated()
  {
    _mouseTracking.HandleMouseInputIntegrated(HandleMouseInputForTerminal);
  }

  /// <summary>
  /// Updates the coordinate converter with current terminal metrics.
  /// </summary>
  private void UpdateCoordinateConverterMetrics()
  {
    _mouseTracking.UpdateCoordinateConverterMetrics();
  }

  /// <summary>
  /// Synchronizes mouse tracking configuration from terminal state to mouse tracking manager.
  /// </summary>
  private void SyncMouseTrackingConfiguration()
  {
    _mouseTracking.SyncMouseTrackingConfiguration();
  }

  /// <summary>
  /// Handles mouse input for selection and copying.
  /// Integrates with ImGui mouse state to provide text selection functionality.
  /// </summary>
  private void HandleMouseInput()
  {
    // CRITICAL: Check if mouse is over the terminal content area first
    // This prevents window dragging when clicking in the terminal area
    var mousePos = ImGui.GetMousePos();
    bool mouseOverTerminal = IsMouseOverTerminal(mousePos);

    if (!mouseOverTerminal)
    {
      return; // Don't handle mouse input if not over terminal content
    }

    // Delegate to selection subsystem
    _selection.HandleMouseInputForTerminal();
  }

  /// <summary>
  /// Selects all visible content in the terminal viewport.
  /// </summary>
  private void SelectAllVisibleContent()
  {
    _selection.SelectAllVisibleContent();
  }

  /// <summary>
  /// Handles mouse input only when the invisible button is hovered/active.
  /// This method contains the actual mouse input logic for text selection.
  /// This approach prevents ImGui window dragging when selecting text in the terminal.
  /// </summary>
  private void HandleMouseInputForTerminal()
  {
    _selection.HandleMouseInputForTerminal();
  }



  /// <summary>
  /// Converts mouse coordinates to terminal cell coordinates (0-based).
  /// </summary>
  /// <returns>The cell coordinates, or null if the mouse is outside the terminal area</returns>
  private SelectionPosition? GetMouseCellCoordinates()
  {
    return _mouseTracking.GetMouseCellCoordinates();
  }

  /// <summary>
  /// Checks if the mouse is currently over the terminal content area.
  /// This is used to prevent window dragging when selecting text in the terminal.
  /// </summary>
  /// <param name="mousePos">The current mouse position in screen coordinates</param>
  /// <returns>True if the mouse is over the terminal content area, false otherwise</returns>
  private bool IsMouseOverTerminal(float2 mousePos)
  {
    return _mouseTracking.IsMouseOverTerminal(mousePos);
  }

  /// <summary>
  /// Copies the current selection to the clipboard.
  /// </summary>
  /// <returns>True if text was copied successfully, false otherwise</returns>
  public bool CopySelectionToClipboard()
  {
    return _selection.CopySelectionToClipboard();
  }

  /// <summary>
  /// Pastes text from the clipboard to the terminal.
  /// </summary>
  public void PasteFromClipboard()
  {
    _selection.PasteFromClipboard();
  }

  /// <summary>
  /// Gets the current text selection.
  /// </summary>
  /// <returns>The current selection</returns>
  public TextSelection GetCurrentSelection()
  {
    return _selection.GetCurrentSelection();
  }

  /// <summary>
  /// Sets the current text selection.
  /// </summary>
  /// <param name="selection">The selection to set</param>
  public void SetSelection(TextSelection selection)
  {
    _selection.SetSelection(selection);
  }


  /// <summary>
  ///     Handles terminal reset events by resetting cursor to theme defaults.
  ///     This method should be called when terminal reset sequences are processed.
  /// </summary>
  public void OnTerminalReset()
  {
    var activeSession = _sessionManager.ActiveSession;
    if (activeSession != null)
    {
      ResetCursorToThemeDefaults();
    }
  }

  /// <summary>
  /// Gets the current terminal settings instance.
  /// </summary>
  /// <returns>Current terminal settings</returns>
  public TerminalSettings GetCurrentTerminalSettings()
  {
    return _currentTerminalSettings.Clone();
  }

  /// <summary>
  /// Updates the current terminal settings and applies changes.
  /// </summary>

  /// <summary>
  /// Renders the tab area using real ImGui tabs for session management.
  /// Includes add button and context menus for tab operations.
  /// </summary>
  private void RenderTabArea()
  {
    _tabs.RenderTabArea();
  }

  /// <summary>
  /// Renders the terminal canvas for multi-session UI layout.
  /// This method provides terminal display within the session management framework.
  /// </summary>
  /// <summary>
  /// Renders the terminal canvas for multi-session UI layout.
  /// This method provides terminal display within the session management framework.
  /// </summary>
  private void RenderTerminalCanvas(bool drawBackground)
  {
//    _perfWatch.Start("RenderTerminalCanvas");
    try
    {
      // Pop UI font before rendering terminal content
      // Note: This assumes UI font was pushed before calling this method

      // Render terminal content directly without additional UI elements
      RenderTerminalContent(drawBackground);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error rendering terminal canvas: {ex.Message}");

      // Fallback: render a simple error message
      ImGui.Text("Terminal rendering error");
    }
    finally
    {
//      _perfWatch.Stop("RenderTerminalCanvas");
    }
  }

  /// <summary>
  ///     Enable or disable performance tracing.
  /// </summary>
  /// <param name="enabled">Whether to enable performance tracing</param>
  public void EnablePerformanceTracing(bool enabled) => _perfWatch.Enabled = enabled;

  /// <summary>
  ///     Set the performance dump interval in frames.
  /// </summary>
  /// <param name="frames">Number of frames between auto-dumps</param>
  public void SetPerformanceDumpInterval(int frames) => _perfWatch.DumpIntervalFrames = frames;

  /// <summary>
  ///     Get the current performance summary as a formatted string.
  /// </summary>
  /// <returns>Formatted performance summary</returns>
  public string GetPerformanceSummary() => _perfWatch.GetSummaryExclusive();

}

