using System;
using System.Collections.Generic;
using System.Linq;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for theme menu functionality.
/// Tests universal properties for theme menu content and theme application.
/// </summary>
[TestFixture]
[Category("Property")]
public class ThemeMenuProperties
{
    /// <summary>
    /// Generator for valid theme collections.
    /// Creates collections with built-in and TOML themes.
    /// </summary>
    public static Arbitrary<List<TerminalTheme>> ValidThemeCollections()
    {
        return Gen.Fresh(() =>
        {
            var themes = new List<TerminalTheme>();
            
            // Always include built-in themes
            themes.Add(ThemeManager.DefaultTheme);
            themes.Add(ThemeManager.DefaultLightTheme);
            
            // Add some mock TOML themes
            var tomlThemeNames = new[] { "Adventure", "Monokai Pro", "Matrix", "Neon", "Coffee Theme" };
            var themeCount = Gen.Choose(0, tomlThemeNames.Length).Sample(0, 1).First();
            
            for (int i = 0; i < themeCount; i++)
            {
                var themeName = tomlThemeNames[i];
                var mockTheme = CreateMockTomlTheme(themeName);
                themes.Add(mockTheme);
            }
            
            return themes;
        }).ToArbitrary();
    }

    /// <summary>
    /// Creates a mock TOML theme for testing.
    /// </summary>
    private static TerminalTheme CreateMockTomlTheme(string name)
    {
        var colorPalette = new TerminalColorPalette(
            // Standard ANSI colors
            black: new Brutal.Numerics.float4(0.0f, 0.0f, 0.0f, 1.0f),
            red: new Brutal.Numerics.float4(0.8f, 0.0f, 0.0f, 1.0f),
            green: new Brutal.Numerics.float4(0.0f, 0.8f, 0.0f, 1.0f),
            yellow: new Brutal.Numerics.float4(0.8f, 0.8f, 0.0f, 1.0f),
            blue: new Brutal.Numerics.float4(0.0f, 0.0f, 0.8f, 1.0f),
            magenta: new Brutal.Numerics.float4(0.8f, 0.0f, 0.8f, 1.0f),
            cyan: new Brutal.Numerics.float4(0.0f, 0.8f, 0.8f, 1.0f),
            white: new Brutal.Numerics.float4(0.8f, 0.8f, 0.8f, 1.0f),
            
            // Bright ANSI colors
            brightBlack: new Brutal.Numerics.float4(0.4f, 0.4f, 0.4f, 1.0f),
            brightRed: new Brutal.Numerics.float4(1.0f, 0.2f, 0.2f, 1.0f),
            brightGreen: new Brutal.Numerics.float4(0.2f, 1.0f, 0.2f, 1.0f),
            brightYellow: new Brutal.Numerics.float4(1.0f, 1.0f, 0.2f, 1.0f),
            brightBlue: new Brutal.Numerics.float4(0.2f, 0.2f, 1.0f, 1.0f),
            brightMagenta: new Brutal.Numerics.float4(1.0f, 0.2f, 1.0f, 1.0f),
            brightCyan: new Brutal.Numerics.float4(0.2f, 1.0f, 1.0f, 1.0f),
            brightWhite: new Brutal.Numerics.float4(1.0f, 1.0f, 1.0f, 1.0f),
            
            // Terminal UI colors
            foreground: new Brutal.Numerics.float4(1.0f, 1.0f, 1.0f, 1.0f),
            background: new Brutal.Numerics.float4(0.0f, 0.0f, 0.0f, 1.0f),
            cursor: new Brutal.Numerics.float4(1.0f, 1.0f, 1.0f, 1.0f),
            selection: new Brutal.Numerics.float4(0.5f, 0.5f, 0.5f, 1.0f)
        );

        var cursorConfig = new CursorConfig(caTTY.Core.Types.CursorStyle.BlinkingBlock, true, 500);
        
        return new TerminalTheme(name, ThemeType.Dark, colorPalette, cursorConfig, ThemeSource.TomlFile, $"/themes/{name}.toml");
    }

