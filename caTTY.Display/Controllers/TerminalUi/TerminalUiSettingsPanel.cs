using System;
using System.Linq;
using Brutal.ImGuiApi;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers.TerminalUi.Menus;
using caTTY.Display.Performance;
using caTTY.Display.Rendering;
using caTTY.Display.Utils;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
/// Coordinates menu bar rendering and shell configuration initialization.
/// Delegates all menu rendering to specialized renderer classes.
/// </summary>
internal class TerminalUiSettingsPanel
{
  private readonly SessionManager _sessionManager;
  private readonly ThemeConfiguration _themeConfig;
  private readonly SessionsMenuRenderer _sessionsMenuRenderer;
  private readonly EditMenuRenderer _editMenuRenderer;
  private readonly SettingsMenuRenderer _settingsMenuRenderer;

  /// <summary>
  /// Gets or sets whether any menu is currently open in the menu bar.
  /// </summary>
  public bool IsAnyMenuOpen { get; internal set; }

  public TerminalUiSettingsPanel(
    TerminalController controller,
    SessionManager sessionManager,
    ThemeConfiguration themeConfig,
    TerminalUiFonts fonts,
    TerminalUiSelection selection,
    Action triggerTerminalResizeForAllSessions,
    PerformanceStopwatch perfWatch)
  {
    if (controller == null) throw new ArgumentNullException(nameof(controller));
    if (fonts == null) throw new ArgumentNullException(nameof(fonts));
    if (selection == null) throw new ArgumentNullException(nameof(selection));
    if (triggerTerminalResizeForAllSessions == null) throw new ArgumentNullException(nameof(triggerTerminalResizeForAllSessions));
    if (perfWatch == null) throw new ArgumentNullException(nameof(perfWatch));

    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    _themeConfig = themeConfig ?? throw new ArgumentNullException(nameof(themeConfig));

    // Create submenu renderers first
    var colorThemeSubmenu = new ColorThemeSubmenuRenderer(themeConfig);
    var fontSubmenu = new FontSubmenuRenderer(fonts, sessionManager, triggerTerminalResizeForAllSessions);
    var windowSubmenu = new WindowSubmenuRenderer(themeConfig);
    var shellsSubmenu = new ShellsSubmenuRenderer(themeConfig, sessionManager);
    var gameShellSubmenu = new GameShellSubmenuRenderer(themeConfig);
    var performanceSubmenu = new PerformanceSubmenuRenderer(perfWatch);

    // Create top-level menus
    _sessionsMenuRenderer = new SessionsMenuRenderer(controller, sessionManager);
    _editMenuRenderer = new EditMenuRenderer(controller, selection);
    _settingsMenuRenderer = new SettingsMenuRenderer(
      colorThemeSubmenu,
      fontSubmenu,
      windowSubmenu,
      shellsSubmenu,
      gameShellSubmenu,
      performanceSubmenu
    );
  }

  /// <summary>
  /// Renders the menu bar by coordinating all menu renderers.
  /// Delegates to Sessions, Edit, and Settings menu renderers.
  /// </summary>
  public void RenderMenuBar()
  {
    if (ImGui.BeginMenuBar())
    {
      try
      {
        // Track if any menu is open by collecting BeginMenu return values from all menu renderers
        bool sessionsMenuOpen = _sessionsMenuRenderer.Render();
        bool editMenuOpen = _editMenuRenderer.Render();
        bool settingsMenuOpen = _settingsMenuRenderer.Render();

        // Set IsAnyMenuOpen to true if ANY menu is currently open
        IsAnyMenuOpen = sessionsMenuOpen || editMenuOpen || settingsMenuOpen;
      }
      finally
      {
        ImGui.EndMenuBar();
      }
    }
    else
    {
      // Menu bar is not visible, so no menus can be open
      IsAnyMenuOpen = false;
    }
  }


  /// <summary>
  /// Applies the loaded shell configuration to the session manager during initialization.
  /// </summary>
  public void ApplyShellConfigurationToSessionManager()
  {
    try
    {
      // Check if the configured shell is available
      // CustomGame shells don't need strict validation here - they will be validated when the session is created
      bool shellAvailable = _themeConfig.DefaultShellType == ShellType.CustomGame
        ? !string.IsNullOrEmpty(_themeConfig.DefaultCustomGameShellId)
        : ShellAvailabilityChecker.IsShellAvailable(_themeConfig.DefaultShellType);

      if (!shellAvailable)
      {
        // Fall back to the first available standard shell
        var availableShells = ShellAvailabilityChecker.GetAvailableShells();
        var availableNonCustomGame = availableShells.Where(s => s != ShellType.CustomGame).ToList();

        if (availableNonCustomGame.Count > 0)
        {
          // Prefer PowerShell or WSL for fallback
          ShellType fallbackShell;
          if (availableNonCustomGame.Contains(ShellType.PowerShell))
          {
            fallbackShell = ShellType.PowerShell;
          }
          else if (availableNonCustomGame.Contains(ShellType.Wsl))
          {
            fallbackShell = ShellType.Wsl;
          }
          else
          {
            fallbackShell = availableNonCustomGame[0];
          }

          _themeConfig.DefaultShellType = fallbackShell;
          _themeConfig.Save(); // Save the fallback choice
        }
      }

      // Create launch options from loaded configuration
      var launchOptions = _themeConfig.CreateLaunchOptions();

      // Set default terminal dimensions and working directory
      launchOptions.InitialWidth = 80;
      launchOptions.InitialHeight = 24;
      launchOptions.WorkingDirectory = Environment.CurrentDirectory;

      // Update session manager with loaded default launch options
      _sessionManager.UpdateDefaultLaunchOptions(launchOptions);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error loading shell configuration: {ex.Message}");
      // Continue with default shell configuration
    }
  }
}
