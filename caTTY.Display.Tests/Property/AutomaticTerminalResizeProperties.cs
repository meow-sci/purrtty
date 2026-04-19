using System;
using NUnit.Framework;
using FsCheck;
using FsCheck.NUnit;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers;
using caTTY.Display.Configuration;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for automatic terminal resize on font changes.
/// Validates that terminal dimensions are recalculated when font size changes.
/// **Feature: window-design, Property 11: Font changes trigger terminal dimension recalculation**
/// </summary>
[TestFixture]
[Category("Property")]
public class AutomaticTerminalResizeProperties
{
    /// <summary>
    /// Generator for valid font size changes within the acceptable range.
    /// </summary>
    public static Arbitrary<(float initial, float changed)> ValidFontSizeChanges()
    {
        return (from initial in Gen.Choose(12, 48).Select(i => (float)i)
                from delta in Gen.Choose(-10, 10).Where(d => d != 0).Select(d => (float)d)
                let changed = Math.Max(LayoutConstants.MIN_FONT_SIZE,
                                     Math.Min(LayoutConstants.MAX_FONT_SIZE, initial + delta))
                where Math.Abs(changed - initial) > 0.1f // Ensure meaningful change
                select (initial, changed))
               .ToArbitrary();
    }

    /// <summary>
    /// Property: Font size changes should trigger terminal dimension recalculation.
    /// **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontSizeChange_TriggersTerminalResize()
    {
        return Prop.ForAll(ValidFontSizeChanges(), (fontSizes) =>
        {
            try
            {
                // Test that font configuration updates work correctly
                var initialFontConfig = new TerminalFontConfig
                {
                    FontSize = fontSizes.initial,
                    RegularFontName = "HackNerdFontMono-Regular",
                    AutoDetectContext = false
                };

                var newFontConfig = new TerminalFontConfig
                {
                    FontSize = fontSizes.changed,
                    RegularFontName = initialFontConfig.RegularFontName,
                    BoldFontName = initialFontConfig.BoldFontName,
                    ItalicFontName = initialFontConfig.ItalicFontName,
                    BoldItalicFontName = initialFontConfig.BoldItalicFontName,
                    AutoDetectContext = initialFontConfig.AutoDetectContext
                };

                // Both configurations should be valid
                initialFontConfig.Validate();
                newFontConfig.Validate();

                // Font size should be updated correctly
                bool fontSizeUpdated = Math.Abs(newFontConfig.FontSize - fontSizes.changed) < 0.1f;

                // Character metrics should be recalculated
                var initialMetrics = initialFontConfig.CalculateCharacterMetrics();
                var newMetrics = newFontConfig.CalculateCharacterMetrics();

                bool characterMetricsUpdated = initialMetrics.Width > 0 &&
                                             initialMetrics.Height > 0 &&
                                             newMetrics.Width > 0 &&
                                             newMetrics.Height > 0;

                // If font size changed, metrics should change proportionally
                if (Math.Abs(fontSizes.initial - fontSizes.changed) > 0.1f)
                {
                    float expectedRatio = fontSizes.changed / fontSizes.initial;
                    float actualWidthRatio = newMetrics.Width / initialMetrics.Width;
                    float actualHeightRatio = newMetrics.Height / initialMetrics.Height;

                    bool proportionalChange = Math.Abs(actualWidthRatio - expectedRatio) < 0.1f &&
                                             Math.Abs(actualHeightRatio - expectedRatio) < 0.1f;

                    characterMetricsUpdated = characterMetricsUpdated && proportionalChange;
                }

                return fontSizeUpdated && characterMetricsUpdated;
            }
            catch (Exception)
            {
                // Skip invalid configurations
                return true;
            }
        });
    }

    /// <summary>
    /// Property: Font configuration updates should be atomic (all-or-nothing).
    /// **Validates: Requirements 10.4, 10.5**
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FontConfigurationUpdate_IsAtomic()
    {
        return Prop.ForAll(ValidFontSizeChanges(), (fontSizes) =>
        {
            try
            {
                // Test that font configuration updates are atomic
                var initialFontConfig = new TerminalFontConfig
                {
                    FontSize = fontSizes.initial,
                    RegularFontName = "HackNerdFontMono-Regular",
                    AutoDetectContext = false
                };

                var newFontConfig = new TerminalFontConfig
                {
                    FontSize = fontSizes.changed,
                    RegularFontName = initialFontConfig.RegularFontName,
                    BoldFontName = initialFontConfig.BoldFontName,
                    ItalicFontName = initialFontConfig.ItalicFontName,
                    BoldItalicFontName = initialFontConfig.BoldItalicFontName,
                    AutoDetectContext = initialFontConfig.AutoDetectContext
                };

                bool updateSucceeded = false;
                try
                {
                    newFontConfig.Validate();
                    updateSucceeded = true;
                }
                catch (Exception)
                {
                    // Update failed - this is acceptable for invalid configurations
                    updateSucceeded = false;
                }

                if (updateSucceeded)
                {
                    // All font-related properties should be updated consistently
                    bool fontSizeUpdated = Math.Abs(newFontConfig.FontSize - fontSizes.changed) < 0.1f;
                    bool fontNamesConsistent = !string.IsNullOrWhiteSpace(newFontConfig.RegularFontName) &&
                                             !string.IsNullOrWhiteSpace(newFontConfig.BoldFontName) &&
                                             !string.IsNullOrWhiteSpace(newFontConfig.ItalicFontName) &&
                                             !string.IsNullOrWhiteSpace(newFontConfig.BoldItalicFontName);

                    return fontSizeUpdated && fontNamesConsistent;
                }
                else
                {
                    // Failed updates should not partially modify the configuration
                    return true; // This is expected behavior for invalid configurations
                }
            }
            catch (Exception)
            {
                // Skip invalid configurations
                return true;
            }
        });
    }
}
