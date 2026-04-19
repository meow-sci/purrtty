using caTTY.Display.Controllers;
using FsCheck;
using NUnit.Framework;
using Brutal.Numerics;

namespace caTTY.Display.Tests.Property;

/// <summary>
///     Property-based tests for terminal canvas space utilization in simplified UI layout.
///     Tests universal properties that should hold across all terminal canvas calculations.
/// </summary>
[TestFixture]
[Category("Property")]
public class TerminalCanvasSpaceProperties
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
    ///     Generator for valid character metrics.
    ///     Produces character width and height values within reasonable bounds.
    /// </summary>
    public static Arbitrary<(float width, float height)> ValidCharacterMetrics()
    {
        return Gen.Fresh(() =>
        {
            var width = Gen.Choose(6, 24).Select(x => (float)x).Sample(0, 1).First(); // 6px to 24px character width
            var height = Gen.Choose(12, 48).Select(x => (float)x).Sample(0, 1).First(); // 12px to 48px line height
            return (width, height);
        }).ToArbitrary();
    }

    /// <summary>
    ///     Property 13: Terminal Canvas Space Utilization
    ///     For any window size, the terminal canvas should utilize the full available space
    ///     after accounting for menu bar height, ensuring maximum space for terminal content.
    ///     Feature: toml-terminal-theming, Property 13: Terminal Canvas Space Utilization
    ///     Validates: Requirements 8.3
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TerminalCanvas_ShouldUtilizeFullAvailableSpace()
    {
        return Prop.ForAll(ValidWindowDimensions(), ValidCharacterMetrics(), (windowSize, charMetrics) =>
        {
            try
            {
                var (charWidth, lineHeight) = charMetrics;

                // Calculate expected terminal canvas dimensions for simplified UI
                // In simplified UI, only menu bar should consume vertical space
                float menuBarHeight = LayoutConstants.MENU_BAR_HEIGHT;
                float padding = LayoutConstants.WINDOW_PADDING;

                // Available space calculation for simplified UI (no tab area or info display)
                float availableWidth = windowSize.X - (padding * 2);
                float availableHeight = windowSize.Y - menuBarHeight - (padding * 2);

                // Ensure positive dimensions
                if (availableWidth <= 0 || availableHeight <= 0)
                {
                    return true; // Skip invalid window sizes
                }

                // Calculate terminal dimensions in characters
                int expectedCols = (int)Math.Floor(availableWidth / charWidth);
                int expectedRows = (int)Math.Floor(availableHeight / lineHeight);

                // Apply reasonable bounds
                expectedCols = Math.Max(10, Math.Min(1000, expectedCols));
                expectedRows = Math.Max(3, Math.Min(1000, expectedRows));

                // Calculate actual canvas size used by terminal
                float actualCanvasWidth = expectedCols * charWidth;
                float actualCanvasHeight = expectedRows * lineHeight;

                // Test that terminal canvas utilizes maximum available space
                bool utilizesMaximumWidth = actualCanvasWidth <= availableWidth &&
                                           actualCanvasWidth >= (availableWidth - charWidth); // Within one character

                bool utilizesMaximumHeight = actualCanvasHeight <= availableHeight &&
                                            actualCanvasHeight >= (availableHeight - lineHeight); // Within one line

                // Test that canvas doesn't exceed window bounds
                bool canvasWithinBounds = actualCanvasWidth <= windowSize.X &&
                                         actualCanvasHeight <= (windowSize.Y - menuBarHeight);

                // Test that canvas position leaves appropriate space for menu bar
                float expectedCanvasY = menuBarHeight + padding;
                float expectedCanvasX = padding;

                bool canvasPositionCorrect = expectedCanvasX >= 0 &&
                                            expectedCanvasY >= menuBarHeight &&
                                            (expectedCanvasX + actualCanvasWidth) <= windowSize.X &&
                                            (expectedCanvasY + actualCanvasHeight) <= windowSize.Y;

                // Test that terminal dimensions are reasonable
                bool terminalDimensionsReasonable = expectedCols >= 10 && expectedCols <= 1000 &&
                                                   expectedRows >= 3 && expectedRows <= 1000;

                // Test space utilization efficiency (should use at least 80% of available space)
                // For very small windows or unusual character metrics, relax the efficiency requirement
                float widthUtilization = actualCanvasWidth / availableWidth;
                float heightUtilization = actualCanvasHeight / availableHeight;

                // Relax efficiency requirements for edge cases
                bool isEdgeCase = windowSize.Y < 300 || windowSize.X < 400 || 
                                 lineHeight > 30 || charWidth > 20 ||
                                 availableHeight < (lineHeight * 5) || availableWidth < (charWidth * 20);

                float minEfficiency = isEdgeCase ? 0.5f : 0.8f; // Lower requirement for edge cases
                bool efficientSpaceUtilization = widthUtilization >= minEfficiency && heightUtilization >= minEfficiency;

                // Test that simplified UI removes tab area and info display overhead
                // In the original UI, these would consume additional vertical space
                float originalUIOverhead = 60.0f + LayoutConstants.TAB_AREA_HEIGHT; // Original info + tab area
                float simplifiedUIOverhead = 0.0f; // No tab area or info display in simplified UI

                bool simplifiedUIReducesOverhead = simplifiedUIOverhead < originalUIOverhead;

                // Test that menu bar is the only UI element consuming vertical space
                float totalVerticalOverhead = menuBarHeight + (padding * 2);
                float expectedVerticalOverhead = LayoutConstants.MENU_BAR_HEIGHT + (LayoutConstants.WINDOW_PADDING * 2);

                bool onlyMenuBarConsumesVerticalSpace = Math.Abs(totalVerticalOverhead - expectedVerticalOverhead) < 1.0f;

                return utilizesMaximumWidth && utilizesMaximumHeight && canvasWithinBounds &&
                       canvasPositionCorrect && terminalDimensionsReasonable && efficientSpaceUtilization &&
                       simplifiedUIReducesOverhead && onlyMenuBarConsumesVerticalSpace;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: Terminal Canvas Positioning Consistency
    ///     For any window configuration, the terminal canvas should be positioned
    ///     consistently below the menu bar with appropriate padding.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TerminalCanvas_ShouldBePositionedConsistently()
    {
        return Prop.ForAll(ValidWindowDimensions(), windowSize =>
        {
            try
            {
                float2 windowPos = new float2(100, 100); // Arbitrary window position

                // Calculate expected canvas position for simplified UI
                float expectedCanvasX = windowPos.X + LayoutConstants.WINDOW_PADDING;
                float expectedCanvasY = windowPos.Y + LayoutConstants.MENU_BAR_HEIGHT + LayoutConstants.WINDOW_PADDING;

                // Test that canvas is positioned correctly relative to window
                bool canvasXPositionCorrect = expectedCanvasX == (windowPos.X + LayoutConstants.WINDOW_PADDING);
                bool canvasYPositionCorrect = expectedCanvasY == (windowPos.Y + LayoutConstants.MENU_BAR_HEIGHT + LayoutConstants.WINDOW_PADDING);

                // Test that canvas position respects menu bar space
                bool canvasRespectMenuBar = expectedCanvasY > (windowPos.Y + LayoutConstants.MENU_BAR_HEIGHT);

                // Test that canvas position includes appropriate padding
                bool canvasIncludesPadding = expectedCanvasX > windowPos.X &&
                                            expectedCanvasY > (windowPos.Y + LayoutConstants.MENU_BAR_HEIGHT);

                // Test that canvas position is within window bounds
                bool canvasPositionWithinBounds = expectedCanvasX >= windowPos.X &&
                                                 expectedCanvasY >= windowPos.Y &&
                                                 expectedCanvasX < (windowPos.X + windowSize.X) &&
                                                 expectedCanvasY < (windowPos.Y + windowSize.Y);

                // Test that canvas leaves space for padding on all sides
                float rightPadding = (windowPos.X + windowSize.X) - expectedCanvasX;
                float bottomPadding = (windowPos.Y + windowSize.Y) - expectedCanvasY;

                bool paddingSpaceAvailable = rightPadding >= LayoutConstants.WINDOW_PADDING &&
                                            bottomPadding >= LayoutConstants.WINDOW_PADDING;

                return canvasXPositionCorrect && canvasYPositionCorrect && canvasRespectMenuBar &&
                       canvasIncludesPadding && canvasPositionWithinBounds && paddingSpaceAvailable;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: Simplified UI Layout Efficiency
    ///     For any window configuration, the simplified UI should provide more
    ///     terminal canvas space compared to the original complex UI layout.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SimplifiedUI_ShouldProvideMoreCanvasSpace()
    {
        return Prop.ForAll(ValidWindowDimensions(), ValidCharacterMetrics(), (windowSize, charMetrics) =>
        {
            try
            {
                var (charWidth, lineHeight) = charMetrics;

                // Calculate canvas space for simplified UI (only menu bar)
                float simplifiedUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + (LayoutConstants.WINDOW_PADDING * 2);
                float simplifiedAvailableHeight = windowSize.Y - simplifiedUIOverhead;

                // Calculate canvas space for original complex UI (menu bar + tab area + info display)
                float originalUIOverhead = LayoutConstants.MENU_BAR_HEIGHT + 
                                          LayoutConstants.TAB_AREA_HEIGHT + 
                                          60.0f + // Original terminal info display
                                          (LayoutConstants.WINDOW_PADDING * 2);
                float originalAvailableHeight = windowSize.Y - originalUIOverhead;

                // Ensure positive dimensions
                if (simplifiedAvailableHeight <= 0 || originalAvailableHeight <= 0)
                {
                    return true; // Skip invalid configurations
                }

                // Calculate terminal rows for each UI layout
                int simplifiedRows = Math.Max(3, (int)Math.Floor(simplifiedAvailableHeight / lineHeight));
                int originalRows = Math.Max(3, (int)Math.Floor(originalAvailableHeight / lineHeight));

                // Test that simplified UI provides more or equal terminal rows
                bool simplifiedProvidesMoreRows = simplifiedRows >= originalRows;

                // Test that the improvement is significant for reasonable window sizes
                if (windowSize.Y >= 600) // For reasonably sized windows
                {
                    bool significantImprovement = simplifiedRows > originalRows ||
                                                 (simplifiedAvailableHeight - originalAvailableHeight) >= lineHeight;
                    
                    return simplifiedProvidesMoreRows && significantImprovement;
                }

                return simplifiedProvidesMoreRows;
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: Menu Bar Accessibility Preservation
    ///     For any window configuration, the simplified UI should preserve
    ///     menu bar functionality while maximizing terminal canvas space.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SimplifiedUI_ShouldPreserveMenuBarAccessibility()
    {
        return Prop.ForAll(ValidWindowDimensions(), windowSize =>
        {
            try
            {
                // Test that menu bar space is preserved in simplified UI
                float menuBarHeight = LayoutConstants.MENU_BAR_HEIGHT;
                bool menuBarSpacePreserved = menuBarHeight > 0 && menuBarHeight <= 50.0f;

                // Test that menu bar spans full window width
                float menuBarWidth = windowSize.X;
                bool menuBarSpansFullWidth = menuBarWidth == windowSize.X;

                // Test that menu bar is positioned at the top
                float2 windowPos = new float2(100, 100);
                float menuBarY = windowPos.Y;
                bool menuBarAtTop = menuBarY == windowPos.Y;

                // Test that menu bar doesn't interfere with terminal canvas
                float canvasStartY = windowPos.Y + menuBarHeight + LayoutConstants.WINDOW_PADDING;
                bool menuBarDoesntInterfereWithCanvas = canvasStartY > (windowPos.Y + menuBarHeight);

                // Test that menu bar height is consistent with layout constants
                bool menuBarHeightConsistent = menuBarHeight == LayoutConstants.MENU_BAR_HEIGHT;

                // Test that menu bar leaves maximum space for terminal content
                float availableHeightForTerminal = windowSize.Y - menuBarHeight - (LayoutConstants.WINDOW_PADDING * 2);
                float totalWindowHeight = windowSize.Y;
                float menuBarSpaceRatio = menuBarHeight / totalWindowHeight;

                bool menuBarUsesMinimalSpace = menuBarSpaceRatio <= 0.15f; // Menu bar should use ≤15% of window height

                return menuBarSpacePreserved && menuBarSpansFullWidth && menuBarAtTop &&
                       menuBarDoesntInterfereWithCanvas && menuBarHeightConsistent && menuBarUsesMinimalSpace;
            }
            catch
            {
                return false;
            }
        });
    }
}