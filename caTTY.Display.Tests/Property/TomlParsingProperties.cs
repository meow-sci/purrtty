using System;
using System.IO;
using System.Linq;
using caTTY.Display.Rendering;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for TOML theme parsing consistency.
/// Tests universal properties that should hold across all valid TOML theme files.
/// </summary>
[TestFixture]
[Category("Property")]
public class TomlParsingProperties
{
    /// <summary>
    /// Generator for valid hex color strings.
    /// </summary>
    public static Arbitrary<string> ValidHexColors()
    {
        return Gen.Fresh(() =>
        {
            var r = Gen.Choose(0, 255).Sample(0, 1).First();
            var g = Gen.Choose(0, 255).Sample(0, 1).First();
            var b = Gen.Choose(0, 255).Sample(0, 1).First();
            return $"#{r:x2}{g:x2}{b:x2}";
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for valid TOML theme content with all required sections.
    /// </summary>
    public static Arbitrary<string> ValidTomlThemeContent()
    {
        return Gen.Fresh(() =>
        {
            var normalColors = Enumerable.Range(0, 8).Select(_ => ValidHexColors().Generator.Sample(0, 1).First()).ToArray();
            var brightColors = Enumerable.Range(0, 8).Select(_ => ValidHexColors().Generator.Sample(0, 1).First()).ToArray();
            var foreground = ValidHexColors().Generator.Sample(0, 1).First();
            var background = ValidHexColors().Generator.Sample(0, 1).First();
            var cursor = ValidHexColors().Generator.Sample(0, 1).First();
            var selection = ValidHexColors().Generator.Sample(0, 1).First();

            return $@"
[colors.normal]
black = '{normalColors[0]}'
red = '{normalColors[1]}'
green = '{normalColors[2]}'
yellow = '{normalColors[3]}'
blue = '{normalColors[4]}'
magenta = '{normalColors[5]}'
cyan = '{normalColors[6]}'
white = '{normalColors[7]}'

[colors.bright]
black = '{brightColors[0]}'
red = '{brightColors[1]}'
green = '{brightColors[2]}'
yellow = '{brightColors[3]}'
blue = '{brightColors[4]}'
magenta = '{brightColors[5]}'
cyan = '{brightColors[6]}'
white = '{brightColors[7]}'

[colors.primary]
background = '{background}'
foreground = '{foreground}'

[colors.cursor]
cursor = '{cursor}'
text = '#000000'

[colors.selection]
background = '{selection}'
text = '#ffffff'
";
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for TOML content with missing required sections.
    /// </summary>
    public static Arbitrary<string> InvalidTomlThemeContent()
    {
        return Gen.Fresh(() =>
        {
            var missingSection = Gen.Elements("colors.normal", "colors.bright", "colors.primary", "colors.cursor", "colors.selection").Sample(0, 1).First();

            var baseContent = @"
[colors.normal]
black = '#040404'
red = '#d84a33'
green = '#5da602'
yellow = '#eebb6e'
blue = '#417ab3'
magenta = '#e5c499'
cyan = '#bdcfe5'
white = '#dbded8'

[colors.bright]
black = '#685656'
red = '#d76b42'
green = '#99b52c'
yellow = '#ffb670'
blue = '#97d7ef'
magenta = '#aa7900'
cyan = '#bdcfe5'
white = '#e4d5c7'

[colors.primary]
background = '#040404'
foreground = '#feffff'

[colors.cursor]
cursor = '#feffff'
text = '#000000'

[colors.selection]
background = '#606060'
text = '#ffffff'
";

            // Remove the specified section
            var lines = baseContent.Split('\n').ToList();
            var sectionStart = -1;
            var sectionEnd = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim() == $"[{missingSection}]")
                {
                    sectionStart = i;
                }
                else if (sectionStart >= 0 && lines[i].Trim().StartsWith("[") && i > sectionStart)
                {
                    sectionEnd = i;
                    break;
                }
            }

            if (sectionStart >= 0)
            {
                if (sectionEnd == -1) sectionEnd = lines.Count;
                lines.RemoveRange(sectionStart, sectionEnd - sectionStart);
            }

            return string.Join('\n', lines);
        }).ToArbitrary();
    }

    /// <summary>
    /// Property 2: TOML Theme Parsing Consistency
    /// For any valid TOML theme file with all required color sections, parsing should
    /// successfully create a theme object with all colors correctly mapped.
    /// Feature: toml-terminal-theming, Property 2: TOML Theme Parsing Consistency
    /// Validates: Requirements 1.2, 5.3, 5.4, 5.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TomlThemeParsingConsistency_ShouldParseValidThemes()
    {
        return Prop.ForAll(ValidTomlThemeContent(), tomlContent =>
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"test_theme_{Guid.NewGuid():N}.toml");

            try
            {
                // Write TOML content to temporary file
                File.WriteAllText(tempFile, tomlContent);

                // Load theme from file
                var theme = TomlThemeLoader.LoadThemeFromFile(tempFile);

                // Theme should be successfully loaded
                if (!theme.HasValue) return false;

                var loadedTheme = theme.Value;

                // Theme should have a valid name
                bool hasValidName = !string.IsNullOrWhiteSpace(loadedTheme.Name) &&
                                   loadedTheme.Name != "Unknown Theme";

                // All colors should have alpha = 1.0 (opaque)
                bool allColorsOpaque =
                    loadedTheme.Colors.Black.W == 1.0f &&
                    loadedTheme.Colors.Red.W == 1.0f &&
                    loadedTheme.Colors.Green.W == 1.0f &&
                    loadedTheme.Colors.Yellow.W == 1.0f &&
                    loadedTheme.Colors.Blue.W == 1.0f &&
                    loadedTheme.Colors.Magenta.W == 1.0f &&
                    loadedTheme.Colors.Cyan.W == 1.0f &&
                    loadedTheme.Colors.White.W == 1.0f &&
                    loadedTheme.Colors.BrightBlack.W == 1.0f &&
                    loadedTheme.Colors.BrightRed.W == 1.0f &&
                    loadedTheme.Colors.BrightGreen.W == 1.0f &&
                    loadedTheme.Colors.BrightYellow.W == 1.0f &&
                    loadedTheme.Colors.BrightBlue.W == 1.0f &&
                    loadedTheme.Colors.BrightMagenta.W == 1.0f &&
                    loadedTheme.Colors.BrightCyan.W == 1.0f &&
                    loadedTheme.Colors.BrightWhite.W == 1.0f &&
                    loadedTheme.Colors.Foreground.W == 1.0f &&
                    loadedTheme.Colors.Background.W == 1.0f &&
                    loadedTheme.Colors.Cursor.W == 1.0f &&
                    loadedTheme.Colors.Selection.W == 1.0f;

                // All color components should be in valid range [0.0, 1.0]
                bool allColorsInRange = IsColorInRange(loadedTheme.Colors.Black) &&
                                       IsColorInRange(loadedTheme.Colors.Red) &&
                                       IsColorInRange(loadedTheme.Colors.Green) &&
                                       IsColorInRange(loadedTheme.Colors.Yellow) &&
                                       IsColorInRange(loadedTheme.Colors.Blue) &&
                                       IsColorInRange(loadedTheme.Colors.Magenta) &&
                                       IsColorInRange(loadedTheme.Colors.Cyan) &&
                                       IsColorInRange(loadedTheme.Colors.White) &&
                                       IsColorInRange(loadedTheme.Colors.BrightBlack) &&
                                       IsColorInRange(loadedTheme.Colors.BrightRed) &&
                                       IsColorInRange(loadedTheme.Colors.BrightGreen) &&
                                       IsColorInRange(loadedTheme.Colors.BrightYellow) &&
                                       IsColorInRange(loadedTheme.Colors.BrightBlue) &&
                                       IsColorInRange(loadedTheme.Colors.BrightMagenta) &&
                                       IsColorInRange(loadedTheme.Colors.BrightCyan) &&
                                       IsColorInRange(loadedTheme.Colors.BrightWhite) &&
                                       IsColorInRange(loadedTheme.Colors.Foreground) &&
                                       IsColorInRange(loadedTheme.Colors.Background) &&
                                       IsColorInRange(loadedTheme.Colors.Cursor) &&
                                       IsColorInRange(loadedTheme.Colors.Selection);

                // Theme type should be determined based on background brightness
                var backgroundBrightness = 0.299f * loadedTheme.Colors.Background.X +
                                          0.587f * loadedTheme.Colors.Background.Y +
                                          0.114f * loadedTheme.Colors.Background.Z;
                var expectedType = backgroundBrightness < 0.5f ? ThemeType.Dark : ThemeType.Light;
                bool correctThemeType = loadedTheme.Type == expectedType;

                // Cursor configuration should have reasonable defaults
                bool validCursorConfig = loadedTheme.Cursor.DefaultStyle == CursorStyle.BlinkingBlock &&
                                        loadedTheme.Cursor.DefaultBlink == true &&
                                        loadedTheme.Cursor.BlinkIntervalMs == 500;

                return hasValidName && allColorsOpaque && allColorsInRange &&
                       correctThemeType && validCursorConfig;
            }
            catch (Exception)
            {
                // Valid TOML content should not cause exceptions
                return false;
            }
            finally
            {
                // Clean up temporary file
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        });
    }

    /// <summary>
    /// Property: TOML Validation Enforcement
    /// TOML files missing required sections should be rejected during parsing.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TomlValidationEnforcement_ShouldRejectInvalidThemes()
    {
        return Prop.ForAll(InvalidTomlThemeContent(), invalidTomlContent =>
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"invalid_theme_{Guid.NewGuid():N}.toml");

            try
            {
                // Write invalid TOML content to temporary file
                File.WriteAllText(tempFile, invalidTomlContent);

                // Attempt to load theme from file
                var theme = TomlThemeLoader.LoadThemeFromFile(tempFile);

                // Invalid themes should not be loaded (should return null)
                return !theme.HasValue;
            }
            catch (Exception)
            {
                // Exceptions are acceptable for invalid content
                return true;
            }
            finally
            {
                // Clean up temporary file
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        });
    }

    /// <summary>
    /// Property: Theme Name Extraction Consistency
    /// Theme names should be consistently extracted from filenames.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeNameExtraction_ShouldBeConsistent()
    {
        return Prop.ForAll(ValidTomlThemeContent(), tomlContent =>
        {
            var themeNames = new[] { "Adventure", "Monokai Pro", "Matrix", "Neon Theme", "Coffee-Dark" };
            var themeName = Gen.Elements(themeNames).Sample(0, 1).First();
            var tempFile = Path.Combine(Path.GetTempPath(), $"{themeName}.toml");

            try
            {
                // Write TOML content to temporary file with specific name
                File.WriteAllText(tempFile, tomlContent);

                // Load theme from file
                var theme = TomlThemeLoader.LoadThemeFromFile(tempFile);

                // Theme should be loaded successfully
                if (!theme.HasValue) return false;

                // Theme name should match filename (without .toml extension)
                bool nameMatches = theme.Value.Name == themeName;

                // Test GetThemeDisplayName directly
                var extractedName = TomlThemeLoader.GetThemeDisplayName(tempFile);
                bool directExtractionMatches = extractedName == themeName;

                return nameMatches && directExtractionMatches;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                // Clean up temporary file
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        });
    }

    /// <summary>
    /// Property: Parsing Determinism
    /// Parsing the same TOML content multiple times should produce identical results.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TomlParsingDeterminism_ShouldProduceConsistentResults()
    {
        return Prop.ForAll(ValidTomlThemeContent(), tomlContent =>
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"determinism_test_{Guid.NewGuid():N}.toml");

            try
            {
                // Write TOML content to temporary file
                File.WriteAllText(tempFile, tomlContent);

                // Load theme multiple times
                var theme1 = TomlThemeLoader.LoadThemeFromFile(tempFile);
                var theme2 = TomlThemeLoader.LoadThemeFromFile(tempFile);

                // Both should succeed or both should fail
                if (theme1.HasValue != theme2.HasValue) return false;

                // If both succeeded, they should be identical
                if (theme1.HasValue && theme2.HasValue)
                {
                    var t1 = theme1.Value;
                    var t2 = theme2.Value;

                    bool identical = t1.Name == t2.Name &&
                                    t1.Type == t2.Type &&
                                    ColorsEqual(t1.Colors, t2.Colors) &&
                                    t1.Cursor.DefaultStyle == t2.Cursor.DefaultStyle &&
                                    t1.Cursor.DefaultBlink == t2.Cursor.DefaultBlink &&
                                    t1.Cursor.BlinkIntervalMs == t2.Cursor.BlinkIntervalMs;

                    return identical;
                }

                return true; // Both failed, which is consistent
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                // Clean up temporary file
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        });
    }

    /// <summary>
    /// Helper method to check if a color is in valid range [0.0, 1.0].
    /// </summary>
    private static bool IsColorInRange(Brutal.Numerics.float4 color)
    {
        return color.X >= 0.0f && color.X <= 1.0f &&
               color.Y >= 0.0f && color.Y <= 1.0f &&
               color.Z >= 0.0f && color.Z <= 1.0f &&
               color.W >= 0.0f && color.W <= 1.0f;
    }

    /// <summary>
    /// Helper method to compare two color palettes for equality.
    /// </summary>
    private static bool ColorsEqual(TerminalColorPalette c1, TerminalColorPalette c2)
    {
        const float epsilon = 0.001f;

        return ColorEqual(c1.Black, c2.Black, epsilon) &&
               ColorEqual(c1.Red, c2.Red, epsilon) &&
               ColorEqual(c1.Green, c2.Green, epsilon) &&
               ColorEqual(c1.Yellow, c2.Yellow, epsilon) &&
               ColorEqual(c1.Blue, c2.Blue, epsilon) &&
               ColorEqual(c1.Magenta, c2.Magenta, epsilon) &&
               ColorEqual(c1.Cyan, c2.Cyan, epsilon) &&
               ColorEqual(c1.White, c2.White, epsilon) &&
               ColorEqual(c1.BrightBlack, c2.BrightBlack, epsilon) &&
               ColorEqual(c1.BrightRed, c2.BrightRed, epsilon) &&
               ColorEqual(c1.BrightGreen, c2.BrightGreen, epsilon) &&
               ColorEqual(c1.BrightYellow, c2.BrightYellow, epsilon) &&
               ColorEqual(c1.BrightBlue, c2.BrightBlue, epsilon) &&
               ColorEqual(c1.BrightMagenta, c2.BrightMagenta, epsilon) &&
               ColorEqual(c1.BrightCyan, c2.BrightCyan, epsilon) &&
               ColorEqual(c1.BrightWhite, c2.BrightWhite, epsilon) &&
               ColorEqual(c1.Foreground, c2.Foreground, epsilon) &&
               ColorEqual(c1.Background, c2.Background, epsilon) &&
               ColorEqual(c1.Cursor, c2.Cursor, epsilon) &&
               ColorEqual(c1.Selection, c2.Selection, epsilon);
    }

    /// <summary>
    /// Helper method to compare two colors with epsilon tolerance.
    /// </summary>
    private static bool ColorEqual(Brutal.Numerics.float4 c1, Brutal.Numerics.float4 c2, float epsilon)
    {
        return Math.Abs(c1.X - c2.X) < epsilon &&
               Math.Abs(c1.Y - c2.Y) < epsilon &&
               Math.Abs(c1.Z - c2.Z) < epsilon &&
               Math.Abs(c1.W - c2.W) < epsilon;
    }
}
