using System.Reflection;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.Core;
using purrTTY.Display.Configuration;
using purrTTY.Logging;

namespace purrTTY.Display.Rendering;

/// <summary>
///     Manages fonts used in the purrTTY terminal.
/// </summary>
public class PurrTTYFontManager
{
  public static Dictionary<string, ImFontPtr> LoadedFonts = new();
  private static readonly Dictionary<string, FontFamilyDefinition> _fontRegistry = new();
  private static bool _fontsLoaded;
  private static bool _registryInitialized;

  /// <summary>
  /// Initializes the font registry with hardcoded font family definitions.
  /// Called automatically during font loading process.
  /// </summary>
  static PurrTTYFontManager()
  {
    // Ensure the registry is initialized as soon as the type is first loaded/used.
    InitializeFontRegistry();
  }

  /// <summary>
  ///     Loads fonts explicitly for the game mod.
  ///     Based on BRUTAL ImGui font loading pattern for game mods.
  /// </summary>
  public static void LoadFonts()
  {
    if (_fontsLoaded)
    {
      return;
    }

    try
    {
      // Get the directory where the mod DLL is located
      string? dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

      if (!string.IsNullOrEmpty(dllDir))
      {
        string fontsDir = Path.Combine(dllDir, "TerminalFonts");

        if (Directory.Exists(fontsDir))
        {
          // Get all .iamttf from TerminalFonts folder
          string[] fontFiles = Directory.GetFiles(fontsDir, "*.iamttf");

          if (fontFiles.Length > 0)
          {
            ImGuiIOPtr io = ImGui.GetIO();
            ImFontAtlasPtr atlas = io.Fonts;

            for (int i = 0; i < fontFiles.Length; i++)
            {
              string fontPath = fontFiles[i];
              string fontName = Path.GetFileNameWithoutExtension(fontPath);

              ModLog.Log.Debug($"PurrTTYFontManager: Loading font: {fontPath}");


              if (File.Exists(fontPath))
              {
                // Use a reasonable default font size (32pt)
                float fontSize = 32.0f;
                var fontPathStr = new ImString(fontPath);

                bool fontPixelSnap = GameSettings.GetFontPixelSnap();
                float fontDensity = GameSettings.GetFontDensity() / 100f;

                                ImFontPtr font = atlas.AddFontFromFileTTF(fontPathStr, fontSize);
                LoadedFonts[fontName] = font;

                // Add to FontManager.Fonts dictionary if possible
                try
                {
                    FontManager.Fonts[fontName] = font;
                    ModLog.Log.Debug($"TestApp: Added font '{fontName}' to FontManager");
                }
                catch (Exception ex)
                {
                    ModLog.Log.Error($"TestApp: Could not add font to FontManager: {ex.Message}");
                }

                ModLog.Log.Info($"PurrTTYFontManager: Loaded font '{fontName}' from {fontPath}");
              }
            }

            ModLog.Log.Info(
                $"PurrTTYFontManager: Loaded {LoadedFonts.Count} fonts - {string.Join(", ", LoadedFonts.Keys)}");
          }
          else
          {
            ModLog.Log.Error("PurrTTYFontManager: No font files found in Fonts folder");
          }
        }
        else
        {
          ModLog.Log.Error($"PurrTTYFontManager: Fonts directory not found at: {fontsDir}");
        }
      }

      // Initialize font registry after loading fonts
      InitializeFontRegistry();

      _fontsLoaded = true;
    }
    catch (Exception ex)
    {
      ModLog.Log.Error($"PurrTTYFontManager: Error loading fonts: {ex.Message}");
    }
  }

  private static void InitializeFontRegistry()
  {
    if (_registryInitialized) return;

    ModLog.Log.Debug("PurrTTYFontManager: Initializing font registry...");

    // Register fonts with all 4 variants
    RegisterFontFamily("Jet Brains Mono", "JetBrainsMonoNerdFontMono", 
      hasRegular: true, hasBold: true, hasItalic: true, hasBoldItalic: true);
    RegisterFontFamily("Space Mono", "SpaceMonoNerdFontMono", 
      hasRegular: true, hasBold: true, hasItalic: true, hasBoldItalic: true);
    RegisterFontFamily("Hack", "HackNerdFontMono", 
      hasRegular: true, hasBold: true, hasItalic: true, hasBoldItalic: true);

    // Register fonts with Regular variant only
    RegisterFontFamily("Pro Font", "ProFontWindowsNerdFontMono", 
      hasRegular: true, hasBold: false, hasItalic: false, hasBoldItalic: false);
    RegisterFontFamily("Proggy Clean", "ProggyCleanNerdFontMono", 
      hasRegular: true, hasBold: false, hasItalic: false, hasBoldItalic: false);
    RegisterFontFamily("Shure Tech Mono", "ShureTechMonoNerdFontMono", 
      hasRegular: true, hasBold: false, hasItalic: false, hasBoldItalic: false);
    RegisterFontFamily("Departure Mono", "DepartureMonoNerdFont", 
      hasRegular: true, hasBold: false, hasItalic: false, hasBoldItalic: false);

    ModLog.Log.Info($"PurrTTYFontManager: Font registry initialized with {_fontRegistry.Count} font families");
    _registryInitialized = true;
  }

