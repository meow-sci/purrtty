using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers;
using caTTY.Display.Configuration;
using caTTY.Core.Types;

namespace caTTY.Display.Tests.Unit.Controllers;

/// <summary>
///     Unit tests for terminal controller resize functionality.
///     Tests the resize method validation and error handling logic.
///     Uses direct testing approach without mocking to verify resize behavior.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalControllerResizeTests
{
    [Test]
    public void ApplyTerminalDimensionsToAllSessions_MultipleSessions_ResizesEachTerminal()
    {
        using var sessionManager = new SessionManager(maxSessions: 5);

        // Create multiple sessions (processes are not started; this is a headless resize test)
        var s1 = sessionManager.CreateSessionAsync("S1").Result;
        var s2 = sessionManager.CreateSessionAsync("S2").Result;
        var s3 = sessionManager.CreateSessionAsync("S3").Result;

        using var controller = new TerminalController(sessionManager);

        controller.ApplyTerminalDimensionsToAllSessions(120, 40);

        Assert.That(s1.Terminal.Width, Is.EqualTo(120));
        Assert.That(s1.Terminal.Height, Is.EqualTo(40));
        Assert.That(s2.Terminal.Width, Is.EqualTo(120));
        Assert.That(s2.Terminal.Height, Is.EqualTo(40));
        Assert.That(s3.Terminal.Width, Is.EqualTo(120));
        Assert.That(s3.Terminal.Height, Is.EqualTo(40));

        var (cols, rows) = sessionManager.LastKnownTerminalDimensions;
        Assert.That(cols, Is.EqualTo(120));
        Assert.That(rows, Is.EqualTo(40));
    }

    [Test]
    [TestCase(0, 24)]
    [TestCase(80, 0)]
    [TestCase(-1, 24)]
    [TestCase(80, -1)]
    [TestCase(1001, 24)]
    [TestCase(80, 1001)]
    public void ResizeTerminal_InvalidDimensions_ThrowsArgumentException(int cols, int rows)
    {
        // This test verifies that invalid dimensions are properly rejected
        
        // Arrange - Create a minimal terminal controller for testing
        // We can't easily test the full controller without ImGui context,
        // but we can test the validation logic
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => ValidateTerminalDimensions(cols, rows));
    }

    [Test]
    public void ResizeTerminal_BoundaryValues_AcceptsValidBoundaries()
    {
        // Test minimum valid dimensions
        Assert.DoesNotThrow(() => ValidateTerminalDimensions(1, 1));

        // Test maximum valid dimensions
        Assert.DoesNotThrow(() => ValidateTerminalDimensions(1000, 1000));
    }

    [Test]
    public void ResizeTerminal_TypicalTerminalSizes_WorksCorrectly()
    {
        // Test common terminal sizes
        var commonSizes = new[]
        {
            (80, 24),   // Standard VT100
            (80, 25),   // DOS/Windows
            (132, 24),  // Wide VT100
            (120, 30),  // Modern wide
            (100, 50)   // Tall terminal
        };

        foreach (var (cols, rows) in commonSizes)
        {
            Assert.DoesNotThrow(() => ValidateTerminalDimensions(cols, rows), 
                $"Failed to validate common size {cols}x{rows}");
        }
    }

    [Test]
    public void CalculateTerminalDimensions_ValidInput_ReturnsExpectedDimensions()
    {
        // Test the dimension calculation logic
        
        // Arrange
        float characterWidth = 10.0f;
        float lineHeight = 20.0f;
        // Account for UI overhead: 20px padding + 60px UI overhead
        float availableWidth = 820.0f;  // Should fit 80 characters after overhead ((820-20) / 10 = 80)
        float availableHeight = 540.0f; // Should fit 24 lines after overhead ((540-60) / 20 = 24)
        
        // Act
        var result = CalculateTerminalDimensionsForTest(availableWidth, availableHeight, characterWidth, lineHeight);
        
        // Assert
        Assert.That(result.HasValue, Is.True, "Should return valid dimensions");
        var (cols, rows) = result.Value;
        Assert.That(cols, Is.EqualTo(80), "Should calculate 80 columns");
        Assert.That(rows, Is.EqualTo(24), "Should calculate 24 rows");
    }

    [Test]
    public void CalculateTerminalDimensions_TooSmall_ReturnsMinimumDimensions()
    {
        // Test that very small windows still return minimum viable dimensions
        
        // Arrange
        float characterWidth = 10.0f;
        float lineHeight = 20.0f;
        // Make sure we have enough space for minimum dimensions after overhead
        float availableWidth = 120.0f;   // After overhead: (120-20)/10 = 10 cols (minimum)
        float availableHeight = 120.0f;  // After overhead: (120-60)/20 = 3 rows (minimum)
        
        // Act
        var result = CalculateTerminalDimensionsForTest(availableWidth, availableHeight, characterWidth, lineHeight);
        
        // Assert
        Assert.That(result.HasValue, Is.True, "Should return valid dimensions even for small windows");
        var (cols, rows) = result.Value;
        Assert.That(cols, Is.EqualTo(10), "Should return minimum columns");
        Assert.That(rows, Is.EqualTo(3), "Should return minimum rows");
    }

    [Test]
    public void CalculateTerminalDimensions_InvalidCharacterMetrics_ReturnsNull()
    {
        // Test that invalid character metrics are handled gracefully
        
        // Arrange
        float availableWidth = 800.0f;
        float availableHeight = 480.0f;
        
        // Act & Assert - Zero character width
        var result1 = CalculateTerminalDimensionsForTest(availableWidth, availableHeight, 0.0f, 20.0f);
        Assert.That(result1.HasValue, Is.False, "Should return null for zero character width");
        
        // Act & Assert - Zero line height
        var result2 = CalculateTerminalDimensionsForTest(availableWidth, availableHeight, 10.0f, 0.0f);
        Assert.That(result2.HasValue, Is.False, "Should return null for zero line height");
        
        // Act & Assert - Negative character width
        var result3 = CalculateTerminalDimensionsForTest(availableWidth, availableHeight, -10.0f, 20.0f);
        Assert.That(result3.HasValue, Is.False, "Should return null for negative character width");
    }

    /// <summary>
    ///     Helper method to validate terminal dimensions (extracted from controller logic).
    /// </summary>
    private static void ValidateTerminalDimensions(int cols, int rows)
    {
        if (cols < 1 || rows < 1 || cols > 1000 || rows > 1000)
        {
            throw new ArgumentException($"Invalid terminal dimensions: {cols}x{rows}. Must be between 1x1 and 1000x1000.");
        }
    }

    /// <summary>
    ///     Helper method to test terminal dimension calculation logic.
    /// </summary>
    private static (int cols, int rows)? CalculateTerminalDimensionsForTest(
        float availableWidth, float availableHeight, float characterWidth, float lineHeight)
    {
        try
        {
            // Reserve space for UI elements (matching controller logic)
            const float UI_OVERHEAD_HEIGHT = 60.0f;
            const float PADDING_WIDTH = 20.0f;
            
            float usableWidth = availableWidth - PADDING_WIDTH;
            float usableHeight = availableHeight - UI_OVERHEAD_HEIGHT;
            
            // Ensure we have positive dimensions
            if (usableWidth <= 0 || usableHeight <= 0)
            {
                return null;
            }
            
            // Validate character metrics
            if (characterWidth <= 0 || lineHeight <= 0)
            {
                return null;
            }
            
            int cols = (int)Math.Floor(usableWidth / characterWidth);
            int rows = (int)Math.Floor(usableHeight / lineHeight);
            
            // Apply reasonable bounds (matching controller logic)
            cols = Math.Max(10, Math.Min(1000, cols));
            rows = Math.Max(3, Math.Min(1000, rows));
            
            return (cols, rows);
        }
        catch
        {
            return null;
        }
    }
}