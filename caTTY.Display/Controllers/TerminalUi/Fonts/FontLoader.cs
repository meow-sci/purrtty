using System;
using System.Reflection;
using Brutal.ImGuiApi;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using KSA;

namespace caTTY.Display.Controllers.TerminalUi.Fonts;

/// <summary>
///     Handles font loading from various sources (FontManager, CaTTYFontManager, GameMod reflection).
/// </summary>
internal class FontLoader
{
  private readonly TerminalFontConfig _fontConfig;

  // Font pointers for different styles
  private ImFontPtr _regularFont;
  private ImFontPtr _boldFont;
  private ImFontPtr _italicFont;
  private ImFontPtr _boldItalicFont;

  public FontLoader(TerminalFontConfig fontConfig)
  {
    _fontConfig = fontConfig ?? throw new ArgumentNullException(nameof(fontConfig));
  }

  /// <summary>
  ///     Gets the loaded regular font.
  /// </summary>
  public ImFontPtr RegularFont => _regularFont;

  /// <summary>
  ///     Gets the loaded bold font.
  /// </summary>
  public ImFontPtr BoldFont => _boldFont;

  /// <summary>
  ///     Gets the loaded italic font.
  /// </summary>
  public ImFontPtr ItalicFont => _italicFont;

  /// <summary>
  ///     Gets the loaded bold+italic font.
  /// </summary>
  public ImFontPtr BoldItalicFont => _boldItalicFont;

  /// <summary>
  ///     Loads fonts from the ImGui font system by name.
  /// </summary>
  public void LoadFonts()
  {
    try
    {
      Console.WriteLine($"FontLoader: Loading fonts with config - Regular: {_fontConfig.RegularFontName}, Size: {_fontConfig.FontSize}");

      // Try to find fonts by name, fall back to default if not found
      var defaultFont = ImGui.GetFont();

      var regularFont = FindFont(_fontConfig.RegularFontName);
      _regularFont = regularFont.HasValue ? regularFont.Value : defaultFont;
      Console.WriteLine($"FontLoader: Regular font loaded: {(regularFont.HasValue ? "Success" : "Fallback to default")}");

      var boldFont = FindFont(_fontConfig.BoldFontName);
      _boldFont = boldFont.HasValue ? boldFont.Value : _regularFont;

      var italicFont = FindFont(_fontConfig.ItalicFontName);
      _italicFont = italicFont.HasValue ? italicFont.Value : _regularFont;

      var boldItalicFont = FindFont(_fontConfig.BoldItalicFontName);
      _boldItalicFont = boldItalicFont.HasValue ? boldItalicFont.Value : _regularFont;

      Console.WriteLine("FontLoader: Fonts loaded successfully");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: Error loading fonts: {ex.Message}");

      // Fallback to default font for all styles
      var defaultFont = ImGui.GetFont();
      _regularFont = defaultFont;
      _boldFont = defaultFont;
      _italicFont = defaultFont;
      _boldItalicFont = defaultFont;
    }
  }

