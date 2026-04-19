using System;
using System.Linq;
using Brutal.ImGuiApi;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using caTTY.Display.Utils;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Shells submenu with shell configuration options.
/// Allows users to select default shell type and configure shell-specific options.
/// Only shows shells that are available on the current system.
/// </summary>
internal class ShellsSubmenuRenderer
{
  private readonly ThemeConfiguration _themeConfig;
  private readonly SessionManager _sessionManager;

  public ShellsSubmenuRenderer(
    ThemeConfiguration themeConfig,
    SessionManager sessionManager)
  {
    _themeConfig = themeConfig ?? throw new ArgumentNullException(nameof(themeConfig));
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
  }

  /// <summary>
  /// Renders the Shells submenu content with shell configuration options.
  /// Allows users to select default shell type and configure shell-specific options.
  /// Note: Parent menu handles BeginMenu/EndMenu calls.
  /// </summary>
  public void RenderContent()
  {
    RenderShellConfigurationSection();
  }

  /// <summary>
  /// Renders the shell configuration section in the Shells submenu.
  /// Allows users to select the default shell type from available shells.
  /// </summary>
  private void RenderShellConfigurationSection()
  {
    var config = _themeConfig;

    // Check if current shell is available
    bool currentShellAvailable = ShellAvailabilityChecker.IsShellAvailable(config.DefaultShellType);

    // Current shell display with availability indicator
    if (currentShellAvailable)
    {
      ImGui.Text($"Current Default Shell: {config.GetShellDisplayName()}");
    }
    else
    {
      ImGui.TextColored(new float4(1.0f, 0.6f, 0.0f, 1.0f), $"Current Default Shell: {config.GetShellDisplayName()} (Not Available)");
      if (ImGui.IsItemHovered())
      {
        ImGui.SetTooltip("The currently configured shell is not available on this system. Please select an available shell below.");
      }
    }

    if (ImGui.IsItemHovered() && currentShellAvailable)
    {
      ImGui.SetTooltip("This shell will be used for new terminal sessions");
    }

    ImGui.Spacing();

    // Shell type selection - only show available shells
    ImGui.Text("Select Default Shell:");

    var availableShells = ShellAvailabilityChecker.GetAvailableShellsWithNames();

    // Show message if no shells are available (shouldn't happen, but defensive programming)
    if (availableShells.Count == 0)
    {
      ImGui.TextColored(new float4(1.0f, 0.5f, 0.5f, 1.0f), "No shells available on this system");
      return;
    }

    // If current shell is not available, show warning (fallback is handled during initialization)
    if (!currentShellAvailable)
    {
      ImGui.TextColored(new float4(1.0f, 0.6f, 0.0f, 1.0f), "Note: Shell availability changed since last configuration");
      ImGui.Spacing();
    }

    foreach (var (shellType, displayName) in availableShells)
    {
      bool isSelected = config.DefaultShellType == shellType;

      if (ImGui.RadioButton($"{displayName}##shell_{shellType}", isSelected))
      {
        if (!isSelected)
        {
          config.DefaultShellType = shellType;
          if (shellType == ShellType.CustomGame)
          {
            // Auto-select Game Console shell when CustomGame is selected
            config.DefaultCustomGameShellId = "GameConsoleShell";
          }

          // Apply configuration immediately when shell type changes
          ApplyShellConfiguration();
        }
      }

      // Add tooltips for each shell type
      if (ImGui.IsItemHovered())
      {
        var tooltip = shellType switch
        {
          ShellType.PowerShell => "Traditional Windows PowerShell (powershell.exe)",
          ShellType.Wsl => "Windows Subsystem for Linux - Recommended for development work",
          ShellType.Cmd => "Windows Command Prompt (cmd.exe)",
          ShellType.CustomGame => "KSA game console interface - Execute game commands directly",
          _ => "Shell option"
        };
        ImGui.SetTooltip(tooltip);
      }
    }

    // Show current configuration status
    ImGui.Spacing();
    ImGui.Text("Settings are applied automatically to new terminal sessions.");
  }

  /// <summary>
  /// Applies the current shell configuration to the session manager and saves settings.
  /// </summary>
  private void ApplyShellConfiguration()
  {
    try
    {
      // Create launch options from current configuration
      var launchOptions = _themeConfig.CreateLaunchOptions();

      // Update session manager with new default launch options
      _sessionManager.UpdateDefaultLaunchOptions(launchOptions);

      // Sync current opacity values from OpacityManager before saving
      // This ensures global opacity settings are preserved when shell type changes
      _themeConfig.BackgroundOpacity = OpacityManager.CurrentBackgroundOpacity;
      _themeConfig.ForegroundOpacity = OpacityManager.CurrentForegroundOpacity;

      // Save configuration to disk
      _themeConfig.Save();

      Console.WriteLine($"Shell configuration applied: {_themeConfig.GetShellDisplayName()}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error applying shell configuration: {ex.Message}");
    }
  }
}