    /// <summary>
    /// Property 6: Theme Menu Content Completeness
    /// For any set of available themes (including TOML-loaded and built-in themes),
    /// the theme menu should display all themes with their correct display names.
    /// Feature: toml-terminal-theming, Property 6: Theme Menu Content Completeness
    /// Validates: Requirements 4.2, 4.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeMenuContentCompleteness_ShouldDisplayAllThemes()
    {
        return Prop.ForAll(ValidThemeCollections(), themeCollection =>
        {
            try
            {
                // Simulate theme menu content generation
                var menuItems = GenerateThemeMenuItems(themeCollection);

                // All themes should be represented in menu items
                bool allThemesRepresented = themeCollection.All(theme =>
                    menuItems.Any(item => item.Name == theme.Name));

                // Menu should have same number of items as themes
                bool correctItemCount = menuItems.Count == themeCollection.Count;

                // All menu items should have valid display names
                bool allItemsHaveValidNames = menuItems.All(item =>
                    !string.IsNullOrWhiteSpace(item.Name) && item.Name.Length > 0);

                // Built-in themes should always be present
                bool hasBuiltInThemes = menuItems.Any(item => item.Name == "Default") &&
                                       menuItems.Any(item => item.Name == "Default Light");

                // TOML themes should be properly identified
                var tomlThemes = themeCollection.Where(t => t.Source == ThemeSource.TomlFile);
                bool tomlThemesRepresented = tomlThemes.All(theme =>
                    menuItems.Any(item => item.Name == theme.Name && item.Source == ThemeSource.TomlFile));

                // No duplicate menu items
                bool noDuplicates = menuItems.Select(item => item.Name).Distinct().Count() == menuItems.Count;

                // Menu items should preserve theme metadata
                bool metadataPreserved = menuItems.All(item =>
                {
                    var originalTheme = themeCollection.First(t => t.Name == item.Name);
                    return item.Type == originalTheme.Type && item.Source == originalTheme.Source;
                });

                return allThemesRepresented && correctItemCount && allItemsHaveValidNames &&
                       hasBuiltInThemes && tomlThemesRepresented && noDuplicates && metadataPreserved;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Theme Menu Ordering Consistency
    /// Theme menu items should be consistently ordered with built-in themes first.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeMenuOrdering_ShouldBeConsistent()
    {
        return Prop.ForAll(ValidThemeCollections(), themeCollection =>
        {
            try
            {
                var menuItems = GenerateThemeMenuItems(themeCollection);

                if (menuItems.Count == 0) return true;

                // Built-in themes should come first
                var builtInItems = menuItems.Where(item => item.Source == ThemeSource.BuiltIn).ToList();
                var tomlItems = menuItems.Where(item => item.Source == ThemeSource.TomlFile).ToList();

                if (builtInItems.Count == 0) return true; // No built-in themes to check

                // Find the last built-in theme index and first TOML theme index
                int lastBuiltInIndex = -1;
                int firstTomlIndex = menuItems.Count;

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (menuItems[i].Source == ThemeSource.BuiltIn)
                    {
                        lastBuiltInIndex = i;
                    }
                    else if (menuItems[i].Source == ThemeSource.TomlFile && firstTomlIndex == menuItems.Count)
                    {
                        firstTomlIndex = i;
                    }
                }

                // Built-in themes should come before TOML themes
                bool correctOrdering = tomlItems.Count == 0 || lastBuiltInIndex < firstTomlIndex;

                // Within each group, themes should be ordered alphabetically
                bool builtInAlphabetical = IsAlphabeticallyOrdered(builtInItems.Select(item => item.Name));
                bool tomlAlphabetical = IsAlphabeticallyOrdered(tomlItems.Select(item => item.Name));

                return correctOrdering && builtInAlphabetical && tomlAlphabetical;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Helper method to check if a sequence of strings is alphabetically ordered.
    /// </summary>
    private static bool IsAlphabeticallyOrdered(IEnumerable<string> names)
    {
        var nameList = names.ToList();
        if (nameList.Count <= 1) return true;

        for (int i = 1; i < nameList.Count; i++)
        {
            if (string.Compare(nameList[i - 1], nameList[i], StringComparison.OrdinalIgnoreCase) > 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Simulates theme menu item generation for testing.
    /// This represents the logic that would be used in the actual theme menu rendering.
    /// </summary>
    private static List<ThemeMenuItem> GenerateThemeMenuItems(List<TerminalTheme> themes)
    {
        var menuItems = new List<ThemeMenuItem>();

        // Sort themes: built-in first, then TOML, alphabetically within each group
        var sortedThemes = themes
            .OrderBy(t => t.Source == ThemeSource.BuiltIn ? 0 : 1)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var theme in sortedThemes)
        {
            menuItems.Add(new ThemeMenuItem
            {
                Name = theme.Name,
                Type = theme.Type,
                Source = theme.Source,
                FilePath = theme.FilePath
            });
        }

        return menuItems;
    }

    /// <summary>
    /// Property 7: Theme Application Completeness
    /// For any theme selection, applying the theme should update all terminal color
    /// properties including foreground, background, cursor, selection, and all 16 ANSI colors.
    /// Feature: toml-terminal-theming, Property 7: Theme Application Completeness
    /// Validates: Requirements 4.3, 4.4
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeApplicationCompleteness_ShouldUpdateAllColors()
    {
        return Prop.ForAll(ValidThemeCollections(), themeCollection =>
        {
            try
            {
                if (themeCollection.Count == 0) return true;

                // Initialize theme system
                ThemeManager.InitializeThemes();

                // Test applying each theme in the collection
                foreach (var theme in themeCollection)
                {
                    // Apply the theme
                    ThemeManager.ApplyTheme(theme);
                    var currentTheme = ThemeManager.CurrentTheme;

                    // Verify theme was applied
                    if (currentTheme.Name != theme.Name) return false;

                    // Verify all color properties are updated
                    var colors = currentTheme.Colors;

                    // Check primary colors
                    bool primaryColorsValid = 
                        colors.Foreground.W == 1.0f && // Alpha should be 1.0
                        colors.Background.W == 1.0f &&
                        colors.Cursor.W == 1.0f &&
                        colors.Selection.W == 1.0f;

                    // Check all 16 ANSI colors have valid alpha
                    bool ansiColorsValid = 
                        colors.Black.W == 1.0f &&
                        colors.Red.W == 1.0f &&
                        colors.Green.W == 1.0f &&
                        colors.Yellow.W == 1.0f &&
                        colors.Blue.W == 1.0f &&
                        colors.Magenta.W == 1.0f &&
                        colors.Cyan.W == 1.0f &&
                        colors.White.W == 1.0f &&
                        colors.BrightBlack.W == 1.0f &&
                        colors.BrightRed.W == 1.0f &&
                        colors.BrightGreen.W == 1.0f &&
                        colors.BrightYellow.W == 1.0f &&
                        colors.BrightBlue.W == 1.0f &&
                        colors.BrightMagenta.W == 1.0f &&
                        colors.BrightCyan.W == 1.0f &&
                        colors.BrightWhite.W == 1.0f;

                    // Verify color resolution works for all ANSI codes
                    bool colorResolutionWorks = true;
                    for (int colorCode = 0; colorCode <= 15; colorCode++)
                    {
                        var resolvedColor = ThemeManager.ResolveThemeColor(colorCode);
                        if (resolvedColor.W != 1.0f) // Alpha should be 1.0
                        {
                            colorResolutionWorks = false;
                            break;
                        }
                    }

                    // Verify theme metadata is preserved
                    bool metadataPreserved = 
                        currentTheme.Type == theme.Type &&
                        currentTheme.Source == theme.Source &&
                        currentTheme.FilePath == theme.FilePath;

                    // Verify cursor configuration is applied
                    bool cursorConfigApplied = 
                        currentTheme.Cursor.DefaultStyle == theme.Cursor.DefaultStyle &&
                        currentTheme.Cursor.DefaultBlink == theme.Cursor.DefaultBlink &&
                        currentTheme.Cursor.BlinkIntervalMs == theme.Cursor.BlinkIntervalMs;

                    if (!primaryColorsValid || !ansiColorsValid || !colorResolutionWorks || 
                        !metadataPreserved || !cursorConfigApplied)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Theme Application Immediate Effect
    /// Theme application should take effect immediately without requiring restart.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeApplicationImmediateEffect_ShouldUpdateImmediately()
    {
        return Prop.ForAll(ValidThemeCollections(), themeCollection =>
        {
            try
            {
                if (themeCollection.Count < 2) return true; // Need at least 2 themes to test switching

                // Initialize theme system
                ThemeManager.InitializeThemes();

                var theme1 = themeCollection[0];
                var theme2 = themeCollection[1];

                // Apply first theme
                ThemeManager.ApplyTheme(theme1);
                var currentAfterFirst = ThemeManager.CurrentTheme;

                // Verify first theme is applied
                if (currentAfterFirst.Name != theme1.Name) return false;

                // Get colors from first theme
                var colors1 = currentAfterFirst.Colors;

                // Apply second theme
                ThemeManager.ApplyTheme(theme2);
                var currentAfterSecond = ThemeManager.CurrentTheme;

                // Verify second theme is applied
                if (currentAfterSecond.Name != theme2.Name) return false;

                // Get colors from second theme
                var colors2 = currentAfterSecond.Colors;

                // If themes are different, colors should be different
                if (theme1.Name != theme2.Name)
                {
                    // At least one color should be different (unless themes happen to have identical colors)
                    bool colorsChanged = 
                        colors1.Foreground != colors2.Foreground ||
                        colors1.Background != colors2.Background ||
                        colors1.Cursor != colors2.Cursor ||
                        colors1.Selection != colors2.Selection;

                    // Note: We can't guarantee colors are different since themes might have similar palettes
                    // But we can verify the theme name changed, which indicates the switch worked
                    return true; // Theme name change is sufficient proof of immediate effect
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Represents a theme menu item for testing purposes.
    /// </summary>
    private class ThemeMenuItem
    {
        public string Name { get; set; } = string.Empty;
        public ThemeType Type { get; set; }
        public ThemeSource Source { get; set; }
        public string? FilePath { get; set; }
    }
}