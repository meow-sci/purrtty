using System;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using caTTY.Display.Utils;

namespace caTTY.Display.Controllers.TerminalUi.Fonts;

/// <summary>
///     Handles persistence of font configuration to/from disk.
/// </summary>
internal class FontConfigPersistence
{
  /// <summary>
  ///     Loads font settings from persistent configuration during initialization.
  /// </summary>
  /// <param name="fontConfig">Current font configuration to update</param>
  /// <param name="fontLoader">Font loader to recreate with new config</param>
  /// <param name="familySelector">Font family selector to recreate with new config</param>
  /// <returns>Tuple of (updated font config, updated font loader, updated family selector)</returns>
  public (TerminalFontConfig fontConfig, FontLoader fontLoader, FontFamilySelector familySelector) LoadFontSettingsInConstructor(
    TerminalFontConfig fontConfig,
    FontLoader fontLoader,
    FontFamilySelector familySelector)
  {
    try
    {
      var config = ThemeConfiguration.Load();

      // Console.WriteLine($"FontConfigPersistence: Constructor - Loaded config FontFamily: '{config.FontFamily}', FontSize: {config.FontSize}");

      // Apply saved font family if available
      if (!string.IsNullOrEmpty(config.FontFamily))
      {
        try
        {
          // Console.WriteLine($"FontConfigPersistence: Constructor - Attempting to create font config for family: '{config.FontFamily}'");

          // Create font configuration manually since CaTTYFontManager.CreateFontConfigForFamily is broken
          var savedFontConfig = CaTTYFontManager.CreateFontConfigForFamily(config.FontFamily, config.FontSize ?? fontConfig.FontSize);

          if (savedFontConfig != null)
          {
            // Console.WriteLine($"FontConfigPersistence: Constructor - Successfully created font config");
            // Console.WriteLine($"FontConfigPersistence: Constructor - Regular: {savedFontConfig.RegularFontName}");
            // Console.WriteLine($"FontConfigPersistence: Constructor - Bold: {savedFontConfig.BoldFontName}");
            // Console.WriteLine($"FontConfigPersistence: Constructor - Size: {savedFontConfig.FontSize}");

            var oldRegular = fontConfig.RegularFontName;

            fontConfig = savedFontConfig;
            fontLoader = new FontLoader(fontConfig);
            // Metrics calculator doesn't need to be recreated (it uses immutable config reference)

            // Recreate family selector with new config and loaded family
            familySelector = new FontFamilySelector(fontConfig, config.FontFamily ?? familySelector.CurrentFontFamily);

            // Console.WriteLine($"FontConfigPersistence: Constructor - Font config updated from '{oldRegular}' to '{fontConfig.RegularFontName}'");
            // Console.WriteLine($"FontConfigPersistence: Constructor - Current font family set to: '{familySelector.CurrentFontFamily}'");
          }
          else
          {
            // Console.WriteLine($"FontConfigPersistence: Constructor - Could not create font config for '{config.FontFamily}', keeping default");
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine($"FontConfigPersistence: Constructor - FAILED to load saved font family '{config.FontFamily}': {ex.Message}");

          // Keep current font configuration on error
        }
      }
      else
      {
        // Console.WriteLine("FontConfigPersistence: Constructor - No saved font family found in config");
      }

      // Apply saved font size if available
      if (config.FontSize.HasValue)
      {
        var fontSize = Math.Max(LayoutConstants.MIN_FONT_SIZE, Math.Min(LayoutConstants.MAX_FONT_SIZE, config.FontSize.Value));
        var oldSize = fontConfig.FontSize;
        fontConfig.FontSize = fontSize;
        // Console.WriteLine($"FontConfigPersistence: Constructor - Font size updated from {oldSize} to {fontSize}");
      }
      else
      {
        // Console.WriteLine("FontConfigPersistence: Constructor - No saved font size found in config");
      }

      // Console.WriteLine($"FontConfigPersistence: Constructor - Final font config: Regular='{fontConfig.RegularFontName}', Size={fontConfig.FontSize}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontConfigPersistence: Constructor - ERROR loading font settings: {ex.Message}");
    }

    return (fontConfig, fontLoader, familySelector);
  }

  /// <summary>
  ///     Loads font settings from persistent configuration.
  /// </summary>
  /// <param name="fontConfig">Current font configuration to update</param>
  /// <param name="fontLoader">Font loader to recreate with new config</param>
  /// <param name="familySelector">Font family selector to recreate with new config</param>
  /// <returns>Tuple of (updated font config, updated font loader, updated family selector, whether config changed)</returns>
  public (TerminalFontConfig fontConfig, FontLoader fontLoader, FontFamilySelector familySelector, bool fontConfigChanged) LoadFontSettings(
    TerminalFontConfig fontConfig,
    FontLoader fontLoader,
    FontFamilySelector familySelector)
  {
    bool fontConfigChanged = false;

    try
    {
      // Load fresh configuration from disk to get latest saved values
      var config = ThemeConfiguration.Load();

      // Apply saved font family if available
      if (!string.IsNullOrEmpty(config.FontFamily))
      {
        try
        {
          // Create font configuration for the saved family
          var savedFontConfig = CaTTYFontManager.CreateFontConfigForFamily(config.FontFamily, config.FontSize ?? fontConfig.FontSize);

          // Log what we're trying to load vs what we got
          Console.WriteLine($"FontConfigPersistence: Attempting to load font family '{config.FontFamily}'");
          Console.WriteLine($"FontConfigPersistence: Created font config - Regular: {savedFontConfig.RegularFontName}");

          fontConfig = savedFontConfig;
          fontLoader = new FontLoader(fontConfig);
          // Metrics calculator doesn't need to be recreated (it uses immutable config reference)

          // Recreate family selector with new config and loaded family
          familySelector = new FontFamilySelector(fontConfig, config.FontFamily ?? familySelector.CurrentFontFamily);
          fontConfigChanged = true;
          Console.WriteLine($"FontConfigPersistence: Successfully loaded font family from settings: {config.FontFamily}");
        }
        catch (Exception ex)
        {
          Console.WriteLine($"FontConfigPersistence: Failed to load saved font family '{config.FontFamily}': {ex.Message}");
          // Keep current font configuration on error
        }
      }
      else
      {
        Console.WriteLine("FontConfigPersistence: No saved font family found, using default");
      }

      // Apply saved font size if available
      if (config.FontSize.HasValue)
      {
        var fontSize = Math.Max(LayoutConstants.MIN_FONT_SIZE, Math.Min(LayoutConstants.MAX_FONT_SIZE, config.FontSize.Value));
        if (Math.Abs(fontConfig.FontSize - fontSize) > 0.1f)
        {
          fontConfig.FontSize = fontSize;
          fontConfigChanged = true;
          Console.WriteLine($"FontConfigPersistence: Loaded font size from settings: {fontSize}");
        }
      }
      else
      {
        Console.WriteLine("FontConfigPersistence: No saved font size found, using default");
      }

      // Log final configuration if changed
      if (fontConfigChanged)
      {
        Console.WriteLine("FontConfigPersistence: Font configuration changed, fonts will be reloaded on next render");
        Console.WriteLine($"FontConfigPersistence: Final font config after loading - Regular: {fontConfig.RegularFontName}, Size: {fontConfig.FontSize}");
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontConfigPersistence: Error loading font settings: {ex.Message}");
    }

    return (fontConfig, fontLoader, familySelector, fontConfigChanged);
  }

  /// <summary>
  ///     Saves current font settings to persistent configuration.
  /// </summary>
  /// <param name="familySelector">Font family selector containing current family</param>
  /// <param name="fontConfig">Current font configuration containing size</param>
  public void SaveFontSettings(FontFamilySelector familySelector, TerminalFontConfig fontConfig)
  {
    try
    {
      var config = ThemeConfiguration.Load();

      // Update font settings from family selector
      config.FontFamily = familySelector.GetFontFamilyForSaving();
      config.FontSize = fontConfig.FontSize;

      // Save configuration
      config.Save();

      Console.WriteLine($"FontConfigPersistence: Saved font settings - Family: {familySelector.CurrentFontFamily}, Size: {fontConfig.FontSize}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontConfigPersistence: Error saving font settings: {ex.Message}");
    }
  }
}