  /// <summary>
  /// Registers a font family in the registry with its display name and variant availability.
  /// </summary>
  /// <param name="displayName">User-friendly display name for the font family.</param>
  /// <param name="fontBaseName">Base name used for font file naming.</param>
  /// <param name="hasRegular">Whether the Regular variant is available.</param>
  /// <param name="hasBold">Whether the Bold variant is available.</param>
  /// <param name="hasItalic">Whether the Italic variant is available.</param>
  /// <param name="hasBoldItalic">Whether the BoldItalic variant is available.</param>
  private static void RegisterFontFamily(string displayName, string fontBaseName, 
    bool hasRegular = true, bool hasBold = false, bool hasItalic = false, bool hasBoldItalic = false)
  {
    var definition = new FontFamilyDefinition
    {
      DisplayName = displayName,
      FontBaseName = fontBaseName,
      HasRegular = hasRegular,
      HasBold = hasBold,
      HasItalic = hasItalic,
      HasBoldItalic = hasBoldItalic
    };

    _fontRegistry[displayName] = definition;
    // ModLog.Log.Debug($"PurrTTYFontManager: Registered font family: {displayName} -> {fontBaseName}");
  }

  /// <summary>
  /// Gets a read-only list of available font family display names.
  /// </summary>
  /// <returns>A read-only list of display names for all registered font families.</returns>
  public static IReadOnlyList<string> GetAvailableFontFamilies()
  {
    return _fontRegistry.Keys.ToList().AsReadOnly();
  }

  /// <summary>
  /// Gets the font family definition for the specified display name.
  /// </summary>
  /// <param name="displayName">The display name of the font family to look up.</param>
  /// <returns>The FontFamilyDefinition if found, null otherwise.</returns>
  public static FontFamilyDefinition? GetFontFamilyDefinition(string displayName)
  {
    if (displayName == null) return null;
    return _fontRegistry.TryGetValue(displayName, out var definition) ? definition : null;
  }

  /// <summary>
  /// Creates a TerminalFontConfig for the specified font family with variant fallback logic.
  /// If the font family has missing variants, falls back to Regular variant for those styles.
  /// </summary>
  /// <param name="displayName">The display name of the font family to create configuration for.</param>
  /// <param name="fontSize">The font size to use. Defaults to 32.0f.</param>
  /// <returns>A TerminalFontConfig configured for the specified font family, or default configuration if font family not found.</returns>
  public static TerminalFontConfig CreateFontConfigForFamily(string displayName, float fontSize = 32.0f)
  {
    // ModLog.Log.Debug($"PurrTTYFontManager: Creating font configuration for family: {displayName}, size: {fontSize}");

    var definition = GetFontFamilyDefinition(displayName);
    if (definition == null)
    {
      // ModLog.Log.Debug($"PurrTTYFontManager: Unknown font family '{displayName}', using default configuration");
      return TerminalFontConfig.CreateForTestApp();
    }

    // ModLog.Log.Debug($"PurrTTYFontManager: Found font family definition: {definition}");

    // Create font configuration with variant fallback logic
    var regularFontName = $"{definition.FontBaseName}-Regular";
    var boldFontName = definition.HasBold ? $"{definition.FontBaseName}-Bold" : regularFontName;
    var italicFontName = definition.HasItalic ? $"{definition.FontBaseName}-Italic" : regularFontName;
    var boldItalicFontName = definition.HasBoldItalic ? $"{definition.FontBaseName}-BoldItalic" : regularFontName;

    // ModLog.Log.Debug($"PurrTTYFontManager: Font configuration - Regular: {regularFontName}, Bold: {boldFontName}, Italic: {italicFontName}, BoldItalic: {boldItalicFontName}");

    var config = new TerminalFontConfig
    {
      RegularFontName = regularFontName,
      BoldFontName = boldFontName,
      ItalicFontName = italicFontName,
      BoldItalicFontName = boldItalicFontName,
      FontSize = fontSize,
      AutoDetectContext = false
    };

    // ModLog.Log.Debug($"PurrTTYFontManager: Successfully created font configuration for '{displayName}'");
    return config;
  }

  /// <summary>
  /// Determines the current font family display name from an existing TerminalFontConfig.
  /// Matches the RegularFontName against registered font base names to identify the family.
  /// </summary>
  /// <param name="currentConfig">The TerminalFontConfig to analyze.</param>
  /// <returns>The display name of the matching font family, or null if no match found.</returns>
  public static string? GetCurrentFontFamily(TerminalFontConfig currentConfig)
  {
    if (currentConfig == null)
    {
      // ModLog.Log.Debug("PurrTTYFontManager: GetCurrentFontFamily called with null config");
      return null;
    }

    // ModLog.Log.Debug($"PurrTTYFontManager: Detecting font family for RegularFontName: {currentConfig.RegularFontName}");

    // Find which font family matches the current configuration
    foreach (var kvp in _fontRegistry)
    {
      var definition = kvp.Value;
      var expectedRegular = $"{definition.FontBaseName}-Regular";

      if (currentConfig.RegularFontName == expectedRegular)
      {
        // ModLog.Log.Debug($"PurrTTYFontManager: Detected font family: {kvp.Key} (matched {expectedRegular})");
        return kvp.Key; // Return display name
      }
    }

    // ModLog.Log.Debug($"PurrTTYFontManager: No matching font family found for RegularFontName: {currentConfig.RegularFontName}");
    return null; // Current config doesn't match any registered family
  }
}
