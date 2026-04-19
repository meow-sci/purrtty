using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Rendering;

/// <summary>
/// Unit tests for CaTTYFontManager font configuration generation and font family detection.
/// Tests specific examples and edge cases for font configuration functionality.
/// </summary>
[TestFixture]
[Category("Unit")]
public class CaTTYFontManagerTests
{
    [SetUp]
    public void SetUp()
    {
        // Ensure font registry is initialized before each test
        CaTTYFontManager.LoadFonts();
    }

    #region CreateFontConfigForFamily Tests

    [Test]
    public void CreateFontConfigForFamily_WithFontHavingAll4Variants_ShouldUseAppropriateVariants()
    {
        // Arrange
        const string displayName = "Jet Brains Mono";
        const float fontSize = 24.0f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.RegularFontName, Is.EqualTo("JetBrainsMonoNerdFontMono-Regular"));
        Assert.That(config.BoldFontName, Is.EqualTo("JetBrainsMonoNerdFontMono-Bold"));
        Assert.That(config.ItalicFontName, Is.EqualTo("JetBrainsMonoNerdFontMono-Italic"));
        Assert.That(config.BoldItalicFontName, Is.EqualTo("JetBrainsMonoNerdFontMono-BoldItalic"));
        Assert.That(config.FontSize, Is.EqualTo(fontSize));
        Assert.That(config.AutoDetectContext, Is.False);
    }

    [Test]
    public void CreateFontConfigForFamily_WithFontHavingOnlyRegularVariant_ShouldUseRegularForAllStyles()
    {
        // Arrange
        const string displayName = "Pro Font";
        const float fontSize = 18.0f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.RegularFontName, Is.EqualTo("ProFontWindowsNerdFontMono-Regular"));
        Assert.That(config.BoldFontName, Is.EqualTo("ProFontWindowsNerdFontMono-Regular"));
        Assert.That(config.ItalicFontName, Is.EqualTo("ProFontWindowsNerdFontMono-Regular"));
        Assert.That(config.BoldItalicFontName, Is.EqualTo("ProFontWindowsNerdFontMono-Regular"));
        Assert.That(config.FontSize, Is.EqualTo(fontSize));
        Assert.That(config.AutoDetectContext, Is.False);
    }

    [Test]
    public void CreateFontConfigForFamily_WithUnknownFontFamily_ShouldReturnDefaultConfiguration()
    {
        // Arrange
        const string displayName = "NonExistentFont";
        const float fontSize = 20.0f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize);

        // Assert
        Assert.That(config, Is.Not.Null);
        
        // Should match default configuration from CreateForTestApp
        var defaultConfig = TerminalFontConfig.CreateForTestApp();
        Assert.That(config.RegularFontName, Is.EqualTo(defaultConfig.RegularFontName));
        Assert.That(config.BoldFontName, Is.EqualTo(defaultConfig.BoldFontName));
        Assert.That(config.ItalicFontName, Is.EqualTo(defaultConfig.ItalicFontName));
        Assert.That(config.BoldItalicFontName, Is.EqualTo(defaultConfig.BoldItalicFontName));
    }

    [Test]
    public void CreateFontConfigForFamily_WithDefaultFontSize_ShouldUse32Point()
    {
        // Arrange
        const string displayName = "Hack";

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.FontSize, Is.EqualTo(32.0f));
    }

    [Test]
    public void CreateFontConfigForFamily_WithCustomFontSize_ShouldUseSpecifiedSize()
    {
        // Arrange
        const string displayName = "Space Mono";
        const float customSize = 14.5f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, customSize);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.FontSize, Is.EqualTo(customSize));
    }

    [Test]
    public void CreateFontConfigForFamily_GeneratedConfig_ShouldPassValidation()
    {
        // Arrange
        const string displayName = "Hack";
        const float fontSize = 16.0f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.DoesNotThrow(() => config.Validate());
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateFontConfigForFamily_WithInvalidDisplayName_ShouldReturnDefaultConfiguration(string invalidName)
    {
        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(invalidName, 20.0f);

        // Assert
        Assert.That(config, Is.Not.Null);
        
        // Should match default configuration
        var defaultConfig = TerminalFontConfig.CreateForTestApp();
        Assert.That(config.RegularFontName, Is.EqualTo(defaultConfig.RegularFontName));
    }

    [Test]
    public void CreateFontConfigForFamily_WithNullDisplayName_ShouldReturnDefaultConfiguration()
    {
        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(null!, 20.0f);

        // Assert
        Assert.That(config, Is.Not.Null);
        
        // Should match default configuration
        var defaultConfig = TerminalFontConfig.CreateForTestApp();
        Assert.That(config.RegularFontName, Is.EqualTo(defaultConfig.RegularFontName));
    }

    #endregion

    #region GetCurrentFontFamily Tests

    [Test]
    public void GetCurrentFontFamily_WithValidConfigForRegisteredFont_ShouldReturnDisplayName()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "HackNerdFontMono-Regular",
            BoldFontName = "HackNerdFontMono-Bold",
            ItalicFontName = "HackNerdFontMono-Italic",
            BoldItalicFontName = "HackNerdFontMono-BoldItalic",
            FontSize = 32.0f,
            AutoDetectContext = false
        };

        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);

        // Assert
        Assert.That(detectedFamily, Is.EqualTo("Hack"));
    }

    [Test]
    public void GetCurrentFontFamily_WithConfigForFontWithOnlyRegular_ShouldReturnDisplayName()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "ProFontWindowsNerdFontMono-Regular",
            BoldFontName = "ProFontWindowsNerdFontMono-Regular",
            ItalicFontName = "ProFontWindowsNerdFontMono-Regular",
            BoldItalicFontName = "ProFontWindowsNerdFontMono-Regular",
            FontSize = 32.0f,
            AutoDetectContext = false
        };

        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);

        // Assert
        Assert.That(detectedFamily, Is.EqualTo("Pro Font"));
    }

    [Test]
    public void GetCurrentFontFamily_WithConfigForUnregisteredFont_ShouldReturnNull()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "UnknownFont-Regular",
            BoldFontName = "UnknownFont-Bold",
            ItalicFontName = "UnknownFont-Italic",
            BoldItalicFontName = "UnknownFont-BoldItalic",
            FontSize = 32.0f,
            AutoDetectContext = false
        };

        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);

        // Assert
        Assert.That(detectedFamily, Is.Null);
    }

    [Test]
    public void GetCurrentFontFamily_WithNullConfig_ShouldReturnNull()
    {
        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(null!);

        // Assert
        Assert.That(detectedFamily, Is.Null);
    }

    [Test]
    public void GetCurrentFontFamily_WithConfigHavingNullRegularFontName_ShouldReturnNull()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = null!,
            BoldFontName = "SomeFont-Bold",
            ItalicFontName = "SomeFont-Italic",
            BoldItalicFontName = "SomeFont-BoldItalic",
            FontSize = 32.0f,
            AutoDetectContext = false
        };

        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);

        // Assert
        Assert.That(detectedFamily, Is.Null);
    }

    [Test]
    public void GetCurrentFontFamily_WithConfigHavingEmptyRegularFontName_ShouldReturnNull()
    {
        // Arrange
        var config = new TerminalFontConfig
        {
            RegularFontName = "",
            BoldFontName = "SomeFont-Bold",
            ItalicFontName = "SomeFont-Italic",
            BoldItalicFontName = "SomeFont-BoldItalic",
            FontSize = 32.0f,
            AutoDetectContext = false
        };

        // Act
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);

        // Assert
        Assert.That(detectedFamily, Is.Null);
    }

    #endregion

    #region Integration Tests

    [Test]
    public void CreateFontConfigForFamily_ThenGetCurrentFontFamily_ShouldRoundTrip()
    {
        // Arrange
        const string originalDisplayName = "Space Mono";
        const float fontSize = 28.0f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(originalDisplayName, fontSize);
        var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);

        // Assert
        Assert.That(detectedFamily, Is.EqualTo(originalDisplayName));
    }

    [Test]
    public void CreateFontConfigForFamily_ForAllRegisteredFonts_ShouldRoundTripCorrectly()
    {
        // Arrange
        var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();

        // Act & Assert
        foreach (var family in availableFamilies)
        {
            var config = CaTTYFontManager.CreateFontConfigForFamily(family, 20.0f);
            var detectedFamily = CaTTYFontManager.GetCurrentFontFamily(config);
            
            Assert.That(detectedFamily, Is.EqualTo(family), 
                $"Round trip failed for font family: {family}");
        }
    }

    [Test]
    public void GetAvailableFontFamilies_ShouldContainExpectedFonts()
    {
        // Act
        var availableFamilies = CaTTYFontManager.GetAvailableFontFamilies();

        // Assert
        Assert.That(availableFamilies, Is.Not.Null);
        Assert.That(availableFamilies.Count, Is.EqualTo(7));
        
        // Check that all expected fonts are present
        Assert.That(availableFamilies, Contains.Item("Jet Brains Mono"));
        Assert.That(availableFamilies, Contains.Item("Space Mono"));
        Assert.That(availableFamilies, Contains.Item("Hack"));
        Assert.That(availableFamilies, Contains.Item("Pro Font"));
        Assert.That(availableFamilies, Contains.Item("Proggy Clean"));
        Assert.That(availableFamilies, Contains.Item("Shure Tech Mono"));
        Assert.That(availableFamilies, Contains.Item("Departure Mono"));
    }

    [Test]
    public void GetFontFamilyDefinition_WithValidDisplayName_ShouldReturnCorrectDefinition()
    {
        // Act
        var definition = CaTTYFontManager.GetFontFamilyDefinition("Jet Brains Mono");

        // Assert
        Assert.That(definition, Is.Not.Null);
        Assert.That(definition!.DisplayName, Is.EqualTo("Jet Brains Mono"));
        Assert.That(definition.FontBaseName, Is.EqualTo("JetBrainsMonoNerdFontMono"));
        Assert.That(definition.HasRegular, Is.True);
        Assert.That(definition.HasBold, Is.True);
        Assert.That(definition.HasItalic, Is.True);
        Assert.That(definition.HasBoldItalic, Is.True);
    }

    [Test]
    public void GetFontFamilyDefinition_WithInvalidDisplayName_ShouldReturnNull()
    {
        // Act
        var definition = CaTTYFontManager.GetFontFamilyDefinition("NonExistentFont");

        // Assert
        Assert.That(definition, Is.Null);
    }

    #endregion

    #region Edge Cases

    [Test]
    public void CreateFontConfigForFamily_WithExtremelySmallFontSize_ShouldWork()
    {
        // Arrange
        const string displayName = "Hack";
        const float tinySize = 0.1f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, tinySize);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.FontSize, Is.EqualTo(tinySize));
    }

    [Test]
    public void CreateFontConfigForFamily_WithExtremelyLargeFontSize_ShouldWork()
    {
        // Arrange
        const string displayName = "Hack";
        const float largeSize = 999.9f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, largeSize);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config.FontSize, Is.EqualTo(largeSize));
    }

    [Test]
    public void CreateFontConfigForFamily_WithCaseSensitiveDisplayName_ShouldNotMatch()
    {
        // Arrange - Use incorrect case
        const string displayName = "hack"; // Should be "Hack"
        const float fontSize = 20.0f;

        // Act
        var config = CaTTYFontManager.CreateFontConfigForFamily(displayName, fontSize);

        // Assert - Should return default config since case doesn't match
        Assert.That(config, Is.Not.Null);
        var defaultConfig = TerminalFontConfig.CreateForTestApp();
        Assert.That(config.RegularFontName, Is.EqualTo(defaultConfig.RegularFontName));
    }

    #endregion
}