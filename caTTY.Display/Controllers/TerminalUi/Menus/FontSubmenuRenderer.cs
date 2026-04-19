using System;
using Brutal.ImGuiApi;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Font submenu with font selection and size adjustment.
/// Provides menu items for font family selection and font size slider.
/// </summary>
internal class FontSubmenuRenderer
{
  private readonly TerminalUiFonts _fonts;
  private readonly SessionManager _sessionManager;
  private readonly Action _triggerTerminalResizeForAllSessions;

  public FontSubmenuRenderer(
    TerminalUiFonts fonts,
    SessionManager sessionManager,
    Action triggerTerminalResizeForAllSessions)
  {
    _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    _triggerTerminalResizeForAllSessions = triggerTerminalResizeForAllSessions ?? throw new ArgumentNullException(nameof(triggerTerminalResizeForAllSessions));
  }

  /// <summary>
  /// Renders the Font submenu content with font size slider and font family selection options.
  /// Note: Parent menu handles BeginMenu/EndMenu calls.
  /// </summary>
  public void RenderContent()
  {
    // Font Size Slider
    int currentFontSize = (int)_fonts.CurrentFontConfig.FontSize;
    ImGui.Text("Font Size:");
    ImGui.SameLine();
    if (ImGui.SliderInt("##FontSize", ref currentFontSize, 4, 72))
    {
      SetFontSize((float)currentFontSize);
    }

    ImGui.Separator();

    // Font Family Selection
    var availableFonts = CaTTYFontManager.GetAvailableFontFamilies();

    foreach (var fontFamily in availableFonts)
    {
      bool isSelected = fontFamily == _fonts.CurrentFontFamily;

      if (ImGui.MenuItem(fontFamily, "", isSelected))
      {
        SelectFontFamily(fontFamily);
      }
    }
  }

  /// <summary>
  /// Selects a font family and applies it to the terminal.
  /// </summary>
  /// <param name="displayName">The display name of the font family to select</param>
  private void SelectFontFamily(string displayName)
  {
    _fonts.SelectFontFamily(displayName, () =>
    {
      // Callback when font configuration changes
      _sessionManager.ApplyFontConfigToAllSessions(_fonts.CurrentFontConfig);
      _triggerTerminalResizeForAllSessions();
    });
    _fonts.SaveFontSettings();
  }

  /// <summary>
  /// Sets the font size to the specified value.
  /// </summary>
  /// <param name="fontSize">The new font size to set</param>
  private void SetFontSize(float fontSize)
  {
    try
    {
      // Clamp the font size to valid range
      fontSize = Math.Max(LayoutConstants.MIN_FONT_SIZE, Math.Min(LayoutConstants.MAX_FONT_SIZE, fontSize));

      var currentConfig = _fonts.CurrentFontConfig;
      var newFontConfig = new TerminalFontConfig
      {
        FontSize = fontSize,
        RegularFontName = currentConfig.RegularFontName,
        BoldFontName = currentConfig.BoldFontName,
        ItalicFontName = currentConfig.ItalicFontName,
        BoldItalicFontName = currentConfig.BoldItalicFontName,
        AutoDetectContext = currentConfig.AutoDetectContext
      };

      // Update font configuration with callback to apply to all sessions
      _fonts.UpdateFontConfig(newFontConfig, () =>
      {
        // Callback when font configuration changes
        _sessionManager.ApplyFontConfigToAllSessions(_fonts.CurrentFontConfig);
        _triggerTerminalResizeForAllSessions();
      });

      // Save font settings to persistent configuration
      _fonts.SaveFontSettings();

      Console.WriteLine($"FontMenuRenderer: Font size set to {fontSize}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontMenuRenderer: Error setting font size: {ex.Message}");
    }
  }
}
