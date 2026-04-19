using Brutal.Numerics;
using caTTY.Core.Types;
using caTTY.Display.Performance;
using caTTY.Display.Rendering;
using NUnit.Framework;
using TerminalColor = caTTY.Core.Types.Color;

namespace caTTY.Display.Tests.Unit.Rendering;

/// <summary>
/// Unit tests for CachedColorResolver performance optimization.
/// Verifies caching behavior, cache invalidation on theme changes, and correctness of resolved colors.
/// </summary>
[TestFixture]
[Category("Unit")]
public class CachedColorResolverTests
{
    private PerformanceStopwatch _perfWatch = null!;
    private ColorResolver _baseResolver = null!;
    private CachedColorResolver _cachedResolver = null!;

    [SetUp]
    public void SetUp()
    {
        // Initialize theme manager to ensure consistent state
        ThemeManager.InitializeThemes();
        ThemeManager.ApplyTheme("Default");
        
        _perfWatch = new PerformanceStopwatch { Enabled = false };
        _baseResolver = new ColorResolver(_perfWatch);
        _cachedResolver = new CachedColorResolver(_baseResolver, _perfWatch);
    }

    [TearDown]
    public void TearDown()
    {
        _cachedResolver?.Dispose();
    }

    #region Default Color Caching Tests

    [Test]
    public void Resolve_NullForegroundColor_ReturnsCachedDefaultForeground()
    {
        // Arrange - null color represents default
        TerminalColor? nullColor = null;

        // Act
        var result1 = _cachedResolver.Resolve(nullColor, isBackground: false);
        var result2 = _cachedResolver.Resolve(nullColor, isBackground: false);
        
        // Assert - Both should return same cached result
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(result1, Is.EqualTo(ThemeManager.GetDefaultForeground()));
    }

    [Test]
    public void Resolve_NullBackgroundColor_ReturnsCachedDefaultBackground()
    {
        // Arrange - null color represents default
        TerminalColor? nullColor = null;

        // Act
        var result1 = _cachedResolver.Resolve(nullColor, isBackground: true);
        var result2 = _cachedResolver.Resolve(nullColor, isBackground: true);
        
        // Assert - Both should return same cached result
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(result1, Is.EqualTo(ThemeManager.GetDefaultBackground()));
    }

    [Test]
    public void GetCachedDefaultForeground_ReturnsThemeForeground()
    {
        // Act
        var result = _cachedResolver.GetCachedDefaultForeground();
        
        // Assert
        Assert.That(result, Is.EqualTo(ThemeManager.GetDefaultForeground()));
    }

    [Test]
    public void GetCachedDefaultBackground_ReturnsThemeBackground()
    {
        // Act
        var result = _cachedResolver.GetCachedDefaultBackground();
        
        // Assert
        Assert.That(result, Is.EqualTo(ThemeManager.GetDefaultBackground()));
    }

    #endregion

    #region Named Color Caching Tests

    [Test]
    public void Resolve_NamedColor_ReturnsCachedValue()
    {
        // Arrange
        var redColor = new TerminalColor(NamedColor.Red);

        // Act
        var result1 = _cachedResolver.Resolve(redColor, isBackground: false);
        var result2 = _cachedResolver.Resolve(redColor, isBackground: false);
        
        // Assert
        Assert.That(result1, Is.EqualTo(result2));
        Assert.That(result1, Is.EqualTo(ThemeManager.ResolveThemeColor(1))); // Red is index 1
    }

