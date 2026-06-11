using System.Reflection;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Logging;

namespace purrTTY.Display.Rendering;

/// <summary>
/// Loads the bundled terminal fonts (TerminalFonts/*.iamttf beside the
/// assemblies) into the ImGui atlas and maps font-family display names to the
/// per-variant font names a window resolves at draw time.
/// </summary>
public static class PurrTTYFontManager
{
    private static readonly Dictionary<string, ImFontPtr> s_loadedFonts = new();
    private static readonly Dictionary<string, FontFamilyDefinition> s_fontRegistry = new();
    private static IReadOnlyList<string>? s_familyNames;
    private static bool s_fontsLoaded;
    private static bool s_registryInitialized;

    /// <summary>Fonts loaded into the atlas, keyed by font name (e.g. "HackNerdFontMono-Regular").</summary>
    public static IReadOnlyDictionary<string, ImFontPtr> LoadedFonts => s_loadedFonts;

    static PurrTTYFontManager()
    {
        InitializeFontRegistry();
    }

    /// <summary>
    /// Loads every bundled .iamttf into the ImGui font atlas. Must run during
    /// atlas build (BRUTAL game-mod font loading pattern); idempotent.
    /// </summary>
    public static void LoadFonts()
    {
        if (s_fontsLoaded)
        {
            return;
        }

        try
        {
            string? dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(dllDir))
            {
                return;
            }

            string fontsDir = Path.Combine(dllDir, "TerminalFonts");
            if (!Directory.Exists(fontsDir))
            {
                ModLog.Log.Error($"PurrTTYFontManager: fonts directory not found at: {fontsDir}");
                return;
            }

            string[] fontFiles = Directory.GetFiles(fontsDir, "*.iamttf");
            if (fontFiles.Length == 0)
            {
                ModLog.Log.Error($"PurrTTYFontManager: no font files found in {fontsDir}");
                return;
            }

            ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
            foreach (string fontPath in fontFiles)
            {
                string fontName = Path.GetFileNameWithoutExtension(fontPath);
                ImFontPtr font = atlas.AddFontFromFileTTF(new ImString(fontPath), 32.0f);
                s_loadedFonts[fontName] = font;

                try
                {
                    FontManager.Fonts[fontName] = font;
                }
                catch (Exception ex)
                {
                    ModLog.Log.Error($"PurrTTYFontManager: could not add font '{fontName}' to FontManager: {ex.Message}");
                }
            }

            ModLog.Log.Info(
                $"PurrTTYFontManager: loaded {s_loadedFonts.Count} fonts - {string.Join(", ", s_loadedFonts.Keys)}");
            s_fontsLoaded = true;
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"PurrTTYFontManager: error loading fonts: {ex.Message}");
        }
    }

    private static void InitializeFontRegistry()
    {
        if (s_registryInitialized)
        {
            return;
        }

        // Families with all 4 variants.
        RegisterFontFamily("Jet Brains Mono", "JetBrainsMonoNerdFontMono",
            hasBold: true, hasItalic: true, hasBoldItalic: true);
        RegisterFontFamily("Space Mono", "SpaceMonoNerdFontMono",
            hasBold: true, hasItalic: true, hasBoldItalic: true);
        RegisterFontFamily("Hack", "HackNerdFontMono",
            hasBold: true, hasItalic: true, hasBoldItalic: true);

        // Regular-only families (bold/italic fall back to regular).
        RegisterFontFamily("Pro Font", "ProFontWindowsNerdFontMono");
        RegisterFontFamily("Proggy Clean", "ProggyCleanNerdFontMono");
        RegisterFontFamily("Shure Tech Mono", "ShureTechMonoNerdFontMono");
        RegisterFontFamily("Departure Mono", "DepartureMonoNerdFont");

        s_registryInitialized = true;
    }

    private static void RegisterFontFamily(
        string displayName,
        string fontBaseName,
        bool hasBold = false,
        bool hasItalic = false,
        bool hasBoldItalic = false)
    {
        s_fontRegistry[displayName] = new FontFamilyDefinition
        {
            DisplayName = displayName,
            FontBaseName = fontBaseName,
            HasRegular = true,
            HasBold = hasBold,
            HasItalic = hasItalic,
            HasBoldItalic = hasBoldItalic,
        };
    }

    /// <summary>
    /// Display names of the registered font families. Cached: this is read from
    /// the menu draw path every frame the Font menu is open.
    /// </summary>
    public static IReadOnlyList<string> GetAvailableFontFamilies()
        => s_familyNames ??= s_fontRegistry.Keys.ToList().AsReadOnly();

    /// <summary>
    /// Maps a font-family display name to its per-variant font names, falling
    /// back to the Regular variant for styles the family does not provide (and
    /// to the default family when the name is unknown).
    /// </summary>
    public static TerminalFontConfig CreateFontConfigForFamily(string displayName, float fontSize = 32.0f)
    {
        if (displayName is null || !s_fontRegistry.TryGetValue(displayName, out var definition))
        {
            return TerminalFontConfig.CreateDefault();
        }

        var regularFontName = $"{definition.FontBaseName}-Regular";
        return new TerminalFontConfig
        {
            RegularFontName = regularFontName,
            BoldFontName = definition.HasBold ? $"{definition.FontBaseName}-Bold" : regularFontName,
            ItalicFontName = definition.HasItalic ? $"{definition.FontBaseName}-Italic" : regularFontName,
            BoldItalicFontName = definition.HasBoldItalic ? $"{definition.FontBaseName}-BoldItalic" : regularFontName,
            FontSize = fontSize,
        };
    }
}
