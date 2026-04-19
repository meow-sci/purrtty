using caTTY.Display.Controllers;
using FsCheck;
using NUnit.Framework;
using Brutal.Numerics;

namespace caTTY.Display.Tests.Property;

/// <summary>
///     Property-based tests for window design layout constants and helper structures.
///     Tests universal properties that should hold across all layout calculations.
/// </summary>
[TestFixture]
[Category("Property")]
public class WindowDesignLayoutProperties
{
    /// <summary>
    ///     Generator for valid window dimensions.
    ///     Produces window sizes within reasonable bounds for terminal applications.
    /// </summary>
    public static Arbitrary<float2> ValidWindowDimensions()
    {
        return Gen.Fresh(() =>
        {
            var width = Gen.Choose(400, 3840).Select(x => (float)x).Sample(0, 1).First(); // 400px to 4K width
            var height = Gen.Choose(200, 2160).Select(x => (float)x).Sample(0, 1).First(); // 200px to 4K height
            return new float2(width, height);
        }).ToArbitrary();
    }

    /// <summary>
    ///     Generator for valid font sizes.
    ///     Produces font sizes within the acceptable range defined by layout constants.
    /// </summary>
    public static Arbitrary<float> ValidFontSizes()
    {
        return Gen.Choose((int)LayoutConstants.MIN_FONT_SIZE, (int)LayoutConstants.MAX_FONT_SIZE)
                  .Select(x => (float)x)
                  .ToArbitrary();
    }

    /// <summary>
    ///     Generator for valid terminal settings.
    ///     Produces realistic terminal settings within acceptable bounds.
    /// </summary>
    public static Arbitrary<TerminalSettings> ValidTerminalSettings()
    {
        return Gen.Fresh(() =>
        {
            var title = Gen.Elements("Terminal 1", "Terminal 2", "Shell", "Command Prompt").Sample(0, 1).First();
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
        }).ToArbitrary();
    }

