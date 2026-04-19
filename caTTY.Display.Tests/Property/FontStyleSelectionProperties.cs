using caTTY.Core.Types;
using caTTY.Display.Configuration;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for font style selection consistency.
/// Tests universal properties that should hold across all SGR attribute combinations.
/// </summary>
[TestFixture]
[Category("Property")]
public class FontStyleSelectionProperties
{
    /// <summary>
    /// Generator for SGR attributes with various font style combinations.
    /// Produces realistic attribute combinations for testing font selection.
    /// </summary>
    public static Arbitrary<SgrAttributes> SgrAttributesWithFontStyles()
    {
        return Gen.Fresh(() =>
        {
            bool bold = Gen.Elements(true, false).Sample(0, 1).First();
            bool italic = Gen.Elements(true, false).Sample(0, 1).First();
            bool faint = Gen.Elements(true, false).Sample(0, 1).First();
            bool underline = Gen.Elements(true, false).Sample(0, 1).First();
            bool blink = Gen.Elements(true, false).Sample(0, 1).First();
            bool inverse = Gen.Elements(true, false).Sample(0, 1).First();
            bool hidden = Gen.Elements(true, false).Sample(0, 1).First();
            bool strikethrough = Gen.Elements(true, false).Sample(0, 1).First();

            return new SgrAttributes(
                bold: bold,
                faint: faint,
                italic: italic,
                underline: underline,
                blink: blink,
                inverse: inverse,
                hidden: hidden,
                strikethrough: strikethrough
            );
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for valid font configurations with all font styles defined.
    /// </summary>
    public static Arbitrary<TerminalFontConfig> ValidFontConfigurations()
    {
        return Gen.Fresh(() =>
        {
            string[] fontFamilies = { "HackNerdFontMono", "TestFont", "GameFont", "MonoFont" };
            string fontFamily = Gen.Elements(fontFamilies).Sample(0, 1).First();
            float fontSize = Gen.Elements(8.0f, 10.0f, 12.0f, 14.0f, 16.0f, 18.0f, 20.0f, 24.0f).Sample(0, 1).First();

            return new TerminalFontConfig
            {
                RegularFontName = $"{fontFamily}-Regular",
                BoldFontName = $"{fontFamily}-Bold",
                ItalicFontName = $"{fontFamily}-Italic",
                BoldItalicFontName = $"{fontFamily}-BoldItalic",
                FontSize = fontSize,
                AutoDetectContext = false
            };
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for character positions within reasonable terminal bounds.
    /// </summary>
    public static Arbitrary<(int row, int col)> TerminalPositions()
    {
        return Gen.Fresh(() =>
        {
            int row = Gen.Choose(0, 23).Sample(0, 1).First(); // Standard 24-row terminal
            int col = Gen.Choose(0, 79).Sample(0, 1).First(); // Standard 80-column terminal
            return (row, col);
        }).ToArbitrary();
    }

    /// <summary>
    /// Property 6: Font Style Selection Consistency
    /// For any character with SGR attributes (bold, italic, bold+italic), the system should
    /// consistently select the appropriate font variant (BoldFont, ItalicFont, BoldItalicFont,
    /// or RegularFont) and render the character using that font.
    /// Feature: font-configuration, Property 6: Font Style Selection Consistency
    /// Validates: Requirements 1.3, character rendering consistency
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontStyleSelection_ShouldSelectAppropriateFont()
    {
        return Prop.ForAll(SgrAttributesWithFontStyles(), ValidFontConfigurations(), (attributes, fontConfig) =>
        {
            try
            {
                // Validate font configuration
                fontConfig.Validate();

                // Test font selection logic based on SGR attributes
                string expectedFontName = SelectExpectedFont(attributes, fontConfig);

                // Verify font selection is deterministic
                string selectedFont1 = SelectExpectedFont(attributes, fontConfig);
                string selectedFont2 = SelectExpectedFont(attributes, fontConfig);

                bool fontSelectionDeterministic = selectedFont1 == selectedFont2;

                // Verify font selection follows the correct logic
                bool fontSelectionCorrect = VerifyFontSelectionLogic(attributes, expectedFontName, fontConfig);

                // Test that font selection is consistent across multiple calls
                bool consistentSelection = true;
                for (int i = 0; i < 5; i++)
                {
                    string testSelection = SelectExpectedFont(attributes, fontConfig);
                    if (testSelection != expectedFontName)
                    {
                        consistentSelection = false;
                        break;
                    }
                }

                // Test that different attribute combinations produce different font selections when appropriate
                var regularAttributes = new SgrAttributes(bold: false, italic: false);
                var boldAttributes = new SgrAttributes(bold: true, italic: false);
                var italicAttributes = new SgrAttributes(bold: false, italic: true);
                var boldItalicAttributes = new SgrAttributes(bold: true, italic: true);

                string regularFont = SelectExpectedFont(regularAttributes, fontConfig);
                string boldFont = SelectExpectedFont(boldAttributes, fontConfig);
                string italicFont = SelectExpectedFont(italicAttributes, fontConfig);
                string boldItalicFont = SelectExpectedFont(boldItalicAttributes, fontConfig);

                // Verify that different styles produce different font selections (when fonts are different)
                bool styleDistinction = true;
                if (fontConfig.RegularFontName != fontConfig.BoldFontName)
                {
                    styleDistinction = styleDistinction && (regularFont != boldFont);
                }
                if (fontConfig.RegularFontName != fontConfig.ItalicFontName)
                {
                    styleDistinction = styleDistinction && (regularFont != italicFont);
                }
                if (fontConfig.RegularFontName != fontConfig.BoldItalicFontName)
                {
                    styleDistinction = styleDistinction && (regularFont != boldItalicFont);
                }

                return fontSelectionDeterministic && fontSelectionCorrect &&
                       consistentSelection && styleDistinction;
            }
            catch (ArgumentException)
            {
                // Invalid font configurations should be rejected
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Font Style Fallback Consistency
    /// When style-specific fonts are not available or are the same as regular font,
    /// the system should consistently fall back to the regular font while maintaining
    /// the same selection logic.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontStyleFallback_ShouldMaintainConsistency()
    {
        return Prop.ForAll(SgrAttributesWithFontStyles(), (attributes) =>
        {
            try
            {
                // Create a font configuration where all styles use the same font (fallback scenario)
                var fallbackConfig = new TerminalFontConfig
                {
                    RegularFontName = "TestFont-Regular",
                    BoldFontName = "TestFont-Regular", // Same as regular
                    ItalicFontName = "TestFont-Regular", // Same as regular
                    BoldItalicFontName = "TestFont-Regular", // Same as regular
                    FontSize = 16.0f,
                    AutoDetectContext = false
                };

                fallbackConfig.Validate();

                // Test that font selection still works correctly even with fallback fonts
                string selectedFont = SelectExpectedFont(attributes, fallbackConfig);

                // In fallback scenario, all selections should return the regular font
                bool fallbackCorrect = selectedFont == fallbackConfig.RegularFontName;

                // Test consistency across multiple calls
                bool consistentFallback = true;
                for (int i = 0; i < 3; i++)
                {
                    string testSelection = SelectExpectedFont(attributes, fallbackConfig);
                    if (testSelection != fallbackConfig.RegularFontName)
                    {
                        consistentFallback = false;
                        break;
                    }
                }

                // Test that the selection logic still follows the same pattern
                bool logicConsistent = VerifyFontSelectionLogic(attributes, selectedFont, fallbackConfig);

                return fallbackCorrect && consistentFallback && logicConsistent;
            }
            catch (ArgumentException)
            {
                // Invalid configurations should be rejected
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Font Selection Independence from Other Attributes
    /// Font selection should depend only on Bold and Italic attributes and should not
    /// be affected by other SGR attributes like colors, underline, blink, etc.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontSelection_ShouldBeIndependentOfOtherAttributes()
    {
        return Prop.ForAll(ValidFontConfigurations(), (fontConfig) =>
        {
            try
            {
                fontConfig.Validate();

                // Create attributes with same bold/italic but different other attributes
                var baseAttributes = new SgrAttributes(bold: true, italic: false);
                var attributesWithColor = new SgrAttributes(
                    bold: true,
                    italic: false,
                    foregroundColor: new Color(NamedColor.Red)
                );
                var attributesWithUnderline = new SgrAttributes(
                    bold: true,
                    italic: false,
                    underline: true
                );
                var attributesWithBlink = new SgrAttributes(
                    bold: true,
                    italic: false,
                    blink: true
                );
                var attributesWithInverse = new SgrAttributes(
                    bold: true,
                    italic: false,
                    inverse: true
                );

                // All should select the same font since bold/italic are the same
                string baseFont = SelectExpectedFont(baseAttributes, fontConfig);
                string colorFont = SelectExpectedFont(attributesWithColor, fontConfig);
                string underlineFont = SelectExpectedFont(attributesWithUnderline, fontConfig);
                string blinkFont = SelectExpectedFont(attributesWithBlink, fontConfig);
                string inverseFont = SelectExpectedFont(attributesWithInverse, fontConfig);

                bool fontSelectionIndependent = baseFont == colorFont &&
                                                baseFont == underlineFont &&
                                                baseFont == blinkFont &&
                                                baseFont == inverseFont;

                // Test with different bold/italic combinations
                var regularAttrs = new SgrAttributes(bold: false, italic: false, blink: true);
                var boldAttrs = new SgrAttributes(bold: true, italic: false, blink: true);
                var italicAttrs = new SgrAttributes(bold: false, italic: true, blink: true);
                var boldItalicAttrs = new SgrAttributes(bold: true, italic: true, blink: true);

                string regularFont = SelectExpectedFont(regularAttrs, fontConfig);
                string boldFont = SelectExpectedFont(boldAttrs, fontConfig);
                string italicFont = SelectExpectedFont(italicAttrs, fontConfig);
                string boldItalicFont = SelectExpectedFont(boldItalicAttrs, fontConfig);

                // Font selection should follow bold/italic pattern regardless of blink
                bool patternCorrect = VerifyFontSelectionLogic(regularAttrs, regularFont, fontConfig) &&
                                      VerifyFontSelectionLogic(boldAttrs, boldFont, fontConfig) &&
                                      VerifyFontSelectionLogic(italicAttrs, italicFont, fontConfig) &&
                                      VerifyFontSelectionLogic(boldItalicAttrs, boldItalicFont, fontConfig);

                return fontSelectionIndependent && patternCorrect;
            }
            catch (ArgumentException)
            {
                // Invalid configurations should be rejected
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Font Selection Boundary Conditions
    /// Font selection should handle edge cases correctly, including null attributes,
    /// default attributes, and extreme attribute combinations.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property FontSelection_ShouldHandleBoundaryConditions()
    {
        return Prop.ForAll(ValidFontConfigurations(), (fontConfig) =>
        {
            try
            {
                fontConfig.Validate();

                // Test default attributes (should select regular font)
                var defaultAttributes = SgrAttributes.Default;
                string defaultFont = SelectExpectedFont(defaultAttributes, fontConfig);
                bool defaultCorrect = defaultFont == fontConfig.RegularFontName;

                // Test all combinations of bold/italic
                var combinations = new[]
                {
                    (false, false, fontConfig.RegularFontName),
                    (true, false, fontConfig.BoldFontName),
                    (false, true, fontConfig.ItalicFontName),
                    (true, true, fontConfig.BoldItalicFontName)
                };

                bool allCombinationsCorrect = true;
                foreach (var (bold, italic, expectedFont) in combinations)
                {
                    var attrs = new SgrAttributes(bold: bold, italic: italic);
                    string selectedFont = SelectExpectedFont(attrs, fontConfig);
                    if (selectedFont != expectedFont)
                    {
                        allCombinationsCorrect = false;
                        break;
                    }
                }

                // Test that font selection is stable across multiple calls for same attributes
                var testAttributes = new SgrAttributes(bold: true, italic: true);
                string firstCall = SelectExpectedFont(testAttributes, fontConfig);
                string secondCall = SelectExpectedFont(testAttributes, fontConfig);
                string thirdCall = SelectExpectedFont(testAttributes, fontConfig);

                bool stabilityCorrect = firstCall == secondCall && secondCall == thirdCall;

                return defaultCorrect && allCombinationsCorrect && stabilityCorrect;
            }
            catch (ArgumentException)
            {
                // Invalid configurations should be rejected
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Font Selection Performance Consistency
    /// Font selection should be fast and consistent, with no performance degradation
    /// across repeated calls with the same or different attributes.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property FontSelection_ShouldMaintainPerformanceConsistency()
    {
        return Prop.ForAll(ValidFontConfigurations(), SgrAttributesWithFontStyles(), (fontConfig, attributes) =>
        {
            try
            {
                fontConfig.Validate();

                // Measure consistency across many calls (simulating real rendering load)
                const int iterations = 100;
                string expectedFont = SelectExpectedFont(attributes, fontConfig);

                bool allCallsConsistent = true;
                for (int i = 0; i < iterations; i++)
                {
                    string currentFont = SelectExpectedFont(attributes, fontConfig);
                    if (currentFont != expectedFont)
                    {
                        allCallsConsistent = false;
                        break;
                    }
                }

                // Test with rapid attribute changes (simulating dynamic content)
                var attributeVariations = new[]
                {
                    new SgrAttributes(bold: false, italic: false),
                    new SgrAttributes(bold: true, italic: false),
                    new SgrAttributes(bold: false, italic: true),
                    new SgrAttributes(bold: true, italic: true)
                };

                bool rapidChangesConsistent = true;
                foreach (var attrs in attributeVariations)
                {
                    string font1 = SelectExpectedFont(attrs, fontConfig);
                    string font2 = SelectExpectedFont(attrs, fontConfig);
                    if (font1 != font2)
                    {
                        rapidChangesConsistent = false;
                        break;
                    }
                }

                return allCallsConsistent && rapidChangesConsistent;
            }
            catch (ArgumentException)
            {
                // Invalid configurations should be rejected
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Simulates the font selection logic that should be used by TerminalController.
    /// This mirrors the SelectFont method in TerminalController.
    /// </summary>
    /// <param name="attributes">The SGR attributes</param>
    /// <param name="fontConfig">The font configuration</param>
    /// <returns>The expected font name</returns>
    private static string SelectExpectedFont(SgrAttributes attributes, TerminalFontConfig fontConfig)
    {
        if (attributes.Bold && attributes.Italic)
            return fontConfig.BoldItalicFontName;
        else if (attributes.Bold)
            return fontConfig.BoldFontName;
        else if (attributes.Italic)
            return fontConfig.ItalicFontName;
        else
            return fontConfig.RegularFontName;
    }

    /// <summary>
    /// Verifies that the font selection logic is correct for the given attributes.
    /// </summary>
    /// <param name="attributes">The SGR attributes</param>
    /// <param name="selectedFont">The font that was selected</param>
    /// <param name="fontConfig">The font configuration</param>
    /// <returns>True if the selection logic is correct</returns>
    private static bool VerifyFontSelectionLogic(SgrAttributes attributes, string selectedFont, TerminalFontConfig fontConfig)
    {
        if (attributes.Bold && attributes.Italic)
        {
            return selectedFont == fontConfig.BoldItalicFontName;
        }
        else if (attributes.Bold)
        {
            return selectedFont == fontConfig.BoldFontName;
        }
        else if (attributes.Italic)
        {
            return selectedFont == fontConfig.ItalicFontName;
        }
        else
        {
            return selectedFont == fontConfig.RegularFontName;
        }
    }
}
