using System;
using Brutal.ImGuiApi;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using float4 = Brutal.Numerics.float4;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Window submenu with opacity controls and display settings.
/// Provides sliders for background, foreground, and cell background opacity adjustment,
/// along with UI hiding options.
/// </summary>
internal class WindowSubmenuRenderer
{
  private readonly ThemeConfiguration _themeConfig;

  public WindowSubmenuRenderer(ThemeConfiguration themeConfig)
  {
    _themeConfig = themeConfig ?? throw new ArgumentNullException(nameof(themeConfig));
  }

  /// <summary>
  /// Renders the Window submenu content with opacity controls and display settings.
  /// Provides sliders for independent opacity adjustment with immediate visual feedback.
  /// Note: Parent menu handles BeginMenu/EndMenu calls.
  /// </summary>
  public void RenderContent()
  {
    // Initialize opacity manager if not already done
    OpacityManager.Initialize();

    // UI Hiding Option
    bool hideUi = _themeConfig.HideUiWhenNotHovered;
    if (ImGui.Checkbox("Hide UI when not hovered", ref hideUi))
    {
      _themeConfig.HideUiWhenNotHovered = hideUi;
      _themeConfig.Save();
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("When enabled, the window border and menu bar are hidden when the mouse is not\nhovering over the window (while focused). This provides a cleaner, borderless look.");
    }

    ImGui.Separator();

    // Background Opacity Section
    ImGui.Text("Background Opacity:");
    int currentBgOpacityPercent = OpacityManager.GetBackgroundOpacityPercentage();
    int newBgOpacityPercent = currentBgOpacityPercent;

    if (ImGui.SliderInt("##BackgroundOpacitySlider", ref newBgOpacityPercent, 0, 100, $"{newBgOpacityPercent}%%"))
    {
      // Apply background opacity change immediately
      if (OpacityManager.SetBackgroundOpacityFromPercentage(newBgOpacityPercent))
      {
        // Console.WriteLine($"TerminalController: Background opacity set to {newBgOpacityPercent}%");
      }
      else
      {
        Console.WriteLine($"TerminalController: Failed to set background opacity to {newBgOpacityPercent}%");
      }
    }

    // Show tooltip for background opacity
    if (ImGui.IsItemHovered())
    {
      var currentBgOpacity = OpacityManager.CurrentBackgroundOpacity;
      ImGui.SetTooltip($"Background opacity: {currentBgOpacity:F2} ({currentBgOpacityPercent}%)\nAdjust terminal background transparency\nRange: 0% (transparent) to 100% (opaque)");
    }

    // Reset background opacity button
    ImGui.SameLine();
    if (ImGui.Button("Reset##BackgroundOpacityReset"))
    {
      if (OpacityManager.ResetBackgroundOpacity())
      {
        Console.WriteLine("TerminalController: Background opacity reset to default");
      }
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Reset background opacity to 100% (fully opaque)");
    }

    // Cell Background Opacity Section
    ImGui.Text("Cell Background Opacity:");
    int currentCellBgOpacityPercent = OpacityManager.GetCellBackgroundOpacityPercentage();
    int newCellBgOpacityPercent = currentCellBgOpacityPercent;

    if (ImGui.SliderInt("##CellBackgroundOpacitySlider", ref newCellBgOpacityPercent, 0, 100, $"{newCellBgOpacityPercent}%%"))
    {
      // Apply cell background opacity change immediately
      if (OpacityManager.SetCellBackgroundOpacityFromPercentage(newCellBgOpacityPercent))
      {
        // Console.WriteLine($"TerminalController: Cell background opacity set to {newCellBgOpacityPercent}%");
      }
      else
      {
        Console.WriteLine($"TerminalController: Failed to set cell background opacity to {newCellBgOpacityPercent}%");
      }
    }

    // Show tooltip for cell background opacity
    if (ImGui.IsItemHovered())
    {
      var currentCellBgOpacity = OpacityManager.CurrentCellBackgroundOpacity;
      ImGui.SetTooltip($"Cell background opacity: {currentCellBgOpacity:F2} ({currentCellBgOpacityPercent}%)\nAdjust terminal cell background transparency\nRange: 0% (transparent) to 100% (opaque)");
    }

    // Reset cell background opacity button
    ImGui.SameLine();
    if (ImGui.Button("Reset##CellBackgroundOpacityReset"))
    {
      if (OpacityManager.ResetCellBackgroundOpacity())
      {
        Console.WriteLine("TerminalController: Cell background opacity reset to default");
      }
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Reset cell background opacity to 100% (fully opaque)");
    }

    // Foreground Opacity Section
    ImGui.Text("Foreground Opacity:");
    int currentFgOpacityPercent = OpacityManager.GetForegroundOpacityPercentage();
    int newFgOpacityPercent = currentFgOpacityPercent;

    if (ImGui.SliderInt("##ForegroundOpacitySlider", ref newFgOpacityPercent, 0, 100, $"{newFgOpacityPercent}%%"))
    {
      // Apply foreground opacity change immediately
      if (OpacityManager.SetForegroundOpacityFromPercentage(newFgOpacityPercent))
      {
        // Console.WriteLine($"TerminalController: Foreground opacity set to {newFgOpacityPercent}%");
      }
      else
      {
        Console.WriteLine($"TerminalController: Failed to set foreground opacity to {newFgOpacityPercent}%");
      }
    }

    // Show tooltip for foreground opacity
    if (ImGui.IsItemHovered())
    {
      var currentFgOpacity = OpacityManager.CurrentForegroundOpacity;
      ImGui.SetTooltip($"Foreground opacity: {currentFgOpacity:F2} ({currentFgOpacityPercent}%)\nAdjust terminal text transparency\nRange: 0% (transparent) to 100% (opaque)");
    }

    // Reset foreground opacity button
    ImGui.SameLine();
    if (ImGui.Button("Reset##ForegroundOpacityReset"))
    {
      if (OpacityManager.ResetForegroundOpacity())
      {
        Console.WriteLine("TerminalController: Foreground opacity reset to default");
      }
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Reset foreground opacity to 100% (fully opaque)");
    }

    // Reset all button
    ImGui.Separator();
    if (ImGui.Button("Reset All##ResetAllOpacity"))
    {
      if (OpacityManager.ResetOpacity())
      {
        Console.WriteLine("TerminalController: All opacity values reset to default");
      }
    }

    if (ImGui.IsItemHovered())
    {
      ImGui.SetTooltip("Reset background, foreground, and cell background opacity to 100%");
    }

    // Show current opacity status
    ImGui.Separator();
    var bgOpacity = OpacityManager.CurrentBackgroundOpacity;
    var fgOpacity = OpacityManager.CurrentForegroundOpacity;
    var cellBgOpacity = OpacityManager.CurrentCellBackgroundOpacity;
    var bgIsDefault = OpacityManager.IsDefaultBackgroundOpacity();
    var fgIsDefault = OpacityManager.IsDefaultForegroundOpacity();
    var cellBgIsDefault = OpacityManager.IsDefaultCellBackgroundOpacity();

    var bgStatusText = bgIsDefault ? "Default (100%)" : $"{bgOpacity:F2} ({currentBgOpacityPercent}%)";
    var fgStatusText = fgIsDefault ? "Default (100%)" : $"{fgOpacity:F2} ({currentFgOpacityPercent}%)";
    var cellBgStatusText = cellBgIsDefault ? "Default (100%)" : $"{cellBgOpacity:F2} ({currentCellBgOpacityPercent}%)";

    ImGui.Text($"Window Background: {bgStatusText}");
    ImGui.Text($"Cell Background: {cellBgStatusText}");
    ImGui.Text($"Foreground: {fgStatusText}");
  }
}
