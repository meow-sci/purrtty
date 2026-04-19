using System.Linq;
using caTTY.Display.Rendering;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit;

/// <summary>
/// Unit tests for the enhanced ThemeManager functionality.
/// Tests the new TOML theme support and Adventure.toml color integration.
/// </summary>
[TestFixture]
[Category("Unit")]
public class EnhancedThemeManagerTests
{
    [Test]
    public void DefaultTheme_ShouldUseAdventureColors()
    {
        // Arrange & Act
        var defaultTheme = ThemeManager.DefaultTheme;

        // Assert - Verify Adventure.toml colors are used
        Assert.That(defaultTheme.Name, Is.EqualTo("Default"));
        Assert.That(defaultTheme.Type, Is.EqualTo(ThemeType.Dark));
        
        // Check some key Adventure.toml colors (converted from hex)
        // Background should be #040404
        Assert.That(defaultTheme.Colors.Background.X, Is.EqualTo(4f/255f).Within(0.01f));
        Assert.That(defaultTheme.Colors.Background.Y, Is.EqualTo(4f/255f).Within(0.01f));
        Assert.That(defaultTheme.Colors.Background.Z, Is.EqualTo(4f/255f).Within(0.01f));
        
        // Foreground should be #feffff
        Assert.That(defaultTheme.Colors.Foreground.X, Is.EqualTo(254f/255f).Within(0.01f));
        Assert.That(defaultTheme.Colors.Foreground.Y, Is.EqualTo(255f/255f).Within(0.01f));
        Assert.That(defaultTheme.Colors.Foreground.Z, Is.EqualTo(255f/255f).Within(0.01f));
        
        // Red should be #d84a33
        Assert.That(defaultTheme.Colors.Red.X, Is.EqualTo(216f/255f).Within(0.01f));
        Assert.That(defaultTheme.Colors.Red.Y, Is.EqualTo(74f/255f).Within(0.01f));
        Assert.That(defaultTheme.Colors.Red.Z, Is.EqualTo(51f/255f).Within(0.01f));
    }

    [Test]
    public void InitializeThemes_ShouldLoadBuiltInThemes()
    {
        // Arrange & Act
        ThemeManager.InitializeThemes();
        var availableThemes = ThemeManager.AvailableThemes;

        // Assert
        Assert.That(availableThemes.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(availableThemes.Any(t => t.Name == "Default"), Is.True);
        Assert.That(availableThemes.Any(t => t.Name == "Default Light"), Is.True);
    }

    [Test]
    public void CurrentTheme_ShouldReturnValidBuiltInTheme()
    {
        // Arrange & Act
        var currentTheme = ThemeManager.CurrentTheme;

        // Assert - Should be one of the built-in themes
        Assert.That(currentTheme.Name, Is.EqualTo("Default").Or.EqualTo("Default Light"));
        Assert.That(currentTheme.Source, Is.EqualTo(ThemeSource.BuiltIn));
    }

    [Test]
    public void ApplyTheme_WithValidThemeName_ShouldReturnTrue()
    {
        // Arrange
        ThemeManager.InitializeThemes();

        // Act
        var result = ThemeManager.ApplyTheme("Default Light");

        // Assert
        Assert.That(result, Is.True);
        Assert.That(ThemeManager.CurrentTheme.Name, Is.EqualTo("Default Light"));
    }

    [Test]
    public void ApplyTheme_WithInvalidThemeName_ShouldReturnFalse()
    {
        // Arrange
        ThemeManager.InitializeThemes();
        var originalTheme = ThemeManager.CurrentTheme;

        // Act
        var result = ThemeManager.ApplyTheme("NonExistentTheme");

        // Assert
        Assert.That(result, Is.False);
        Assert.That(ThemeManager.CurrentTheme.Name, Is.EqualTo(originalTheme.Name));
    }

    [Test]
    public void ThemeChanged_Event_ShouldFireWhenThemeApplied()
    {
        // Arrange
        ThemeManager.InitializeThemes();
        var initialTheme = ThemeManager.CurrentTheme;
        
        ThemeChangedEventArgs? capturedArgs = null;
        var eventFired = false;

        EventHandler<ThemeChangedEventArgs> handler = (sender, args) =>
        {
            capturedArgs = args;
            eventFired = true;
        };

        ThemeManager.ThemeChanged += handler;

        try
        {
            // Act
            ThemeManager.ApplyTheme("Default Light");

            // Assert
            Assert.That(eventFired, Is.True);
            Assert.That(capturedArgs, Is.Not.Null);
            Assert.That(capturedArgs!.PreviousTheme.Name, Is.EqualTo(initialTheme.Name));
            Assert.That(capturedArgs.NewTheme.Name, Is.EqualTo("Default Light"));
        }
        finally
        {
            ThemeManager.ThemeChanged -= handler;
        }
    }

    [Test]
    public void RefreshAvailableThemes_ShouldMaintainBuiltInThemes()
    {
        // Arrange
        ThemeManager.InitializeThemes();
        var initialCount = ThemeManager.AvailableThemes.Count;

        // Act
        ThemeManager.RefreshAvailableThemes();

        // Assert
        var refreshedThemes = ThemeManager.AvailableThemes;
        Assert.That(refreshedThemes.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(refreshedThemes.Any(t => t.Name == "Default"), Is.True);
        Assert.That(refreshedThemes.Any(t => t.Name == "Default Light"), Is.True);
    }

    [Test]
    public void ResolveThemeColor_ShouldReturnCorrectColors()
    {
        // Arrange
        ThemeManager.InitializeThemes();

        // Act & Assert
        var black = ThemeManager.ResolveThemeColor(0);
        var red = ThemeManager.ResolveThemeColor(1);
        var foreground = ThemeManager.ResolveThemeColor(999); // Invalid code should return foreground

        Assert.That(black, Is.EqualTo(ThemeManager.CurrentTheme.Colors.Black));
        Assert.That(red, Is.EqualTo(ThemeManager.CurrentTheme.Colors.Red));
        Assert.That(foreground, Is.EqualTo(ThemeManager.CurrentTheme.Colors.Foreground));
    }

    [Test]
    public void GetDefaultColors_ShouldReturnThemeColors()
    {
        // Arrange
        ThemeManager.InitializeThemes();
        var currentTheme = ThemeManager.CurrentTheme;

        // Act & Assert
        Assert.That(ThemeManager.GetDefaultForeground(), Is.EqualTo(currentTheme.Colors.Foreground));
        Assert.That(ThemeManager.GetDefaultBackground(), Is.EqualTo(currentTheme.Colors.Background));
        Assert.That(ThemeManager.GetCursorColor(), Is.EqualTo(currentTheme.Colors.Cursor));
        Assert.That(ThemeManager.GetSelectionColor(), Is.EqualTo(currentTheme.Colors.Selection));
    }

    [Test]
    public void GetCursorSettings_ShouldReturnThemeSettings()
    {
        // Arrange
        ThemeManager.InitializeThemes();
        var currentTheme = ThemeManager.CurrentTheme;

        // Act & Assert
        Assert.That(ThemeManager.GetDefaultCursorStyle(), Is.EqualTo(currentTheme.Cursor.DefaultStyle));
        Assert.That(ThemeManager.GetDefaultCursorBlink(), Is.EqualTo(currentTheme.Cursor.DefaultBlink));
        Assert.That(ThemeManager.GetCursorBlinkInterval(), Is.EqualTo(currentTheme.Cursor.BlinkIntervalMs));
    }
}