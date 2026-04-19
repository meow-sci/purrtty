using System;
using System.IO;
using caTTY.Display.Rendering;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit;

/// <summary>
/// Unit tests for TomlThemeLoader functionality.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TomlThemeLoaderTests
{
    [Test]
    public void ParseHexColor_ValidHexColor_ShouldParseCorrectly()
    {
        // Arrange
        var hexColor = "#ff6188";

        // Act
        var result = TomlThemeLoader.ParseHexColor(hexColor);

        // Assert
        Assert.That(result.X, Is.EqualTo(1.0f).Within(0.01f)); // Red component
        Assert.That(result.Y, Is.EqualTo(0.38f).Within(0.01f)); // Green component (97/255)
        Assert.That(result.Z, Is.EqualTo(0.53f).Within(0.01f)); // Blue component (136/255)
        Assert.That(result.W, Is.EqualTo(1.0f)); // Alpha component
    }

    [Test]
    public void ToHexColor_ValidFloat4Color_ShouldConvertCorrectly()
    {
        // Arrange
        var color = new Brutal.Numerics.float4(1.0f, 0.38f, 0.53f, 1.0f);

        // Act
        var result = TomlThemeLoader.ToHexColor(color);

        // Assert
        Assert.That(result, Is.EqualTo("#ff6187").Or.EqualTo("#ff6188")); // Allow for rounding
    }

    [Test]
    public void GetThemeDisplayName_ValidFilePath_ShouldExtractName()
    {
        // Arrange
        var filePath = @"C:\Themes\Adventure.toml";

        // Act
        var result = TomlThemeLoader.GetThemeDisplayName(filePath);

        // Assert
        Assert.That(result, Is.EqualTo("Adventure"));
    }

    [Test]
    public void LoadThemeFromFile_ValidTomlFile_ShouldLoadTheme()
    {
        // Arrange
        var themeName = $"test_theme_{Guid.NewGuid():N}";
        var tempFile = Path.Combine(Path.GetTempPath(), $"{themeName}.toml");
        var tomlContent = @"
[colors.normal]
black = '#040404'
red = '#ff0000'
green = '#5da602'
yellow = '#eebb6e'
blue = '#417ab3'
magenta = '#e5c499'
cyan = '#bdcfe5'
white = '#dbded8'

[colors.bright]
black = '#685656'
red = '#d76b42'
green = '#99b52c'
yellow = '#ffb670'
blue = '#97d7ef'
magenta = '#aa7900'
cyan = '#bdcfe5'
white = '#e4d5c7'

[colors.primary]
background = '#040404'
foreground = '#feffff'

[colors.cursor]
cursor = '#feffff'
text = '#000000'

[colors.selection]
background = '#606060'
text = '#ffffff'
";

        try
        {

            File.WriteAllText(tempFile, tomlContent);

            // Act
            var result = TomlThemeLoader.LoadThemeFromFile(tempFile);

            // Assert
            Assert.That(result, Is.Not.Null);
            if (result.HasValue)
            {
                var theme = result.Value;
                Assert.That(theme.Name, Is.EqualTo(themeName));
                Assert.That(theme.Colors.Foreground.W, Is.EqualTo(1.0f));
                Assert.That(theme.Colors.Background.W, Is.EqualTo(1.0f));
                Assert.That(theme.Colors.Red.R, Is.EqualTo(1.0f));
                Assert.That(theme.Colors.Red.G, Is.EqualTo(0.0f));
                Assert.That(theme.Colors.Red.B, Is.EqualTo(0.0f));
                Assert.That(theme.Colors.Red.A, Is.EqualTo(1.0f));
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void LoadThemesFromDirectory_EmptyDirectory_ShouldReturnEmptyList()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"empty_themes_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Act
            var result = TomlThemeLoader.LoadThemesFromDirectory(tempDir);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.EqualTo(0));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Test]
    public void LoadThemesFromDirectory_NonExistentDirectory_ShouldReturnEmptyList()
    {
        // Arrange
        var nonExistentDir = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}");

        // Act
        var result = TomlThemeLoader.LoadThemesFromDirectory(nonExistentDir);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));
    }
}