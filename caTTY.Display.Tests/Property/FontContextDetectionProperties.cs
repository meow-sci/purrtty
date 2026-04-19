using caTTY.Display.Configuration;
using FsCheck;
using NUnit.Framework;
using ExecutionContext = caTTY.Display.Configuration.ExecutionContext;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for font context detection functionality.
/// Tests universal properties that should hold across all valid inputs and execution contexts.
/// </summary>
[TestFixture]
[Category("Property")]
public class FontContextDetectionProperties
{
    /// <summary>
    /// Generator for execution contexts.
    /// </summary>
    public static Arbitrary<ExecutionContext> ExecutionContexts()
    {
        return Gen.Elements(ExecutionContext.TestApp, ExecutionContext.GameMod, ExecutionContext.Unknown)
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for valid font sizes within the acceptable range.
    /// </summary>
    public static Arbitrary<float> ValidFontSizes()
    {
        return Gen.Elements(8.0f, 10.0f, 12.0f, 14.0f, 16.0f, 18.0f, 20.0f, 24.0f, 32.0f, 48.0f, 72.0f)
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for valid font names.
    /// </summary>
    public static Arbitrary<string> ValidFontNames()
    {
        return Gen.Elements(
                "HackNerdFontMono-Regular",
                "HackNerdFontMono-Bold",
                "HackNerdFontMono-Italic",
                "HackNerdFontMono-BoldItalic",
                "TestFont-Regular",
                "TestFont-Bold",
                "GameFont-Regular",
                "GameFont-Bold")
            .ToArbitrary();
    }

    /// <summary>
    /// Property 2: Context Detection and Default Configuration
    /// For any execution environment (TestApp or GameMod), the system should correctly detect
    /// the context and apply appropriate default font configuration, with TestApp using
    /// development-friendly defaults and GameMod using game-appropriate defaults.
    /// Feature: font-configuration, Property 2: Context Detection and Default Configuration
    /// Validates: Requirements 3.1, 3.2, 3.3, 3.4
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ContextDetectionAndDefaultConfiguration_ShouldApplyAppropriateDefaults()
    {
        return Prop.ForAll<bool>(_ =>
        {
            try
            {
                // Test the actual detection method
                TerminalFontConfig config = FontContextDetector.DetectAndCreateConfig();

                // Validate that configuration is created successfully
                if (config == null)
                    return false;

                // Validate that configuration passes validation
                bool configValid = true;
                try
                {
                    config.Validate();
                }
                catch
                {
                    configValid = false;
                }

                // Validate that font names are not null or empty
                bool fontNamesValid = !string.IsNullOrWhiteSpace(config.RegularFontName) &&
                                      !string.IsNullOrWhiteSpace(config.BoldFontName) &&
                                      !string.IsNullOrWhiteSpace(config.ItalicFontName) &&
                                      !string.IsNullOrWhiteSpace(config.BoldItalicFontName);

                // Validate that font size is within reasonable bounds
                bool fontSizeValid = config.FontSize > 0 && config.FontSize <= 72.0f;

                // In test environment, we expect either TestApp or GameMod-style configuration
                // TestApp uses 32.0f, GameMod uses 32.0f
                bool fontSizeReasonable = config.FontSize >= 8.0f && config.FontSize <= 32.0f;

                // Validate that AutoDetectContext is set to false (since we used explicit detection)
                bool autoDetectDisabled = !config.AutoDetectContext;

                return configValid && fontNamesValid &&
                       fontSizeValid && fontSizeReasonable && autoDetectDisabled;
            }
            catch
            {
                // Detection should not throw exceptions
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Factory Method Consistency
    /// For any factory method (CreateForTestApp or CreateForGameMod), the created configuration
    /// should always pass validation and have consistent properties.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FactoryMethods_ShouldCreateValidConfigurations()
    {
        return Prop.ForAll<bool>(_ =>
        {
            try
            {
                var testAppConfig = TerminalFontConfig.CreateForTestApp();
                var gameModConfig = TerminalFontConfig.CreateForGameMod();

                // Both configurations should pass validation
                testAppConfig.Validate();
                gameModConfig.Validate();

                // TestApp should use 32.0f font size
                bool testAppSizeCorrect = Math.Abs(testAppConfig.FontSize - 32.0f) < 0.001f;

                // GameMod should use 32.0f font size
                bool gameModSizeCorrect = Math.Abs(gameModConfig.FontSize - 32.0f) < 0.001f;

                // Both should have AutoDetectContext set to false
                bool autoDetectDisabled = !testAppConfig.AutoDetectContext && !gameModConfig.AutoDetectContext;

                // Both should use HackNerdFontMono fonts
                bool testAppFontsCorrect = testAppConfig.RegularFontName == "HackNerdFontMono-Regular" &&
                                           testAppConfig.BoldFontName == "HackNerdFontMono-Bold" &&
                                           testAppConfig.ItalicFontName == "HackNerdFontMono-Italic" &&
                                           testAppConfig.BoldItalicFontName == "HackNerdFontMono-BoldItalic";

                bool gameModFontsCorrect = gameModConfig.RegularFontName == "HackNerdFontMono-Regular" &&
                                           gameModConfig.BoldFontName == "HackNerdFontMono-Bold" &&
                                           gameModConfig.ItalicFontName == "HackNerdFontMono-Italic" &&
                                           gameModConfig.BoldItalicFontName == "HackNerdFontMono-BoldItalic";

                return testAppSizeCorrect && gameModSizeCorrect && autoDetectDisabled &&
                       testAppFontsCorrect && gameModFontsCorrect;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Context Detection Robustness
    /// The context detection system should handle various execution environments gracefully
    /// and always return a valid ExecutionContext value.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property ContextDetection_ShouldAlwaysReturnValidContext()
    {
        return Prop.ForAll<bool>(_ =>
        {
            try
            {
                ExecutionContext context = FontContextDetector.DetectExecutionContext();

                // Should return one of the defined enum values
                bool validContext = context == ExecutionContext.TestApp ||
                                    context == ExecutionContext.GameMod ||
                                    context == ExecutionContext.Unknown;

                return validContext;
            }
            catch
            {
                // Detection should not throw exceptions
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Configuration Validation Consistency
    /// For any valid font configuration created through any means, validation should
    /// either pass or fail consistently based on the configuration values.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ConfigurationValidation_ShouldBeConsistent()
    {
        return Prop.ForAll(ValidFontNames(), ValidFontSizes(), (fontName, fontSize) =>
        {
            var config = new TerminalFontConfig
            {
                RegularFontName = fontName,
                BoldFontName = fontName + "-Bold",
                ItalicFontName = fontName + "-Italic",
                BoldItalicFontName = fontName + "-BoldItalic",
                FontSize = fontSize
            };

            try
            {
                config.Validate();

                // If validation passes, all properties should be valid
                bool fontNameValid = !string.IsNullOrWhiteSpace(config.RegularFontName);
                bool fontSizeValid = config.FontSize > 0 && config.FontSize <= 72.0f;
                bool fallbacksSet = !string.IsNullOrWhiteSpace(config.BoldFontName) &&
                                    !string.IsNullOrWhiteSpace(config.ItalicFontName) &&
                                    !string.IsNullOrWhiteSpace(config.BoldItalicFontName);

                return fontNameValid && fontSizeValid && fallbacksSet;
            }
            catch (ArgumentException)
            {
                // If validation fails, it should be due to invalid font size or null font name
                bool shouldFail = string.IsNullOrWhiteSpace(fontName) ||
                                  fontSize <= 0 || fontSize > 72.0f;

                return shouldFail;
            }
            catch
            {
                // Other exceptions should not occur during validation
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Font Fallback Behavior
    /// When style-specific font names are null, they should fall back to the regular font name
    /// after validation.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontFallback_ShouldUseRegularFontForNullStyles()
    {
        return Prop.ForAll(ValidFontNames(), ValidFontSizes(), (regularFont, fontSize) =>
        {
            var config = new TerminalFontConfig
            {
                RegularFontName = regularFont,
                BoldFontName = null!,
                ItalicFontName = null!,
                BoldItalicFontName = null!,
                FontSize = fontSize
            };

            try
            {
                config.Validate();

                // After validation, all style fonts should fall back to regular font
                bool boldFallback = config.BoldFontName == regularFont;
                bool italicFallback = config.ItalicFontName == regularFont;
                bool boldItalicFallback = config.BoldItalicFontName == regularFont;

                return boldFallback && italicFallback && boldItalicFallback;
            }
            catch
            {
                // If validation fails, it should be due to invalid regular font or font size
                bool shouldFail = string.IsNullOrWhiteSpace(regularFont) ||
                                  fontSize <= 0 || fontSize > 72.0f;

                return shouldFail;
            }
        });
    }

    /// <summary>
    /// Property: Detection and Configuration Integration
    /// The DetectAndCreateConfig method should always produce a configuration that is
    /// equivalent to calling the appropriate factory method for the detected context.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property DetectionAndConfiguration_ShouldBeEquivalentToFactoryMethods()
    {
        return Prop.ForAll<bool>(_ =>
        {
            try
            {
                var detectedConfig = FontContextDetector.DetectAndCreateConfig();
                var detectedContext = FontContextDetector.DetectExecutionContext();

                TerminalFontConfig expectedConfig = detectedContext switch
                {
                    ExecutionContext.TestApp => TerminalFontConfig.CreateForTestApp(),
                    ExecutionContext.GameMod => TerminalFontConfig.CreateForGameMod(),
                    _ => TerminalFontConfig.CreateForTestApp() // Safe default
                };

                // Configurations should have equivalent properties
                bool fontNamesMatch = detectedConfig.RegularFontName == expectedConfig.RegularFontName &&
                                      detectedConfig.BoldFontName == expectedConfig.BoldFontName &&
                                      detectedConfig.ItalicFontName == expectedConfig.ItalicFontName &&
                                      detectedConfig.BoldItalicFontName == expectedConfig.BoldItalicFontName;

                bool fontSizeMatches = Math.Abs(detectedConfig.FontSize - expectedConfig.FontSize) < 0.001f;

                bool autoDetectMatches = detectedConfig.AutoDetectContext == expectedConfig.AutoDetectContext;

                return fontNamesMatch && fontSizeMatches && autoDetectMatches;
            }
            catch
            {
                return false;
            }
        });
    }
}
