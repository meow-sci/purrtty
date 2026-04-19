using System.IO;
using caTTY.Display.Rendering;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit;

/// <summary>
/// Unit tests for TomlThemeLoader using real theme files.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TomlThemeLoaderRealFileTests
{
    [Test]
    public void LoadThemeFromFile_AdventureTheme_ShouldLoadSuccessfully()
    {
        // Arrange
        var themePath = Path.Combine("TerminalThemes", "Adventure.toml");

        // Act
        var result = TomlThemeLoader.LoadThemeFromFile(themePath);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result.HasValue)
        {
            var theme = result.Value;
            Assert.That(theme.Name, Is.EqualTo("Adventure"));
            Assert.That(theme.Colors.Foreground.W, Is.EqualTo(1.0f));
            Assert.That(theme.Colors.Background.W, Is.EqualTo(1.0f));
        }
    }

    [Test]
    public void LoadThemesFromDirectory_TerminalThemesDirectory_ShouldLoadMultipleThemes()
    {
        // Arrange
        var themesDirectory = "TerminalThemes";

        // Act
        var result = TomlThemeLoader.LoadThemesFromDirectory(themesDirectory);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.GreaterThan(0));
        
        // Check that Adventure theme is loaded
        var adventureTheme = result.Find(t => t.Name == "Adventure");
        Assert.That(adventureTheme.Name, Is.EqualTo("Adventure"));
    }
}