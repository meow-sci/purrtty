using System;
using Brutal.ImGuiApi;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Settings menu with submenus for theme, font, window, shells, and performance configuration.
/// Orchestrates all settings-related submenus and provides a unified settings interface.
/// </summary>
internal class SettingsMenuRenderer
{
  private readonly ColorThemeSubmenuRenderer _colorThemeSubmenu;
  private readonly FontSubmenuRenderer _fontSubmenu;
  private readonly WindowSubmenuRenderer _windowSubmenu;
  private readonly ShellsSubmenuRenderer _shellsSubmenu;
  private readonly GameShellSubmenuRenderer _gameShellSubmenu;
  private readonly PerformanceSubmenuRenderer _performanceSubmenu;

  public SettingsMenuRenderer(
    ColorThemeSubmenuRenderer colorTheme,
    FontSubmenuRenderer font,
    WindowSubmenuRenderer window,
    ShellsSubmenuRenderer shells,
    GameShellSubmenuRenderer gameShell,
    PerformanceSubmenuRenderer performance)
  {
    _colorThemeSubmenu = colorTheme ?? throw new ArgumentNullException(nameof(colorTheme));
    _fontSubmenu = font ?? throw new ArgumentNullException(nameof(font));
    _windowSubmenu = window ?? throw new ArgumentNullException(nameof(window));
    _shellsSubmenu = shells ?? throw new ArgumentNullException(nameof(shells));
    _gameShellSubmenu = gameShell ?? throw new ArgumentNullException(nameof(gameShell));
    _performanceSubmenu = performance ?? throw new ArgumentNullException(nameof(performance));
  }

  /// <summary>
  /// Renders the Settings menu with all submenus.
  /// </summary>
  /// <returns>True if the menu is currently open, false otherwise.</returns>
  public bool Render()
  {
    bool isOpen = ImGui.BeginMenu("Settings");
    if (isOpen)
    {
      try
      {
        // Color Theme submenu
        if (ImGui.BeginMenu("Color Theme"))
        {
          _colorThemeSubmenu.RenderContent();
          ImGui.EndMenu();
        }

        // Font submenu
        if (ImGui.BeginMenu("Font"))
        {
          _fontSubmenu.RenderContent();
          ImGui.EndMenu();
        }

        // Window submenu
        if (ImGui.BeginMenu("Window"))
        {
          _windowSubmenu.RenderContent();
          ImGui.EndMenu();
        }

        // Shells submenu
        if (ImGui.BeginMenu("Shells"))
        {
          _shellsSubmenu.RenderContent();
          ImGui.EndMenu();
        }

        // Game Shell submenu
        if (ImGui.BeginMenu("Game Shell"))
        {
          _gameShellSubmenu.RenderContent();
          ImGui.EndMenu();
        }

        ImGui.Separator();

        // Performance submenu
        if (ImGui.BeginMenu("Performance"))
        {
          _performanceSubmenu.RenderContent();
          ImGui.EndMenu();
        }
      }
      finally
      {
        ImGui.EndMenu();
      }
    }
    return isOpen;
  }
}