    [Test]
    public void Resolve_AllNamedColors_ReturnCorrectThemeColors()
    {
        // Test all 16 standard ANSI colors
        var namedColors = new[]
        {
            (NamedColor.Black, 0),
            (NamedColor.Red, 1),
            (NamedColor.Green, 2),
            (NamedColor.Yellow, 3),
            (NamedColor.Blue, 4),
            (NamedColor.Magenta, 5),
            (NamedColor.Cyan, 6),
            (NamedColor.White, 7),
            (NamedColor.BrightBlack, 8),
            (NamedColor.BrightRed, 9),
            (NamedColor.BrightGreen, 10),
            (NamedColor.BrightYellow, 11),
            (NamedColor.BrightBlue, 12),
            (NamedColor.BrightMagenta, 13),
            (NamedColor.BrightCyan, 14),
            (NamedColor.BrightWhite, 15)
        };

        foreach (var (namedColor, themeIndex) in namedColors)
        {
            var color = new TerminalColor(namedColor);
            var result = _cachedResolver.Resolve(color, isBackground: false);
            var expected = ThemeManager.ResolveThemeColor(themeIndex);
            
            Assert.That(result, Is.EqualTo(expected), 
                $"Named color {namedColor} (index {themeIndex}) did not match expected theme color");
        }
    }

    #endregion

    #region Indexed Color Caching Tests

    [Test]
    public void Resolve_IndexedColor_LowRange_UsesCachedNamedColors()
    {
        // Indices 0-15 should use the cached named colors (theme-dependent)
        for (byte i = 0; i <= 15; i++)
        {
            var color = new TerminalColor(i);
            var result = _cachedResolver.Resolve(color, isBackground: false);
            var expected = ThemeManager.ResolveThemeColor(i);
            
            Assert.That(result, Is.EqualTo(expected), 
                $"Indexed color {i} did not match expected theme color");
        }
    }

    [Test]
    public void Resolve_IndexedColor_CubeRange_ReturnsCachedValue()
    {
        // Test 6x6x6 color cube (indices 16-231)
        var color = new TerminalColor((byte)51); // A color in the cube range
        
        var result1 = _cachedResolver.Resolve(color, isBackground: false);
        var result2 = _cachedResolver.Resolve(color, isBackground: false);
        
        // Verify consistency
        Assert.That(result1, Is.EqualTo(result2));
        
        // Verify it's a valid color (not default)
        Assert.That(result1.W, Is.EqualTo(1.0f)); // Alpha should be 1
    }

    [Test]
    public void Resolve_IndexedColor_GrayscaleRange_ReturnsCachedValue()
    {
        // Test grayscale ramp (indices 232-255)
        var color = new TerminalColor((byte)240); // A grayscale color
        
        var result1 = _cachedResolver.Resolve(color, isBackground: false);
        var result2 = _cachedResolver.Resolve(color, isBackground: false);
        
        // Verify consistency
        Assert.That(result1, Is.EqualTo(result2));
        
        // Grayscale should have R=G=B
        Assert.That(result1.X, Is.EqualTo(result1.Y).Within(0.001f));
        Assert.That(result1.Y, Is.EqualTo(result1.Z).Within(0.001f));
    }

    #endregion

    #region RGB Color Caching Tests