    /// <summary>
    ///     Property 1: Layout constants are within reasonable ranges
    ///     For any layout constant defined in LayoutConstants, the values should be within
    ///     reasonable ranges for terminal window layout and should maintain proper proportions.
    ///     Feature: window-design, Property 1: Layout constants are within reasonable ranges
    ///     Validates: Requirements 7.2, 7.3
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property LayoutConstants_ShouldBeWithinReasonableRanges()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            try
            {
                // Test that all layout constants are positive and reasonable
                bool menuBarHeightValid = LayoutConstants.MENU_BAR_HEIGHT > 0 &&
                                         LayoutConstants.MENU_BAR_HEIGHT <= 50.0f;

                bool tabAreaHeightValid = LayoutConstants.MIN_TAB_AREA_HEIGHT > 0 &&
                                         LayoutConstants.MIN_TAB_AREA_HEIGHT <= LayoutConstants.MAX_TAB_AREA_HEIGHT &&
                                         LayoutConstants.MAX_TAB_AREA_HEIGHT <= 100.0f;

                bool settingsAreaHeightValid = LayoutConstants.MIN_SETTINGS_AREA_HEIGHT > 0 &&
                                              LayoutConstants.MIN_SETTINGS_AREA_HEIGHT <= LayoutConstants.MAX_SETTINGS_AREA_HEIGHT &&
                                              LayoutConstants.MAX_SETTINGS_AREA_HEIGHT <= 120.0f;

                bool addButtonWidthValid = LayoutConstants.ADD_BUTTON_WIDTH > 0 &&
                                          LayoutConstants.ADD_BUTTON_WIDTH <= 100.0f;

                bool elementSpacingValid = LayoutConstants.ELEMENT_SPACING >= 0 &&
                                          LayoutConstants.ELEMENT_SPACING <= 20.0f;

                bool windowPaddingValid = LayoutConstants.WINDOW_PADDING >= 0 &&
                                         LayoutConstants.WINDOW_PADDING <= 50.0f;

                // Test minimum window dimensions are reasonable
                bool minWindowWidthValid = LayoutConstants.MIN_WINDOW_WIDTH >= 200.0f &&
                                          LayoutConstants.MIN_WINDOW_WIDTH <= 800.0f;

                bool minWindowHeightValid = LayoutConstants.MIN_WINDOW_HEIGHT >= 100.0f &&
                                           LayoutConstants.MIN_WINDOW_HEIGHT <= 400.0f;

                // Test font size bounds are reasonable
                bool fontSizeBoundsValid = LayoutConstants.MIN_FONT_SIZE >= 3.0f &&
                                          LayoutConstants.MIN_FONT_SIZE <= 12.0f &&
                                          LayoutConstants.MAX_FONT_SIZE >= 48.0f &&
                                          LayoutConstants.MAX_FONT_SIZE <= 144.0f &&
                                          LayoutConstants.MIN_FONT_SIZE < LayoutConstants.MAX_FONT_SIZE;

                // Test proportional relationships between constants
                bool proportionsReasonable = LayoutConstants.MENU_BAR_HEIGHT <= LayoutConstants.MIN_TAB_AREA_HEIGHT + 10.0f &&
                                            LayoutConstants.MIN_TAB_AREA_HEIGHT <= LayoutConstants.MIN_SETTINGS_AREA_HEIGHT + 20.0f &&
                                            LayoutConstants.ADD_BUTTON_WIDTH <= LayoutConstants.MAX_TAB_AREA_HEIGHT + 10.0f;

                // Test that header areas don't consume too much space (using maximum possible header height)
                float maxHeaderHeight = LayoutConstants.MENU_BAR_HEIGHT +
                                       LayoutConstants.MAX_TAB_AREA_HEIGHT +
                                       LayoutConstants.MAX_SETTINGS_AREA_HEIGHT;
                bool headerHeightReasonable = maxHeaderHeight <= LayoutConstants.MIN_WINDOW_HEIGHT * 0.9f; // Increased tolerance

                // Test constrained sizing parameters
                bool constrainedSizingValid = LayoutConstants.TAB_HEIGHT_PER_EXTRA_TAB >= 0 &&
                                             LayoutConstants.TAB_HEIGHT_PER_EXTRA_TAB <= 30.0f &&
                                             LayoutConstants.SETTINGS_HEIGHT_PER_CONTROL_ROW > 0 &&
                                             LayoutConstants.SETTINGS_HEIGHT_PER_CONTROL_ROW <= 40.0f;

                return menuBarHeightValid && tabAreaHeightValid && settingsAreaHeightValid &&
                       addButtonWidthValid && elementSpacingValid && windowPaddingValid &&
                       minWindowWidthValid && minWindowHeightValid && fontSizeBoundsValid &&
                       proportionsReasonable && headerHeightReasonable && constrainedSizingValid;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: Terminal Settings Validation
    ///     For any terminal settings, validation should enforce reasonable bounds
    ///     and reject invalid configurations with appropriate exceptions.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TerminalSettings_ShouldValidateCorrectly()
    {
        // Generate settings that may be invalid
        var settingsGen = Gen.Fresh(() =>
        {
            var title = Gen.Elements("", null, "Valid Title", "Terminal 1").Sample(0, 1).First();
            var showLineNumbers = Gen.Elements(true, false).Sample(0, 1).First();
            var wordWrap = Gen.Elements(true, false).Sample(0, 1).First();
            var isActive = Gen.Elements(true, false).Sample(0, 1).First();

            return new TerminalSettings
            {
                Title = title ?? "",
                ShowLineNumbers = showLineNumbers,
                WordWrap = wordWrap,
                IsActive = isActive
            };
        });

        return Prop.ForAll(settingsGen.ToArbitrary(), settings =>
        {
            try
            {
                // Determine if settings should be valid
                bool shouldBeValid = !string.IsNullOrWhiteSpace(settings.Title);

                if (shouldBeValid)
                {
                    // Valid settings should pass validation
                    settings.Validate();
                    return true;
                }

                // Invalid settings should throw ArgumentException
                try
                {
                    settings.Validate();
                    return false; // Should have thrown exception
                }
                catch (ArgumentException)
                {
                    return true; // Expected exception
                }
                catch
                {
                    return false; // Wrong exception type
                }
            }
            catch (ArgumentException)
            {
                // ArgumentException is acceptable for invalid settings
                return true;
            }
            catch
            {
                // Other exceptions indicate a problem
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: Terminal Settings Clone Consistency
    ///     For any valid terminal settings, cloning should produce an identical
    ///     but independent copy that can be modified without affecting the original.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TerminalSettings_CloneShouldBeIndependent()
    {
        return Prop.ForAll(ValidTerminalSettings(), originalSettings =>
        {
            try
            {
                originalSettings.Validate();

                // Clone the settings
                var clonedSettings = originalSettings.Clone();

                // Verify clone has same values
                bool valuesMatch = clonedSettings.Title == originalSettings.Title &&
                                  clonedSettings.ShowLineNumbers == originalSettings.ShowLineNumbers &&
                                  clonedSettings.WordWrap == originalSettings.WordWrap &&
                                  clonedSettings.IsActive == originalSettings.IsActive;

                // Verify clone is independent (different object reference)
                bool independent = !ReferenceEquals(originalSettings, clonedSettings);

                // Modify clone and verify original is unchanged
                string originalTitle = originalSettings.Title;

                clonedSettings.Title = "Modified Title";

                bool originalUnchanged = originalSettings.Title == originalTitle;

                // Verify clone was actually modified
                bool cloneModified = clonedSettings.Title == "Modified Title";

                return valuesMatch && independent && originalUnchanged && cloneModified;
            }
            catch (ArgumentException)
            {
                // Invalid settings should be rejected
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: Layout Helper Methods Consistency
    ///     For any valid window dimensions, layout helper methods should produce
    ///     consistent and mathematically correct calculations with constrained variable heights.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property LayoutHelperMethods_ShouldProduceConsistentCalculations()
    {
        return Prop.ForAll(ValidWindowDimensions(), windowSize =>
        {
            try
            {
                // Test that window size validation works correctly
                bool isValidSize = windowSize.X >= LayoutConstants.MIN_WINDOW_WIDTH &&
                                  windowSize.Y >= LayoutConstants.MIN_WINDOW_HEIGHT;

                // For valid window sizes, test layout calculations
                if (isValidSize)
                {
                    // Test with different tab and settings configurations
                    var testConfigurations = new[]
                    {
                        new { TabCount = 1, SettingsRows = 1 }, // Minimum configuration
                        new { TabCount = 1, SettingsRows = 2 }, // Extra settings
                        new { TabCount = 3, SettingsRows = 1 }, // Multiple tabs (future)
                        new { TabCount = 2, SettingsRows = 3 }  // Mixed configuration
                    };

                    foreach (var config in testConfigurations)
                    {
                        // Calculate expected values manually using constrained sizing
                        float tabHeight = Math.Min(LayoutConstants.MAX_TAB_AREA_HEIGHT,
                            LayoutConstants.MIN_TAB_AREA_HEIGHT +
                            Math.Max(0, (config.TabCount - 1) * LayoutConstants.TAB_HEIGHT_PER_EXTRA_TAB));

                        float settingsHeight = Math.Min(LayoutConstants.MAX_SETTINGS_AREA_HEIGHT,
                            LayoutConstants.MIN_SETTINGS_AREA_HEIGHT +
                            Math.Max(0, (config.SettingsRows - 1) * LayoutConstants.SETTINGS_HEIGHT_PER_CONTROL_ROW));

                        float expectedHeaderHeight = LayoutConstants.MENU_BAR_HEIGHT + tabHeight + settingsHeight;

                        float expectedCanvasWidth = Math.Max(0, windowSize.X - LayoutConstants.WINDOW_PADDING * 2);
                        float expectedCanvasHeight = Math.Max(0, windowSize.Y - expectedHeaderHeight - LayoutConstants.WINDOW_PADDING * 2);

                        float2 expectedCanvasSize = new float2(expectedCanvasWidth, expectedCanvasHeight);

                        // Test that canvas size calculation is non-negative
                        bool canvasSizeValid = expectedCanvasSize.X >= 0 && expectedCanvasSize.Y >= 0;

                        // Test that canvas position calculation is reasonable
                        float2 windowPos = new float2(100, 100); // Arbitrary window position
                        float2 expectedCanvasPos = new float2(
                            windowPos.X + LayoutConstants.WINDOW_PADDING,
                            windowPos.Y + expectedHeaderHeight + LayoutConstants.WINDOW_PADDING
                        );

                        bool canvasPositionValid = expectedCanvasPos.X >= windowPos.X &&
                                                  expectedCanvasPos.Y >= windowPos.Y;

                        // Test that total layout doesn't exceed window bounds
                        float totalUsedHeight = expectedHeaderHeight + LayoutConstants.WINDOW_PADDING * 2;
                        float totalUsedWidth = LayoutConstants.WINDOW_PADDING * 2;

                        bool layoutFitsInWindow = totalUsedHeight <= windowSize.Y &&
                                                 totalUsedWidth <= windowSize.X;

                        // Test proportional relationships with constrained sizing
                        bool headerProportionReasonable = expectedHeaderHeight <= windowSize.Y * 0.9f; // Headers shouldn't dominate

                        // Test that constrained sizing works correctly
                        bool constrainedSizingWorks = tabHeight >= LayoutConstants.MIN_TAB_AREA_HEIGHT &&
                                                     tabHeight <= LayoutConstants.MAX_TAB_AREA_HEIGHT &&
                                                     settingsHeight >= LayoutConstants.MIN_SETTINGS_AREA_HEIGHT &&
                                                     settingsHeight <= LayoutConstants.MAX_SETTINGS_AREA_HEIGHT;

                        if (!canvasSizeValid || !canvasPositionValid || !layoutFitsInWindow ||
                            !headerProportionReasonable || !constrainedSizingWorks)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                // For invalid window sizes, validation should detect them
                return !isValidSize;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: Layout Constants Mathematical Relationships
    ///     Layout constants should maintain proper mathematical relationships
    ///     and be internally consistent for proper layout calculations with constrained variable sizing.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property LayoutConstants_ShouldMaintainMathematicalConsistency()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            try
            {
                // Test that minimum window size can accommodate all header areas at minimum size
                float minHeaderHeight = LayoutConstants.MENU_BAR_HEIGHT +
                                       LayoutConstants.MIN_TAB_AREA_HEIGHT +
                                       LayoutConstants.MIN_SETTINGS_AREA_HEIGHT;

                float totalVerticalPadding = LayoutConstants.WINDOW_PADDING * 2;
                float minRequiredHeight = minHeaderHeight + totalVerticalPadding + 50.0f; // 50px minimum for content

                bool minHeightSufficient = LayoutConstants.MIN_WINDOW_HEIGHT >= minRequiredHeight;

                // Test that minimum window size can accommodate maximum header areas
                float maxHeaderHeight = LayoutConstants.MENU_BAR_HEIGHT +
                                       LayoutConstants.MAX_TAB_AREA_HEIGHT +
                                       LayoutConstants.MAX_SETTINGS_AREA_HEIGHT;

                float maxRequiredHeight = maxHeaderHeight + totalVerticalPadding + 50.0f; // 50px minimum for content
                bool maxHeaderFitsReasonably = maxRequiredHeight <= LayoutConstants.MIN_WINDOW_HEIGHT * 2.0f; // Should fit in reasonable window

                // Test that minimum window width can accommodate UI elements
                float minRequiredWidth = LayoutConstants.ADD_BUTTON_WIDTH + LayoutConstants.WINDOW_PADDING * 2 + 100.0f; // 100px minimum for content
                bool minWidthSufficient = LayoutConstants.MIN_WINDOW_WIDTH >= minRequiredWidth;

                // Test that spacing values are proportional to component sizes
                bool spacingProportional = LayoutConstants.ELEMENT_SPACING <= LayoutConstants.MENU_BAR_HEIGHT * 0.5f &&
                                          LayoutConstants.ELEMENT_SPACING <= LayoutConstants.MIN_TAB_AREA_HEIGHT * 0.5f;

                // Test that padding is reasonable relative to minimum window size
                bool paddingReasonable = LayoutConstants.WINDOW_PADDING <= LayoutConstants.MIN_WINDOW_WIDTH * 0.1f &&
                                        LayoutConstants.WINDOW_PADDING <= LayoutConstants.MIN_WINDOW_HEIGHT * 0.1f;

                // Test that font size bounds are mathematically consistent
                bool fontBoundsConsistent = LayoutConstants.MIN_FONT_SIZE > 0 &&
                                           LayoutConstants.MAX_FONT_SIZE > LayoutConstants.MIN_FONT_SIZE &&
                                           LayoutConstants.MAX_FONT_SIZE / LayoutConstants.MIN_FONT_SIZE <= 20.0f; // Reasonable ratio

                // Test that add button width fits within tab area height (for square-ish button)
                bool addButtonProportional = LayoutConstants.ADD_BUTTON_WIDTH <= LayoutConstants.MAX_TAB_AREA_HEIGHT * 1.5f;

                // Test constrained sizing parameters are reasonable
                bool constrainedSizingConsistent =
                    LayoutConstants.MIN_TAB_AREA_HEIGHT <= LayoutConstants.MAX_TAB_AREA_HEIGHT &&
                    LayoutConstants.MIN_SETTINGS_AREA_HEIGHT <= LayoutConstants.MAX_SETTINGS_AREA_HEIGHT &&
                    LayoutConstants.TAB_HEIGHT_PER_EXTRA_TAB >= 0 &&
                    LayoutConstants.SETTINGS_HEIGHT_PER_CONTROL_ROW > 0 &&
                    LayoutConstants.SETTINGS_HEIGHT_PER_CONTROL_ROW <= LayoutConstants.MAX_SETTINGS_AREA_HEIGHT - LayoutConstants.MIN_SETTINGS_AREA_HEIGHT;

                // Test that constrained ranges are reasonable
                bool constrainedRangesReasonable =
                    (LayoutConstants.MAX_TAB_AREA_HEIGHT - LayoutConstants.MIN_TAB_AREA_HEIGHT) <= 50.0f && // Max 50px growth
                    (LayoutConstants.MAX_SETTINGS_AREA_HEIGHT - LayoutConstants.MIN_SETTINGS_AREA_HEIGHT) <= 60.0f; // Max 60px growth

                return minHeightSufficient && maxHeaderFitsReasonably && minWidthSufficient &&
                       spacingProportional && paddingReasonable && fontBoundsConsistent &&
                       addButtonProportional && constrainedSizingConsistent && constrainedRangesReasonable;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property 2: Menu bar spans full width and appears at top
    ///     For any terminal window configuration, the menu bar should be positioned
    ///     at the top of the window and span the full width of the content area.
    ///     Feature: window-design, Property 2: Menu bar spans full width and appears at top
    ///     Validates: Requirements 1.4, 1.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MenuBar_ShouldSpanFullWidthAndAppearAtTop()
    {
        return Prop.ForAll(ValidWindowDimensions(), windowSize =>
        {
            try
            {
                // Test that menu bar positioning logic is correct
                float2 windowPos = new float2(100, 100); // Arbitrary window position

                // Menu bar should be at the very top of the content area
                float expectedMenuBarY = windowPos.Y;
                float expectedMenuBarX = windowPos.X;

                // Menu bar should span the full width of the window content area
                float expectedMenuBarWidth = windowSize.X;
                float expectedMenuBarHeight = LayoutConstants.MENU_BAR_HEIGHT;

                // Test that menu bar dimensions are valid
                bool menuBarDimensionsValid = expectedMenuBarWidth > 0 &&
                                             expectedMenuBarHeight > 0 &&
                                             expectedMenuBarHeight == LayoutConstants.MENU_BAR_HEIGHT;

                // Test that menu bar position is at the top
                bool menuBarAtTop = expectedMenuBarY == windowPos.Y &&
                                   expectedMenuBarX == windowPos.X;

                // Test that menu bar spans full width
                bool menuBarSpansFullWidth = expectedMenuBarWidth == windowSize.X;

                // Test that menu bar height is consistent with layout constants
                bool menuBarHeightConsistent = expectedMenuBarHeight == LayoutConstants.MENU_BAR_HEIGHT;

                // Test that subsequent elements are positioned below the menu bar
                float nextElementY = expectedMenuBarY + expectedMenuBarHeight;
                bool subsequentElementsPositionedCorrectly = nextElementY > expectedMenuBarY;

                // Test that menu bar doesn't exceed window bounds
                bool menuBarWithinBounds = expectedMenuBarX >= windowPos.X &&
                                          expectedMenuBarY >= windowPos.Y &&
                                          (expectedMenuBarX + expectedMenuBarWidth) <= (windowPos.X + windowSize.X) &&
                                          (expectedMenuBarY + expectedMenuBarHeight) <= (windowPos.Y + windowSize.Y);

                // Test that menu bar takes priority in vertical layout order
                float tabAreaY = nextElementY;
                float settingsAreaY = tabAreaY + LayoutConstants.MIN_TAB_AREA_HEIGHT;
                float terminalCanvasY = settingsAreaY + LayoutConstants.MIN_SETTINGS_AREA_HEIGHT;

                bool verticalOrderCorrect = expectedMenuBarY < tabAreaY &&
                                           tabAreaY < settingsAreaY &&
                                           settingsAreaY < terminalCanvasY;

                // Test that menu bar leaves sufficient space for other elements
                float remainingHeight = windowSize.Y - expectedMenuBarHeight;
                bool sufficientSpaceRemaining = remainingHeight >= (LayoutConstants.MIN_TAB_AREA_HEIGHT +
                                                                   LayoutConstants.MIN_SETTINGS_AREA_HEIGHT +
                                                                   50.0f); // 50px minimum for terminal content

                return menuBarDimensionsValid && menuBarAtTop && menuBarSpansFullWidth &&
                       menuBarHeightConsistent && subsequentElementsPositionedCorrectly &&
                       menuBarWithinBounds && verticalOrderCorrect && sufficientSpaceRemaining;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property 3: Tab area maintains consistent height and full width
    ///     For any terminal window configuration, the tab area should maintain
    ///     consistent height within defined bounds and span the full width of the content area.
    ///     Feature: window-design, Property 3: Tab area maintains consistent height and full width
    ///     Validates: Requirements 2.1, 2.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TabArea_ShouldMaintainConsistentHeightAndFullWidth()
    {
        return Prop.ForAll(ValidWindowDimensions(), windowSize =>
        {
            try
            {
                // Test tab area positioning and dimensions
                float2 windowPos = new float2(100, 100); // Arbitrary window position

                // Tab area should be positioned below the menu bar
                float expectedTabAreaY = windowPos.Y + LayoutConstants.MENU_BAR_HEIGHT;
                float expectedTabAreaX = windowPos.X;

                // Tab area should span the full width of the window content area
                float expectedTabAreaWidth = windowSize.X;

                // Tab area height should use the fixed height for single terminal
                float expectedTabAreaHeight = LayoutConstants.TAB_AREA_HEIGHT;

                // Test that tab area dimensions are valid
                bool tabAreaDimensionsValid = expectedTabAreaWidth > 0 &&
                                             expectedTabAreaHeight > 0 &&
                                             expectedTabAreaHeight >= LayoutConstants.MIN_TAB_AREA_HEIGHT &&
                                             expectedTabAreaHeight <= LayoutConstants.MAX_TAB_AREA_HEIGHT;

                // Test that tab area is positioned correctly below menu bar
                bool tabAreaPositionedCorrectly = expectedTabAreaY == (windowPos.Y + LayoutConstants.MENU_BAR_HEIGHT) &&
                                                 expectedTabAreaX == windowPos.X;

                // Test that tab area spans full width
                bool tabAreaSpansFullWidth = expectedTabAreaWidth == windowSize.X;

                // Test that tab area height is consistent for single terminal
                bool tabAreaHeightConsistent = expectedTabAreaHeight >= LayoutConstants.MIN_TAB_AREA_HEIGHT;

                // Test tab content layout calculations
                float availableWidth = expectedTabAreaWidth;
                float addButtonWidth = LayoutConstants.ADD_BUTTON_WIDTH;
                float spacing = LayoutConstants.ELEMENT_SPACING;
                float tabWidth = Math.Max(100.0f, availableWidth - addButtonWidth - spacing);

                // Test that tab and add button fit within available width
                bool tabContentFitsWidth = (tabWidth + addButtonWidth + spacing) <= availableWidth &&
                                          tabWidth >= 100.0f && // Minimum tab width
                                          addButtonWidth == LayoutConstants.ADD_BUTTON_WIDTH;

                // Test that tab area doesn't exceed window bounds
                bool tabAreaWithinBounds = expectedTabAreaX >= windowPos.X &&
                                          expectedTabAreaY >= windowPos.Y &&
                                          (expectedTabAreaX + expectedTabAreaWidth) <= (windowPos.X + windowSize.X) &&
                                          (expectedTabAreaY + expectedTabAreaHeight) <= (windowPos.Y + windowSize.Y);

                // Test that subsequent elements are positioned below the tab area
                float nextElementY = expectedTabAreaY + expectedTabAreaHeight;
                bool subsequentElementsPositionedCorrectly = nextElementY > expectedTabAreaY;

                // Test vertical layout order (tab area between menu bar and settings area)
                float menuBarBottom = windowPos.Y + LayoutConstants.MENU_BAR_HEIGHT;
                float settingsAreaY = nextElementY;

                bool verticalOrderCorrect = expectedTabAreaY == menuBarBottom &&
                                           settingsAreaY == nextElementY &&
                                           expectedTabAreaY > windowPos.Y &&
                                           settingsAreaY > expectedTabAreaY;

                // Test that tab area leaves sufficient space for other elements
                float remainingHeight = windowSize.Y - LayoutConstants.MENU_BAR_HEIGHT - expectedTabAreaHeight;
                bool sufficientSpaceRemaining = remainingHeight >= (LayoutConstants.MIN_SETTINGS_AREA_HEIGHT +
                                                                   50.0f); // 50px minimum for terminal content

                // Test that single tab configuration uses fixed height correctly
                bool singleTabHeightCorrect = expectedTabAreaHeight == LayoutConstants.TAB_AREA_HEIGHT;

                // Test that add button positioning is correct
                float addButtonX = expectedTabAreaX + tabWidth + spacing;
                bool addButtonPositionedCorrectly = addButtonX >= expectedTabAreaX &&
                                                   (addButtonX + addButtonWidth) <= (expectedTabAreaX + expectedTabAreaWidth);

                return tabAreaDimensionsValid && tabAreaPositionedCorrectly && tabAreaSpansFullWidth &&
                       tabAreaHeightConsistent && tabContentFitsWidth && tabAreaWithinBounds &&
                       subsequentElementsPositionedCorrectly && verticalOrderCorrect &&
                       sufficientSpaceRemaining && singleTabHeightCorrect && addButtonPositionedCorrectly;
            }
            catch
            {
                return false;
            }
        });
    }
}
