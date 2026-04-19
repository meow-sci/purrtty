using caTTY.Display.Utils;
using Brutal.Numerics;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for coordinate conversion between screen pixels and terminal cells.
/// Tests universal properties that should hold across all valid coordinate conversions.
/// </summary>
[TestFixture]
[Category("Property")]
public class CoordinateConversionProperties
{
    /// <summary>
    /// Generator for valid character metrics.
    /// Produces realistic character width and height values.
    /// </summary>
    public static Arbitrary<(float Width, float Height)> ValidCharacterMetrics()
    {
        var widthGen = Gen.Choose(4, 32).Select(x => (float)x);
        var heightGen = Gen.Choose(8, 64).Select(x => (float)x);

        return Gen.Zip(widthGen, heightGen)
            .Select(tuple => (tuple.Item1, tuple.Item2))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for valid terminal origins.
    /// Produces reasonable origin positions for terminal placement.
    /// </summary>
    public static Arbitrary<float2> ValidTerminalOrigins()
    {
        var xGen = Gen.Choose(0, 1920).Select(x => (float)x);
        var yGen = Gen.Choose(0, 1080).Select(y => (float)y);

        return Gen.Zip(xGen, yGen)
            .Select(tuple => new float2(tuple.Item1, tuple.Item2))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for valid terminal dimensions.
    /// Produces reasonable terminal sizes in columns and rows.
    /// </summary>
    public static Arbitrary<(int Width, int Height)> ValidTerminalDimensions()
    {
        var widthGen = Gen.Choose(10, 200);
        var heightGen = Gen.Choose(5, 100);

        return Gen.Zip(widthGen, heightGen)
            .Select(tuple => (tuple.Item1, tuple.Item2))
            .ToArbitrary();
    }

    /// <summary>
    /// Generator for valid pixel positions within terminal bounds.
    /// Produces pixel coordinates that should be within a terminal area.
    /// </summary>
    public static Arbitrary<float2> ValidPixelPositions(float2 origin, float charWidth, float lineHeight, int terminalWidth, int terminalHeight)
    {
        var terminalPixelWidth = terminalWidth * charWidth;
        var terminalPixelHeight = terminalHeight * lineHeight;

        var xGen = Gen.Choose(0, (int)terminalPixelWidth).Select(x => origin.X + (float)x);
        var yGen = Gen.Choose(0, (int)terminalPixelHeight).Select(y => origin.Y + (float)y);

        return Gen.Zip(xGen, yGen)
            .Select(tuple => new float2(tuple.Item1, tuple.Item2))
            .ToArbitrary();
    }

    /// <summary>
    /// Property 8: Coordinate Conversion Accuracy
    /// For any valid screen pixel position within terminal bounds, conversion to 1-based terminal
    /// coordinates should be accurate and consistent with character metrics.
    /// Feature: mouse-input-support, Property 8: Coordinate Conversion Accuracy
    /// Validates: Requirements R3.1, R3.2, R3.4
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CoordinateConversionAccuracy_ShouldBeAccurateAndConsistent()
    {
        return Prop.ForAll(
            ValidCharacterMetrics(),
            ValidTerminalOrigins(),
            ValidTerminalDimensions(),
            (metrics, origin, dimensions) =>
            {
                try
                {
                    var converter = new CoordinateConverter();
                    converter.UpdateMetrics(metrics.Width, metrics.Height, origin);

                    // Test specific pixel positions within terminal bounds
                    var terminalPixelWidth = dimensions.Width * metrics.Width;
                    var terminalPixelHeight = dimensions.Height * metrics.Height;

                    // Generate a few test positions within bounds
                    var testPositions = new[]
                    {
                        origin, // Top-left corner
                        new float2(origin.X + terminalPixelWidth / 2, origin.Y + terminalPixelHeight / 2), // Center
                        new float2(origin.X + terminalPixelWidth - 1, origin.Y + terminalPixelHeight - 1), // Bottom-right (within bounds)
                        new float2(origin.X + metrics.Width, origin.Y + metrics.Height), // Second cell
                        new float2(origin.X + metrics.Width * 2, origin.Y + metrics.Height * 2) // Third cell
                    };

                    foreach (var pixelPos in testPositions)
                    {
                        // Skip positions that are outside terminal bounds
                        if (pixelPos.X >= origin.X + terminalPixelWidth ||
                            pixelPos.Y >= origin.Y + terminalPixelHeight)
                        {
                            continue;
                        }

                        // Test pixel-to-cell conversion
                        var cellCoords = converter.PixelToCell(pixelPos, dimensions.Width, dimensions.Height);

                        // Should always return valid coordinates for positions within bounds
                        if (!cellCoords.HasValue)
                        {
                            return false;
                        }

                        var (x1, y1) = cellCoords.Value;

                        // Coordinates should be 1-based and within terminal bounds
                        bool coordsValid = x1 >= 1 && x1 <= dimensions.Width &&
                                          y1 >= 1 && y1 <= dimensions.Height;

                        if (!coordsValid)
                        {
                            return false;
                        }

                        // Test round-trip conversion accuracy
                        var backToPixel = converter.CellToPixel(x1, y1);

                        // Calculate expected cell boundaries
                        float expectedCellLeft = origin.X + ((x1 - 1) * metrics.Width);
                        float expectedCellTop = origin.Y + ((y1 - 1) * metrics.Height);
                        float expectedCellRight = expectedCellLeft + metrics.Width;
                        float expectedCellBottom = expectedCellTop + metrics.Height;

                        // Original pixel should be within the cell boundaries
                        bool withinCellBounds = pixelPos.X >= expectedCellLeft && pixelPos.X < expectedCellRight &&
                                               pixelPos.Y >= expectedCellTop && pixelPos.Y < expectedCellBottom;

                        // CellToPixel should return the top-left corner of the cell
                        bool cellToPixelAccurate = Math.Abs(backToPixel.X - expectedCellLeft) < 0.001f &&
                                                  Math.Abs(backToPixel.Y - expectedCellTop) < 0.001f;

                        if (!withinCellBounds || !cellToPixelAccurate)
                        {
                            return false;
                        }

                        // Test consistency: same input should produce same output
                        var cellCoords2 = converter.PixelToCell(pixelPos, dimensions.Width, dimensions.Height);
                        bool consistent = cellCoords2.HasValue &&
                                         cellCoords2.Value.X1 == x1 &&
                                         cellCoords2.Value.Y1 == y1;

                        if (!consistent)
                        {
                            return false;
                        }
                    }

                    // Test metrics update consistency
                    float originalWidth = converter.CharacterWidth;
                    float originalHeight = converter.LineHeight;
                    float2 originalOrigin = converter.TerminalOrigin;

                    bool metricsMatch = Math.Abs(originalWidth - metrics.Width) < 0.001f &&
                                       Math.Abs(originalHeight - metrics.Height) < 0.001f &&
                                       Math.Abs(originalOrigin.X - origin.X) < 0.001f &&
                                       Math.Abs(originalOrigin.Y - origin.Y) < 0.001f;

                    return metricsMatch;
                }
                catch
                {
                    // Any exception indicates a problem with coordinate conversion
                    return false;
                }
            });
    }

    /// <summary>
    /// Generator for out-of-bounds pixel positions.
    /// Produces pixel coordinates that are outside terminal bounds.
    /// </summary>
    public static Arbitrary<float2> OutOfBoundsPixelPositions(float2 origin, float charWidth, float lineHeight, int terminalWidth, int terminalHeight)
    {
        var terminalPixelWidth = terminalWidth * charWidth;
        var terminalPixelHeight = terminalHeight * lineHeight;

        var outOfBoundsGen = Gen.OneOf(
            // Left of terminal
            Gen.Choose(-1000, -1).Select(x => new float2(origin.X + (float)x, origin.Y + terminalPixelHeight / 2)),
            // Right of terminal
            Gen.Choose(1, 1000).Select(x => new float2(origin.X + terminalPixelWidth + (float)x, origin.Y + terminalPixelHeight / 2)),
            // Above terminal
            Gen.Choose(-1000, -1).Select(y => new float2(origin.X + terminalPixelWidth / 2, origin.Y + (float)y)),
            // Below terminal
            Gen.Choose(1, 1000).Select(y => new float2(origin.X + terminalPixelWidth / 2, origin.Y + terminalPixelHeight + (float)y)),
            // Diagonal out of bounds
            Gen.Choose(-1000, -1).SelectMany(x => Gen.Choose(-1000, -1).Select(y => new float2(origin.X + (float)x, origin.Y + (float)y))),
            Gen.Choose(1, 1000).SelectMany(x => Gen.Choose(1, 1000).Select(y => new float2(origin.X + terminalPixelWidth + (float)x, origin.Y + terminalPixelHeight + (float)y)))
        );

        return outOfBoundsGen.ToArbitrary();
    }

    /// <summary>
    /// Property 9: Coordinate Boundary Handling
    /// For any mouse position outside terminal bounds, coordinate conversion should clamp to
    /// valid terminal ranges and handle gracefully without crashing.
    /// Feature: mouse-input-support, Property 9: Coordinate Boundary Handling
    /// Validates: Requirements R3.3, R3.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CoordinateBoundaryHandling_ShouldClampAndHandleGracefully()
    {
        return Prop.ForAll(
            ValidCharacterMetrics(),
            ValidTerminalOrigins(),
            ValidTerminalDimensions(),
            (metrics, origin, dimensions) =>
            {
                try
                {
                    var converter = new CoordinateConverter();
                    converter.UpdateMetrics(metrics.Width, metrics.Height, origin);

                    // Test out-of-bounds positions
                    var outOfBoundsGen = OutOfBoundsPixelPositions(origin, metrics.Width, metrics.Height, dimensions.Width, dimensions.Height);
                    var outOfBoundsPositions = outOfBoundsGen.Generator.Sample(0, 10);

                    foreach (var pixelPos in outOfBoundsPositions)
                    {
                        // Test that out-of-bounds conversion still returns valid coordinates (clamped)
                        var cellCoords = converter.PixelToCell(pixelPos, dimensions.Width, dimensions.Height);

                        // Should always return coordinates (clamped to bounds)
                        if (!cellCoords.HasValue)
                        {
                            return false;
                        }

                        var (x1, y1) = cellCoords.Value;

                        // Coordinates should be clamped to valid terminal bounds
                        bool coordsClamped = x1 >= 1 && x1 <= dimensions.Width &&
                                            y1 >= 1 && y1 <= dimensions.Height;

                        if (!coordsClamped)
                        {
                            return false;
                        }

                        // Test that clamped coordinates are at the boundary
                        bool atBoundary = (x1 == 1 || x1 == dimensions.Width) ||
                                         (y1 == 1 || y1 == dimensions.Height);

                        if (!atBoundary)
                        {
                            return false;
                        }

                        // Test IsWithinBounds method
                        var terminalSize = new float2(dimensions.Width * metrics.Width, dimensions.Height * metrics.Height);
                        bool withinBounds = converter.IsWithinBounds(pixelPos, terminalSize);

                        // Out-of-bounds positions should return false
                        if (withinBounds)
                        {
                            return false;
                        }
                    }

                    // Test edge cases at exact boundaries
                    var terminalPixelWidth = dimensions.Width * metrics.Width;
                    var terminalPixelHeight = dimensions.Height * metrics.Height;

                    var boundaryPositions = new[]
                    {
                        new float2(origin.X, origin.Y), // Top-left corner
                        new float2(origin.X + terminalPixelWidth - 1, origin.Y), // Top-right corner
                        new float2(origin.X, origin.Y + terminalPixelHeight - 1), // Bottom-left corner
                        new float2(origin.X + terminalPixelWidth - 1, origin.Y + terminalPixelHeight - 1), // Bottom-right corner
                        new float2(origin.X + terminalPixelWidth, origin.Y), // Just outside right edge
                        new float2(origin.X, origin.Y + terminalPixelHeight), // Just outside bottom edge
                        new float2(origin.X - 1, origin.Y), // Just outside left edge
                        new float2(origin.X, origin.Y - 1) // Just outside top edge
                    };

                    foreach (var boundaryPos in boundaryPositions)
                    {
                        var cellCoords = converter.PixelToCell(boundaryPos, dimensions.Width, dimensions.Height);

                        // Should always return valid coordinates
                        if (!cellCoords.HasValue)
                        {
                            return false;
                        }

                        var (x1, y1) = cellCoords.Value;

                        // Coordinates should be within valid bounds (clamped if necessary)
                        bool validBounds = x1 >= 1 && x1 <= dimensions.Width &&
                                          y1 >= 1 && y1 <= dimensions.Height;

                        if (!validBounds)
                        {
                            return false;
                        }

                        // Test IsWithinBounds for boundary positions
                        var terminalSize = new float2(terminalPixelWidth, terminalPixelHeight);
                        bool shouldBeWithinBounds = boundaryPos.X >= origin.X &&
                                                   boundaryPos.Y >= origin.Y &&
                                                   boundaryPos.X < origin.X + terminalPixelWidth &&
                                                   boundaryPos.Y < origin.Y + terminalPixelHeight;

                        bool actuallyWithinBounds = converter.IsWithinBounds(boundaryPos, terminalSize);

                        if (shouldBeWithinBounds != actuallyWithinBounds)
                        {
                            return false;
                        }
                    }

                    // Test error handling with invalid metrics
                    var errorConverter = new CoordinateConverter();

                    // Test with zero/negative metrics (should use fallback)
                    errorConverter.UpdateMetrics(0, 0, origin);
                    var fallbackCoords = errorConverter.PixelToCell(origin, dimensions.Width, dimensions.Height);

                    // Should still return valid coordinates using fallback metrics
                    bool fallbackWorks = fallbackCoords.HasValue &&
                                        fallbackCoords.Value.X1 >= 1 && fallbackCoords.Value.X1 <= dimensions.Width &&
                                        fallbackCoords.Value.Y1 >= 1 && fallbackCoords.Value.Y1 <= dimensions.Height;

                    if (!fallbackWorks)
                    {
                        return false;
                    }

                    // Test with negative metrics
                    errorConverter.UpdateMetrics(-10, -20, origin);
                    var negativeMetricsCoords = errorConverter.PixelToCell(origin, dimensions.Width, dimensions.Height);

                    // Should still return valid coordinates using fallback metrics
                    bool negativeMetricsHandled = negativeMetricsCoords.HasValue &&
                                                 negativeMetricsCoords.Value.X1 >= 1 && negativeMetricsCoords.Value.X1 <= dimensions.Width &&
                                                 negativeMetricsCoords.Value.Y1 >= 1 && negativeMetricsCoords.Value.Y1 <= dimensions.Height;

                    return negativeMetricsHandled;
                }
                catch
                {
                    // Should handle all errors gracefully without throwing exceptions
                    return false;
                }
            });
    }
}
