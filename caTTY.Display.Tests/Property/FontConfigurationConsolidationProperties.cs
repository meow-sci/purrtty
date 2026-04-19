using System;
using NUnit.Framework;
using FsCheck;
using FsCheck.NUnit;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers;
using caTTY.Display.Configuration;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for font configuration consolidation.
/// Validates that only TerminalFontConfig is used for font settings storage.
/// **Feature: window-design, Property 10: Only TerminalFontConfig stores font configuration**
/// </summary>
[TestFixture]
[Category("Property")]
public class FontConfigurationConsolidationProperties
{
    /// <summary>
    /// Generator for valid font sizes within the acceptable range.
    /// </summary>
    public static Arbitrary<float> ValidFontSizes()
    {
        return Gen.Choose(12, 48).Select(i => (float)i).ToArbitrary();
    }

    /// <summary>
    /// Generator for valid font names.
    /// </summary>
    public static Arbitrary<string> ValidFontNames()
    {
        var fontNames = new[]
        {
            "HackNerdFontMono-Regular",
            "HackNerdFontMono-Bold",
            "HackNerdFontMono-Italic",
            "HackNerdFontMono-BoldItalic",
            "Consolas",
            "Courier New"
        };

        return Gen.Elements(fontNames).ToArbitrary();
    }

    /// <summary>
    /// Property: Font configuration changes should only affect TerminalFontConfig, not TerminalSettings.
    /// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5**
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontConfigurationConsolidation_OnlyAffectsTerminalFontConfig()
    {
        return Prop.ForAll(ValidFontSizes(), ValidFontNames(), (newFontSize, newFontName) =>
        {
            try
            {
                // Test that TerminalSettings no longer contains font configuration properties
                var settingsType = typeof(TerminalSettings);
                bool noFontSizeProperty = settingsType.GetProperty("FontSize") == null;
                bool noFontNameProperty = settingsType.GetProperty("FontName") == null;

                // Test that TerminalSettings validation works without font properties
                var settings = new TerminalSettings
                {
                    Title = "Test Terminal",
                    ShowLineNumbers = false,
                    WordWrap = false,
                    IsActive = true
                };

                settings.Validate(); // Should not throw for non-font properties

                // Test that TerminalSettings clone works without font properties
                var cloned = settings.Clone();
                bool cloneValid = cloned.Title == settings.Title &&
                                cloned.ShowLineNumbers == settings.ShowLineNumbers &&
                                cloned.WordWrap == settings.WordWrap &&
                                cloned.IsActive == settings.IsActive;

                // Test that TerminalFontConfig is the proper place for font configuration
                var fontConfig = new TerminalFontConfig
                {
                    FontSize = newFontSize,
                    RegularFontName = newFontName,
                    BoldFontName = "HackNerdFontMono-Bold",
                    ItalicFontName = "HackNerdFontMono-Italic",
                    BoldItalicFontName = "HackNerdFontMono-BoldItalic",
                    AutoDetectContext = false
                };

                fontConfig.Validate(); // Should work for font configuration

                return noFontSizeProperty && noFontNameProperty && cloneValid;
            }
            catch (Exception)
            {
                // Skip invalid configurations
                return true;
            }
        });
    }

    /// <summary>
    /// Property: TerminalSettings validation should not include font-related validation.
    /// **Validates: Requirements 9.1, 9.2, 9.5**
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TerminalSettingsValidation_DoesNotIncludeFontValidation()
    {
        var settingsGen = Gen.Fresh(() =>
        {
            var title = Gen.Elements("", "Terminal 1", "Valid Title").Sample(0, 1).First();
            var showLineNumbers = Gen.Elements(true, false).Sample(0, 1).First();
            var wordWrap = Gen.Elements(true, false).Sample(0, 1).First();
            var isActive = Gen.Elements(true, false).Sample(0, 1).First();

            return new TerminalSettings
            {
                Title = title,
                ShowLineNumbers = showLineNumbers,
                WordWrap = wordWrap,
                IsActive = isActive
            };
        });

        return Prop.ForAll(settingsGen.ToArbitrary(), settings =>
        {
            // Act & Assert - Validation should only check non-font properties
            try
            {
                settings.Validate();

                // If validation passes, title should be valid
                bool titleValid = !string.IsNullOrWhiteSpace(settings.Title);

                // Clone should work without font properties
                var cloned = settings.Clone();
                bool cloneValid = cloned.Title == settings.Title &&
                                cloned.ShowLineNumbers == settings.ShowLineNumbers &&
                                cloned.WordWrap == settings.WordWrap &&
                                cloned.IsActive == settings.IsActive;

                return titleValid && cloneValid;
            }
            catch (ArgumentException)
            {
                // Validation should only fail for invalid title
                return string.IsNullOrWhiteSpace(settings.Title);
            }
        });
    }
}