    [Test]
    public void Resolve_RgbColor_ReturnsCachedValue()
    {
        // Arrange
        var rgbColor = new TerminalColor(128, 64, 192);

        // Act
        var result1 = _cachedResolver.Resolve(rgbColor, isBackground: false);
        var result2 = _cachedResolver.Resolve(rgbColor, isBackground: false);
        
        // Assert - Same RGB should return same cached result
        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void Resolve_RgbColor_ReturnsCorrectConversion()
    {
        // Arrange
        var rgbColor = new TerminalColor(255, 128, 64);

        // Act
        var result = _cachedResolver.Resolve(rgbColor, isBackground: false);
        
        // Assert
        Assert.That(result.X, Is.EqualTo(255f / 255f).Within(0.001f)); // R
        Assert.That(result.Y, Is.EqualTo(128f / 255f).Within(0.001f)); // G
        Assert.That(result.Z, Is.EqualTo(64f / 255f).Within(0.001f));  // B
        Assert.That(result.W, Is.EqualTo(1.0f));                       // A
    }

    [Test]
    public void Resolve_DifferentRgbColors_ReturnsDifferentValues()
    {
        // Arrange
        var color1 = new TerminalColor(255, 0, 0);
        var color2 = new TerminalColor(0, 255, 0);
        var color3 = new TerminalColor(0, 0, 255);

        // Act
        var result1 = _cachedResolver.Resolve(color1, isBackground: false);
        var result2 = _cachedResolver.Resolve(color2, isBackground: false);
        var result3 = _cachedResolver.Resolve(color3, isBackground: false);
        
        // Assert - Different colors should return different values
        Assert.That(result1, Is.Not.EqualTo(result2));
        Assert.That(result2, Is.Not.EqualTo(result3));
        Assert.That(result1, Is.Not.EqualTo(result3));
    }

    #endregion

    #region Theme Change Cache Invalidation Tests

    [Test]
    public void ThemeChange_InvalidatesDefaultColorCache()
    {
        // Arrange - Cache some default colors
        var initialForeground = _cachedResolver.GetCachedDefaultForeground();
        var stats1 = _cachedResolver.GetCacheStats();
        Assert.That(stats1.DefaultColorsValid, Is.True);

        // Act - Change theme
        ThemeManager.ApplyTheme("Default Light");
        
        // Assert - Cache should be invalidated
        var stats2 = _cachedResolver.GetCacheStats();
        Assert.That(stats2.DefaultColorsValid, Is.False);
        
        // New resolution should return updated theme colors
        var newForeground = _cachedResolver.GetCachedDefaultForeground();
        Assert.That(newForeground, Is.Not.EqualTo(initialForeground));
        
        // Restore original theme for other tests
        ThemeManager.ApplyTheme("Default");
    }

    [Test]
    public void ThemeChange_InvalidatesNamedColorCache()
    {
        // Arrange - Cache a named color
        var redColor = new TerminalColor(NamedColor.Red);
        var initialRed = _cachedResolver.Resolve(redColor, isBackground: false);
        var stats1 = _cachedResolver.GetCacheStats();
        Assert.That(stats1.NamedColorsValid, Is.True);

        // Act - Change theme
        ThemeManager.ApplyTheme("Default Light");
        
        // Assert - Named color cache should be invalidated
        var stats2 = _cachedResolver.GetCacheStats();
        Assert.That(stats2.NamedColorsValid, Is.False);
        
        // Restore original theme for other tests
        ThemeManager.ApplyTheme("Default");
    }

    [Test]
    public void ThemeChange_DoesNotInvalidateIndexedColorCache()
    {
        // Arrange - Cache an indexed color (16-255 range, not theme-dependent)
        var indexedColor = new TerminalColor((byte)100);
        _ = _cachedResolver.Resolve(indexedColor, isBackground: false);
        var stats1 = _cachedResolver.GetCacheStats();
        Assert.That(stats1.IndexedColorsInitialized, Is.True);

        // Act - Change theme
        ThemeManager.ApplyTheme("Default Light");
        
        // Assert - Indexed color cache should still be valid
        var stats2 = _cachedResolver.GetCacheStats();
        Assert.That(stats2.IndexedColorsInitialized, Is.True);
        
        // Restore original theme for other tests
        ThemeManager.ApplyTheme("Default");
    }

    #endregion

    #region Cache Statistics Tests

    [Test]
    public void GetCacheStats_InitialState_ShowsEmptyCache()
    {
        // Arrange - Fresh resolver
        using var freshResolver = new CachedColorResolver(_baseResolver, _perfWatch);
        
        // Act
        var stats = freshResolver.GetCacheStats();
        
        // Assert - Initial state after construction (caches are primed but RGB cache is empty)
        Assert.That(stats.RgbCacheSize, Is.EqualTo(0));
    }

    [Test]
    public void GetCacheStats_AfterResolvingRgbColors_ShowsPopulatedCache()
    {
        // Arrange - Resolve several RGB colors
        for (byte r = 0; r < 10; r++)
        {
            var color = new TerminalColor(r, (byte)(r * 10), (byte)(r * 20));
            _ = _cachedResolver.Resolve(color, isBackground: false);
        }
        
        // Act
        var stats = _cachedResolver.GetCacheStats();
        
        // Assert
        Assert.That(stats.RgbCacheSize, Is.EqualTo(10));
    }

    [Test]
    public void ClearAllCaches_ResetsAllCacheState()
    {
        // Arrange - Populate caches
        _ = _cachedResolver.GetCachedDefaultForeground();
        _ = _cachedResolver.Resolve(new TerminalColor(NamedColor.Red), isBackground: false);
        _ = _cachedResolver.Resolve(new TerminalColor((byte)100), isBackground: false);
        _ = _cachedResolver.Resolve(new TerminalColor(255, 128, 64), isBackground: false);
        
        var statsBefore = _cachedResolver.GetCacheStats();
        Assert.That(statsBefore.DefaultColorsValid, Is.True);
        Assert.That(statsBefore.NamedColorsValid, Is.True);
        Assert.That(statsBefore.IndexedColorsInitialized, Is.True);
        Assert.That(statsBefore.RgbCacheSize, Is.GreaterThan(0));
        
        // Act
        _cachedResolver.ClearAllCaches();
        
        // Assert
        var statsAfter = _cachedResolver.GetCacheStats();
        Assert.That(statsAfter.DefaultColorsValid, Is.False);
        Assert.That(statsAfter.NamedColorsValid, Is.False);
        Assert.That(statsAfter.IndexedColorsInitialized, Is.False);
        Assert.That(statsAfter.RgbCacheSize, Is.EqualTo(0));
    }

    #endregion

    #region Consistency Tests

    [Test]
    public void Resolve_MatchesBaseResolverOutput()
    {
        // Test various color types to ensure cached resolver produces same output as base resolver
        var testCases = new TerminalColor?[]
        {
            null, // Default
            new TerminalColor(NamedColor.Red),
            new TerminalColor(NamedColor.BrightCyan),
            new TerminalColor((byte)0),  // Indexed - theme-dependent
            new TerminalColor((byte)15), // Indexed - theme-dependent
            new TerminalColor((byte)100), // Indexed - cube
            new TerminalColor((byte)240), // Indexed - grayscale
            new TerminalColor(255, 0, 0),
            new TerminalColor(0, 255, 0),
            new TerminalColor(128, 128, 128)
        };

        foreach (var color in testCases)
        {
            var baseResult = _baseResolver.Resolve(color, isBackground: false);
            var cachedResult = _cachedResolver.Resolve(color, isBackground: false);
            
            Assert.That(cachedResult.X, Is.EqualTo(baseResult.X).Within(0.001f), 
                $"R component mismatch for {color}");
            Assert.That(cachedResult.Y, Is.EqualTo(baseResult.Y).Within(0.001f), 
                $"G component mismatch for {color}");
            Assert.That(cachedResult.Z, Is.EqualTo(baseResult.Z).Within(0.001f), 
                $"B component mismatch for {color}");
            Assert.That(cachedResult.W, Is.EqualTo(baseResult.W).Within(0.001f), 
                $"A component mismatch for {color}");
        }
    }

    [Test]
    public void Resolve_IsBackgroundFlag_ProducesCorrectDefaults()
    {
        // Arrange
        TerminalColor? nullColor = null;
        
        // Act
        var foreground = _cachedResolver.Resolve(nullColor, isBackground: false);
        var background = _cachedResolver.Resolve(nullColor, isBackground: true);
        
        // Assert
        Assert.That(foreground, Is.EqualTo(ThemeManager.GetDefaultForeground()));
        Assert.That(background, Is.EqualTo(ThemeManager.GetDefaultBackground()));
        Assert.That(foreground, Is.Not.EqualTo(background));
    }

    #endregion
}
