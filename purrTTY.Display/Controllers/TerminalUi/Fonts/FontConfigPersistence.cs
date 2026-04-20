using System;
using purrTTY.Display.Configuration;
using purrTTY.Display.Rendering;
using purrTTY.Display.Utils;
using purrTTY.Logging;

namespace purrTTY.Display.Controllers.TerminalUi.Fonts;

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

      // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Loaded config FontFamily: '{config.FontFamily}', FontSize: {config.FontSize}");

      // Apply saved font family if available
      if (!string.IsNullOrEmpty(config.FontFamily))
      {
        try
        {
          // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Attempting to create font config for family: '{config.FontFamily}'");

          // Create font configuration manually since PurrTTYFontManager.CreateFontConfigForFamily is broken
          var savedFontConfig = PurrTTYFontManager.CreateFontConfigForFamily(config.FontFamily, config.FontSize ?? fontConfig.FontSize);

          if (savedFontConfig != null)
          {
            // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Successfully created font config");
            // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Regular: {savedFontConfig.RegularFontName}");
            // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Bold: {savedFontConfig.BoldFontName}");
            // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Size: {savedFontConfig.FontSize}");

            var oldRegular = fontConfig.RegularFontName;

            fontConfig = savedFontConfig;
            fontLoader = new FontLoader(fontConfig);
            // Metrics calculator doesn't need to be recreated (it uses immutable config reference)

            // Recreate family selector with new config and loaded family
            familySelector = new FontFamilySelector(fontConfig, config.FontFamily ?? familySelector.CurrentFontFamily);

            // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Font config updated from '{oldRegular}' to '{fontConfig.RegularFontName}'");
            // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Current font family set to: '{familySelector.CurrentFontFamily}'");
          }
          else
          {
            // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Could not create font config for '{config.FontFamily}', keeping default");
          }
        }
        catch (Exception ex)
        {
          ModLog.Log.Debug($"FontConfigPersistence: Constructor - FAILED to load saved font family '{config.FontFamily}': {ex.Message}");

          // Keep current font configuration on error
        }
      }
      else
      {
        // ModLog.Log.Debug("FontConfigPersistence: Constructor - No saved font family found in config");
      }

      // Apply saved font size if available
      if (config.FontSize.HasValue)
      {
        var fontSize = Math.Max(LayoutConstants.MIN_FONT_SIZE, Math.Min(LayoutConstants.MAX_FONT_SIZE, config.FontSize.Value));
        var oldSize = fontConfig.FontSize;
        fontConfig.FontSize = fontSize;
        // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Font size updated from {oldSize} to {fontSize}");
      }
      else
      {
        // ModLog.Log.Debug("FontConfigPersistence: Constructor - No saved font size found in config");
      }

      // ModLog.Log.Debug($"FontConfigPersistence: Constructor - Final font config: Regular='{fontConfig.RegularFontName}', Size={fontConfig.FontSize}");
    }
    catch (Exception ex)
    {
      ModLog.Log.Debug($"FontConfigPersistence: Constructor - ERROR loading font settings: {ex.Message}");
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
          var savedFontConfig = PurrTTYFontManager.CreateFontConfigForFamily(config.FontFamily, config.FontSize ?? fontConfig.FontSize);

          // Log what we're trying to load vs what we got
          ModLog.Log.Debug($"FontConfigPersistence: Attempting to load font family '{config.FontFamily}'");
          ModLog.Log.Debug($"FontConfigPersistence: Created font config - Regular: {savedFontConfig.RegularFontName}");

          fontConfig = savedFontConfig;
          fontLoader = new FontLoader(fontConfig);
          // Metrics calculator doesn't need to be recreated (it uses immutable config reference)

          // Recreate family selector with new config and loaded family
          familySelector = new FontFamilySelector(fontConfig, config.FontFamily ?? familySelector.CurrentFontFamily);
          fontConfigChanged = true;
          ModLog.Log.Debug($"FontConfigPersistence: Successfully loaded font family from settings: {config.FontFamily}");
        }
        catch (Exception ex)
        {
          ModLog.Log.Debug($"FontConfigPersistence: Failed to load saved font family '{config.FontFamily}': {ex.Message}");
          // Keep current font configuration on error
        }
      }
      else
      {
        ModLog.Log.Debug("FontConfigPersistence: No saved font family found, using default");
      }

      // Apply saved font size if available
      if (config.FontSize.HasValue)
      {
        var fontSize = Math.Max(LayoutConstants.MIN_FONT_SIZE, Math.Min(LayoutConstants.MAX_FONT_SIZE, config.FontSize.Value));
        if (Math.Abs(fontConfig.FontSize - fontSize) > 0.1f)
        {
          fontConfig.FontSize = fontSize;
          fontConfigChanged = true;
          ModLog.Log.Debug($"FontConfigPersistence: Loaded font size from settings: {fontSize}");
        }
      }
      else
      {
        ModLog.Log.Debug("FontConfigPersistence: No saved font size found, using default");
      }

      // Log final configuration if changed
      if (fontConfigChanged)
      {
        ModLog.Log.Debug("FontConfigPersistence: Font configuration changed, fonts will be reloaded on next render");
        ModLog.Log.Debug($"FontConfigPersistence: Final font config after loading - Regular: {fontConfig.RegularFontName}, Size: {fontConfig.FontSize}");
      }
    }
    catch (Exception ex)
    {
      ModLog.Log.Debug($"FontConfigPersistence: Error loading font settings: {ex.Message}");
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

      ModLog.Log.Debug($"FontConfigPersistence: Saved font settings - Family: {familySelector.CurrentFontFamily}, Size: {fontConfig.FontSize}");
    }
    catch (Exception ex)
    {
      ModLog.Log.Debug($"FontConfigPersistence: Error saving font settings: {ex.Message}");
    }
  }
}
