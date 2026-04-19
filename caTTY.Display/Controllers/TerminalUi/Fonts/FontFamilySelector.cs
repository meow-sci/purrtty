using System;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;

namespace caTTY.Display.Controllers.TerminalUi.Fonts;

/// <summary>
///     Handles font family selection and state management for the terminal UI.
///     Manages the current font family, initialization, and selection operations.
/// </summary>
internal class FontFamilySelector
{
  private readonly TerminalFontConfig _fontConfig;
  private string _currentFontFamily;

  public FontFamilySelector(TerminalFontConfig fontConfig, string currentFontFamily)
  {
    _fontConfig = fontConfig ?? throw new ArgumentNullException(nameof(fontConfig));
    _currentFontFamily = currentFontFamily ?? throw new ArgumentNullException(nameof(currentFontFamily));
  }

  /// <summary>
  ///     Gets the current font family.
  /// </summary>
  public string CurrentFontFamily => _currentFontFamily;

  /// <summary>
  ///     Initializes the current font family from the font configuration.
  /// </summary>
  public void InitializeCurrentFontFamily()
  {
    try
    {
      // Font settings were already loaded in LoadFontSettingsInConstructor()
      // Just ensure _currentFontFamily is set correctly
      if (string.IsNullOrEmpty(_currentFontFamily))
      {
        // Determine current font family from configuration using CaTTYFontManager
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(_fontConfig);
        _currentFontFamily = detectedFamily ?? "Hack"; // Default fallback
        // Console.WriteLine($"FontFamilySelector: Detected font family from config: {_currentFontFamily}");
      }
      else
      {
        // Console.WriteLine($"FontFamilySelector: Using font family from constructor loading: {_currentFontFamily}");
      }

      // Console.WriteLine($"FontFamilySelector: Final initialization - Font family: {_currentFontFamily}, Regular font: {_fontConfig.RegularFontName}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontFamilySelector: Error initializing current font family: {ex.Message}");
      _currentFontFamily = "Hack"; // Safe fallback
    }
  }

  /// <summary>
  ///     Creates a new font configuration for the selected font family.
  /// </summary>
  /// <param name="displayName">The display name of the font family to select</param>
  /// <param name="fontSize">The font size to use for the new configuration</param>
  /// <returns>A new TerminalFontConfig for the selected family</returns>
  /// <exception cref="Exception">Thrown if the font family cannot be created</exception>
  public TerminalFontConfig CreateFontConfigForFamily(string displayName, float fontSize)
  {
    Console.WriteLine($"FontFamilySelector: Selecting font family: {displayName}");

    // Create new font configuration for the selected family
    var newFontConfig = CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize);

    // Validate the new configuration
    newFontConfig.Validate();

    return newFontConfig;
  }

  /// <summary>
  ///     Updates the current font family after successful font configuration change.
  /// </summary>
  /// <param name="displayName">The display name of the newly selected font family</param>
  public void UpdateCurrentFontFamily(string displayName)
  {
    _currentFontFamily = displayName;
    Console.WriteLine($"FontFamilySelector: Successfully switched to font family: {displayName}");
  }

  /// <summary>
  ///     Loads the font family from persistent configuration.
  /// </summary>
  /// <param name="fontFamily">The font family loaded from configuration</param>
  /// <returns>True if a font family was loaded, false otherwise</returns>
  public bool LoadFontFamilyFromConfig(string? fontFamily)
  {
    if (!string.IsNullOrEmpty(fontFamily))
    {
      _currentFontFamily = fontFamily;
      Console.WriteLine($"FontFamilySelector: Loaded font family from settings: {fontFamily}");
      return true;
    }

    return false;
  }

  /// <summary>
  ///     Gets the current font family for saving to configuration.
  /// </summary>
  /// <returns>The current font family display name</returns>
  public string GetFontFamilyForSaving()
  {
    return _currentFontFamily;
  }
}
