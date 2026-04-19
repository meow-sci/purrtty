using System;
using System.IO;
using System.Linq;
using Brutal.ImGuiApi;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Color Theme submenu with theme selection and customization options.
/// Provides access to built-in and TOML-loaded themes with refresh capability.
/// </summary>
internal class ColorThemeSubmenuRenderer
{
  private readonly ThemeConfiguration _themeConfig;

  public ColorThemeSubmenuRenderer(ThemeConfiguration themeConfig)
  {
    _themeConfig = themeConfig ?? throw new ArgumentNullException(nameof(themeConfig));
  }

  /// <summary>
  /// Renders the Color Theme submenu content with theme selection options.
  /// Displays all available themes including built-in and TOML-loaded themes.
  /// Note: Parent menu handles BeginMenu/EndMenu calls.
  /// </summary>
  public void RenderContent()
  {
    // Initialize theme system if not already done
    ThemeManager.InitializeThemes();

    var availableThemes = ThemeManager.AvailableThemes;
    var currentTheme = ThemeManager.CurrentTheme;

    // Group themes by source: built-in first, then TOML
    var builtInThemes = availableThemes.Where(t => t.Source == ThemeSource.BuiltIn)
                                      .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                      .ToList();
    var tomlThemes = availableThemes.Where(t => t.Source == ThemeSource.TomlFile)
                                   .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                                   .ToList();

    // Render built-in themes
    if (builtInThemes.Count > 0)
    {
      ImGui.Text("Built-in Themes:");
      ImGui.Separator();

      foreach (var theme in builtInThemes)
      {
        bool isSelected = theme.Name == currentTheme.Name;

        if (ImGui.MenuItem(theme.Name, "", isSelected))
        {
          ApplySelectedTheme(theme);
        }

        // Show tooltip with theme information
        if (ImGui.IsItemHovered())
        {
          ImGui.SetTooltip($"Theme: {theme.Name}\nType: {theme.Type}\nSource: Built-in");
        }
      }
    }

    // Add separator between built-in and TOML themes if both exist
    if (builtInThemes.Count > 0 && tomlThemes.Count > 0)
    {
      ImGui.Separator();
    }

    // Render TOML themes
    if (tomlThemes.Count > 0)
    {
      ImGui.Text("TOML Themes:");
      ImGui.Separator();

      foreach (var theme in tomlThemes)
      {
        bool isSelected = theme.Name == currentTheme.Name;

        if (ImGui.MenuItem(theme.Name, "", isSelected))
        {
          ApplySelectedTheme(theme);
        }

        // Show tooltip with theme information
        if (ImGui.IsItemHovered())
        {
          var tooltip = $"Theme: {theme.Name}\nType: {theme.Type}\nSource: TOML File";
          if (!string.IsNullOrEmpty(theme.FilePath))
          {
            tooltip += $"\nFile: {Path.GetFileName(theme.FilePath)}";
          }
          ImGui.SetTooltip(tooltip);
        }
      }
    }

    // Show message if no themes available
    if (availableThemes.Count == 0)
    {
      ImGui.Text("No themes available");
    }

    // Add refresh option
    if (tomlThemes.Count > 0 || availableThemes.Count == 0)
    {
      ImGui.Separator();
      if (ImGui.MenuItem("Refresh Themes"))
      {
        RefreshThemes();
      }

      if (ImGui.IsItemHovered())
      {
        ImGui.SetTooltip("Reload themes from TerminalThemes directory");
      }
    }
  }

  /// <summary>
  /// Applies the selected theme and handles any errors.
  /// </summary>
  /// <param name="theme">The theme to apply</param>
  private void ApplySelectedTheme(TerminalTheme theme)
  {
    try
    {
      Console.WriteLine($"TerminalController: Applying theme: {theme.Name}");

      // Apply the theme through ThemeManager
      ThemeManager.ApplyTheme(theme);

      Console.WriteLine($"TerminalController: Successfully applied theme: {theme.Name}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Failed to apply theme {theme.Name}: {ex.Message}");
      // Theme system should handle fallback to default theme
    }
  }

  /// <summary>
  /// Refreshes the available themes by reloading from the filesystem.
  /// </summary>
  private void RefreshThemes()
  {
    try
    {
      Console.WriteLine("TerminalController: Refreshing themes...");

      // Refresh themes through ThemeManager
      ThemeManager.RefreshAvailableThemes();

      Console.WriteLine($"TerminalController: Themes refreshed. Available themes: {ThemeManager.AvailableThemes.Count}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Failed to refresh themes: {ex.Message}");
    }
  }
}
