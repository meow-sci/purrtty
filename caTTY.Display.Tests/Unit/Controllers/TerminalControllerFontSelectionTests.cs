using NUnit.Framework;
using caTTY.Display.Configuration;
using caTTY.Display.Types;
using caTTY.Display.Rendering;
using System.Linq;

namespace caTTY.Display.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for font selection functionality supporting TerminalController.
/// Tests CaTTYFontManager methods used by font selection UI.
/// Validates Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 4.1, 4.2, 4.3, 4.4, 4.5, 6.1, 6.2, 6.3, 6.4, 6.5.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalControllerFontSelectionTests
{
    [SetUp]
    public void SetUp()
    {
        // Ensure font registry is initialized
        CaTTYFontManager.LoadFonts();
    }

    [Test]
    public void GetAvailableFontFamilies_ShouldReturnRegisteredFonts()
    {
        // Act
        var availableFonts = CaTTYFontManager.GetAvailableFontFamilies();
        
        // Assert
        Assert.That(availableFonts, Is.Not.Null, "Available fonts should not be null");
        Assert.That(availableFonts.Count, Is.GreaterThan(0), "Should have at least one registered font");
        Assert.That(availableFonts, Does.Contain("Hack"), "Should contain default Hack font");
    }

    [Test]
    public void CreateFontConfigForFamily_WithValidFontFamily_ShouldReturnValidConfig()
    {
        // Arrange
        var fontFamily = "Hack";
        var fontSize = 14.0f;
        
        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, fontSize);
        
        // Assert
        Assert.That(config, Is.Not.Null, "Font configuration should not be null");
        Assert.That(config.FontSize, Is.EqualTo(fontSize), "Font size should match requested size");
        Assert.That(config.RegularFontName, Is.Not.Null.And.Not.Empty, "Regular font name should be set");
        Assert.That(config.BoldFontName, Is.Not.Null.And.Not.Empty, "Bold font name should be set");
        Assert.That(config.ItalicFontName, Is.Not.Null.And.Not.Empty, "Italic font name should be set");
        Assert.That(config.BoldItalicFontName, Is.Not.Null.And.Not.Empty, "Bold italic font name should be set");
    }

    [Test]
    public void CreateFontConfigForFamily_WithDifferentFontSizes_ShouldPreserveSize()
    {
        // Arrange
        var fontFamily = "Hack";
        var testSizes = new[] { 8.0f, 12.0f, 16.0f, 20.0f, 24.0f };
        
        foreach (var fontSize in testSizes)
        {
            // Act
            var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, fontSize);
            
            // Assert
            Assert.That(config.FontSize, Is.EqualTo(fontSize), 
                $"Font size should be {fontSize} for created configuration");
        }
    }

    [Test]
    public void CreateFontConfigForFamily_WithAllRegisteredFonts_ShouldSucceed()
    {
        // Arrange
        var availableFonts = CaTTYFontManager.GetAvailableFontFamilies();
        var fontSize = 14.0f;
        
        // Act & Assert
        foreach (var fontFamily in availableFonts)
        {
            Assert.DoesNotThrow(() => 
            {
                var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, fontSize);
                Assert.That(config, Is.Not.Null, $"Configuration should be created for {fontFamily}");
                Assert.That(config.FontSize, Is.EqualTo(fontSize), $"Font size should be preserved for {fontFamily}");
            }, $"Should create valid configuration for registered font: {fontFamily}");
        }
    }

    [Test]
    public void GetCurrentFontFamily_WithKnownFontConfig_ShouldReturnCorrectFamily()
    {
        // Arrange
        var expectedFamily = "Jet Brains Mono";
        var config = CaTTYFontManager.CreateFontConfigForFamily(expectedFamily, 14.0f);
        
        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);
        
        // Assert
        Assert.That(detectedFamily, Is.EqualTo(expectedFamily), 
            "Should detect the correct font family from configuration");
    }

    [Test]
    public void CreateFontConfigForFamily_WithInvalidFontFamily_ShouldHandleGracefully()
    {
        // Arrange
        var invalidFontFamily = "NonExistentFont";
        var fontSize = 14.0f;
        
        // Act & Assert
        // Should either return a fallback configuration or handle gracefully
        Assert.DoesNotThrow(() => 
        {
            var config = CaTTYFontManager.CreateFontConfigForFamily(invalidFontFamily, fontSize);
            // If it returns a config, it should be valid (uses default font size 32.0f)
            if (config != null)
            {
                Assert.That(config.FontSize, Is.EqualTo(32.0f), "Should use default font size for invalid family");
            }
        }, "Should handle invalid font family gracefully");
    }

    [Test]
    public void CreateFontConfigForFamily_WithNullFontFamily_ShouldHandleGracefully()
    {
        // Arrange
        string? nullFontFamily = null;
        var fontSize = 14.0f;
        
        // Act & Assert
        Assert.DoesNotThrow(() => 
        {
            var config = CaTTYFontManager.CreateFontConfigForFamily(nullFontFamily!, fontSize);
            // If it returns a config, it should be valid (uses default font size 32.0f)
            if (config != null)
            {
                Assert.That(config.FontSize, Is.EqualTo(32.0f), "Should use default font size for null family");
            }
        }, "Should handle null font family gracefully");
    }

    [Test]
    public void CreateFontConfigForFamily_WithEmptyFontFamily_ShouldHandleGracefully()
    {
        // Arrange
        var emptyFontFamily = "";
        var fontSize = 14.0f;
        
        // Act & Assert
        Assert.DoesNotThrow(() => 
        {
            var config = CaTTYFontManager.CreateFontConfigForFamily(emptyFontFamily, fontSize);
            // If it returns a config, it should be valid (uses default font size 32.0f)
            if (config != null)
            {
                Assert.That(config.FontSize, Is.EqualTo(32.0f), "Should use default font size for empty family");
            }
        }, "Should handle empty font family gracefully");
    }

    [Test]
    public void FontConfiguration_Validation_ShouldAcceptValidConfigs()
    {
        // Arrange
        var validFontFamilies = CaTTYFontManager.GetAvailableFontFamilies();
        var fontSize = 14.0f;
        
        foreach (var fontFamily in validFontFamilies)
        {
            // Act
            var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, fontSize);
            
            // Assert
            Assert.DoesNotThrow(() => 
            {
                config.Validate();
            }, $"Valid configuration for {fontFamily} should pass validation");
        }
    }

    [Test]
    public void GetCurrentFontFamily_WithUnknownFontConfig_ShouldReturnNull()
    {
        // Arrange
        var unknownConfig = new TerminalFontConfig
        {
            RegularFontName = "UnknownFont-Regular",
            BoldFontName = "UnknownFont-Bold",
            ItalicFontName = "UnknownFont-Italic",
            BoldItalicFontName = "UnknownFont-BoldItalic",
            FontSize = 14.0f
        };
        
        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(unknownConfig);
        
        // Assert
        Assert.That(detectedFamily, Is.Null, 
            "Should return null for unknown font configuration");
    }

    [Test]
    public void CreateFontConfigForFamily_WithInvalidFontSize_ShouldPreserveInvalidSize()
    {
        // Arrange
        var fontFamily = "Hack";
        var invalidSizes = new[] { -1.0f, 0.0f };
        
        foreach (var invalidSize in invalidSizes)
        {
            // Act & Assert
            Assert.DoesNotThrow(() => 
            {
                var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, invalidSize);
                // The method preserves the invalid size as-is
                if (config != null)
                {
                    Assert.That(config.FontSize, Is.EqualTo(invalidSize), 
                        $"Font size should be preserved as-is for input {invalidSize}");
                }
            }, $"Should handle invalid font size without throwing: {invalidSize}");
        }
        
        // Test special float values separately as they may behave differently
        var specialSizes = new[] { float.NaN, float.PositiveInfinity };
        foreach (var specialSize in specialSizes)
        {
            Assert.DoesNotThrow(() => 
            {
                var config = CaTTYFontManager.CreateFontConfigForFamily(fontFamily, specialSize);
                // Just verify it doesn't throw, behavior may vary for special values
            }, $"Should handle special float value without throwing: {specialSize}");
        }
    }

    [Test]
    public void FontSelection_RoundTrip_ShouldMaintainConsistency()
    {
        // Arrange
        var availableFonts = CaTTYFontManager.GetAvailableFontFamilies();
        var fontSize = 16.0f;
        
        foreach (var originalFamily in availableFonts.Take(3)) // Test first 3 fonts for performance
        {
            // Act
            var config = CaTTYFontManager.CreateFontConfigForFamily(originalFamily, fontSize);
            var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);
            
            // Assert
            Assert.That(detectedFamily, Is.EqualTo(originalFamily), 
                $"Round-trip should maintain font family consistency for {originalFamily}");
            Assert.That(config.FontSize, Is.EqualTo(fontSize), 
                $"Font size should be preserved for {originalFamily}");
        }
    }

    [Test]
    public void FontManager_LoadFonts_ShouldBeIdempotent()
    {
        // Arrange
        var initialFonts = CaTTYFontManager.GetAvailableFontFamilies();
        
        // Act
        CaTTYFontManager.LoadFonts(); // Call again
        var fontsAfterReload = CaTTYFontManager.GetAvailableFontFamilies();
        
        // Assert
        Assert.That(fontsAfterReload.Count, Is.EqualTo(initialFonts.Count), 
            "Font count should remain the same after reloading");
        Assert.That(fontsAfterReload, Is.EquivalentTo(initialFonts), 
            "Available fonts should be identical after reloading");
    }
}