  /// <summary>
  ///     Finds a font by name in the ImGui font atlas.
  /// </summary>
  /// <param name="fontName">The name of the font to find</param>
  /// <returns>The font pointer if found, null otherwise</returns>
  private ImFontPtr? FindFont(string fontName)
  {
    if (string.IsNullOrWhiteSpace(fontName))
    {
      return null;
    }

    try
    {
      // First try the standard FontManager (works in standalone apps)
      if (FontManager.Fonts.TryGetValue(fontName, out ImFontPtr fontPtr))
      {
        return fontPtr;
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: FontManager.Fonts not available for '{fontName}': {ex.Message}");
    }

    try
    {
      // Try the GameMod's font loading system (works in game mod context)
      var gameModType = Type.GetType("caTTY.GameMod.TerminalMod, caTTY");
      if (gameModType != null)
      {
        MethodInfo? getFontMethod = gameModType.GetMethod("GetFont", BindingFlags.Public | BindingFlags.Static);
        if (getFontMethod != null)
        {
          object? result = getFontMethod.Invoke(null, new object[] { fontName });
          if (result is ImFontPtr font)
          {
            return font;
          }
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: GameMod font loading failed for '{fontName}': {ex.Message}");
    }

    // Try to iterate through ImGui font atlas (fallback method)
    try
    {
      var io = ImGui.GetIO();
      var fonts = io.Fonts;

      // This is a simplified approach - in a real implementation,
      // we would need to iterate through the font atlas and match names
      // For now, return null to indicate font not found
      Console.WriteLine($"FontLoader: Font '{fontName}' not found in ImGui font atlas");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: Error searching ImGui font atlas for '{fontName}': {ex.Message}");
    }

    return null;
  }

  /// <summary>
  ///     Pushes a UI font for menu rendering.
  /// </summary>
  public void PushUIFont(out bool fontUsed)
  {
    try
    {
      // Always use Hack Regular at 32.0f for UI elements
      if (CaTTYFontManager.LoadedFonts.TryGetValue("HackNerdFontMono-Regular", out ImFontPtr hackFont))
      {
        ImGui.PushFont(hackFont, 32.0f);
        fontUsed = true;
        return;
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: Error pushing UI font from CaTTYFontManager: {ex.Message}");
    }

    // Fallback: Try FontManager (works in standalone apps)
    try
    {
      if (FontManager.Fonts.TryGetValue("HackNerdFontMono-Regular", out ImFontPtr fontPtr))
      {
        ImGui.PushFont(fontPtr, 32.0f);
        fontUsed = true;
        return;
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: FontManager.Fonts not available for UI font: {ex.Message}");
    }

    // Try the GameMod's font loading system (works in game mod context)
    try
    {
      var gameModType = Type.GetType("caTTY.GameMod.TerminalMod, caTTY");
      if (gameModType != null)
      {
        MethodInfo? getFontMethod = gameModType.GetMethod("GetFont", BindingFlags.Public | BindingFlags.Static);
        if (getFontMethod != null)
        {
          object? result = getFontMethod.Invoke(null, new object[] { "HackNerdFontMono-Regular" });
          if (result is ImFontPtr font)
          {
            ImGui.PushFont(font, 32.0f);
            fontUsed = true;
            return;
          }
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: GameMod font loading failed for UI font: {ex.Message}");
    }

    fontUsed = false;
  }

  /// <summary>
  ///     Pushes a monospace font for terminal content rendering.
  /// </summary>
  public void PushTerminalContentFont(out bool fontUsed)
  {
    try
    {
      // Use the regular font from our font configuration
      ImGui.PushFont(_regularFont, _fontConfig.FontSize);
      fontUsed = true;
      return;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"FontLoader: Error pushing configured font: {ex.Message}");
    }

    // Fallback: First try the standard FontManager (works in standalone apps)
    try
    {
      if (FontManager.Fonts.TryGetValue(_fontConfig.RegularFontName, out ImFontPtr fontPtr))
      {
        ImGui.PushFont(fontPtr, _fontConfig.FontSize);
        fontUsed = true;
        return;
      }
    }
    catch (Exception ex)
    {
      // FontManager.Fonts may not be available in game mod context
      Console.WriteLine($"FontLoader: FontManager.Fonts not available: {ex.Message}");
    }

    // Try the GameMod's font loading system (works in game mod context)
    try
    {
      // Use reflection to call the GameMod's GetFont method
      var gameModType = Type.GetType("caTTY.GameMod.TerminalMod, caTTY");
      if (gameModType != null)
      {
        MethodInfo? getFontMethod = gameModType.GetMethod("GetFont", BindingFlags.Public | BindingFlags.Static);
        if (getFontMethod != null)
        {
          object? result = getFontMethod.Invoke(null, new object[] { _fontConfig.RegularFontName });
          if (result is ImFontPtr font)
          {
            ImGui.PushFont(font, _fontConfig.FontSize);
            fontUsed = true;
            return;
          }
        }
      }
    }
    catch (Exception ex)
    {
      // GameMod font loading not available or failed
      Console.WriteLine($"FontLoader: GameMod font loading failed: {ex.Message}");
    }

    fontUsed = false;
  }
}
