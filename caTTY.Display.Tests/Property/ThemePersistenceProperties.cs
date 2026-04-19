using System;
using System.IO;
using System.Threading;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for theme persistence functionality.
/// Tests universal properties for theme configuration save/load operations.
/// </summary>
[TestFixture]
[Category("Property")]
public class ThemePersistenceProperties
{
    private string _tempConfigDirectory = null!;
    private string? _originalAppDataOverride;

    [SetUp]
    public void SetUp()
    {
        // Create a unique temporary directory for each test run
        var uniqueId = Guid.NewGuid().ToString("N")[..8];
        _tempConfigDirectory = Path.Combine(Path.GetTempPath(), $"caTTY_ThemeTests_{uniqueId}");
        Directory.CreateDirectory(_tempConfigDirectory);

        // Override the application data path for testing using the correct mechanism
        _originalAppDataOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        // Add a small delay to prevent file access conflicts
        Thread.Sleep(10);
    }

    [TearDown]
    public void TearDown()
    {
        // Restore original override
        ThemeConfiguration.OverrideConfigDirectory = _originalAppDataOverride;

        // Clean up temporary directory
        if (Directory.Exists(_tempConfigDirectory))
        {
            try
            {
                Directory.Delete(_tempConfigDirectory, true);
            }
            catch (Exception)
            {
                // Ignore cleanup errors in tests
            }
        }
    }
    /// <summary>
    /// Generator for valid theme names.
    /// </summary>
    public static Arbitrary<string> ValidThemeNames()
    {
        var names = new[] { "Adventure", "Monokai Pro", "Matrix", "Neon", "Coffee", "Default", "Default Light", "Custom Theme 1", "My-Theme_2024" };
        return Gen.Elements(names).ToArbitrary();
    }

    /// <summary>
    /// Generator for valid opacity values (0.0 to 1.0).
    /// </summary>
    public static Arbitrary<float> ValidOpacityValues()
    {
        return Gen.Choose(0, 100).Select(i => i / 100.0f).ToArbitrary();
    }

    /// <summary>
    /// Property 9: Theme Persistence Round-Trip
    /// For any theme selection, saving the preference and loading it back should return
    /// the same theme name.
    /// Feature: toml-terminal-theming, Property 9: Theme Persistence Round-Trip
    /// Validates: Requirements 6.1
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemePersistenceRoundTrip_ShouldPreserveThemeName()
    {
        return Prop.ForAll(ValidThemeNames(), ValidOpacityValues(), (themeName, opacity) =>
        {
            try
            {
                // Test the round-trip using ThemeManager methods
                // Save theme preference
                ThemeManager.SaveThemePreference(themeName);
                
                // Load theme preference back
                var loadedThemeName = ThemeManager.LoadThemePreference();
                
                // Theme name should be preserved exactly
                bool themeNamePreserved = loadedThemeName == themeName;
                
                // Test with ThemeConfiguration directly as well
                var config = new ThemeConfiguration
                {
                    SelectedThemeName = themeName,
                    BackgroundOpacity = opacity,
                    ForegroundOpacity = opacity
                };
                
                // Save and load configuration
                config.Save();
                var loadedConfig = ThemeConfiguration.Load();
                
                // Both theme name and opacity should be preserved
                bool configThemePreserved = loadedConfig.SelectedThemeName == themeName;
                bool configBgOpacityPreserved = Math.Abs(loadedConfig.BackgroundOpacity - opacity) < 0.001f;
                bool configFgOpacityPreserved = Math.Abs(loadedConfig.ForegroundOpacity - opacity) < 0.001f;
                
                return themeNamePreserved && configThemePreserved && configBgOpacityPreserved && configFgOpacityPreserved;
            }
            catch (Exception)
            {
                // Persistence failures should not occur for valid inputs
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Configuration Persistence Consistency
    /// For any valid configuration values, the save/load cycle should preserve all settings.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ConfigurationPersistenceConsistency_ShouldPreserveAllSettings()
    {
        return Prop.ForAll(ValidThemeNames(), ValidOpacityValues(), (themeName, opacity) =>
        {
            try
            {
                // Create configuration with test values
                var originalConfig = new ThemeConfiguration
                {
                    SelectedThemeName = themeName,
                    BackgroundOpacity = opacity,
                    ForegroundOpacity = opacity
                };
                
                // Save configuration
                originalConfig.Save();
                
                // Load configuration back
                var loadedConfig = ThemeConfiguration.Load();
                
                // All properties should be preserved
                bool themeNameMatches = loadedConfig.SelectedThemeName == originalConfig.SelectedThemeName;
                bool bgOpacityMatches = Math.Abs(loadedConfig.BackgroundOpacity - originalConfig.BackgroundOpacity) < 0.001f;
                bool fgOpacityMatches = Math.Abs(loadedConfig.ForegroundOpacity - originalConfig.ForegroundOpacity) < 0.001f;
                
                // Test multiple save/load cycles
                loadedConfig.Save();
                var secondLoadConfig = ThemeConfiguration.Load();
                
                bool secondCycleThemeMatches = secondLoadConfig.SelectedThemeName == originalConfig.SelectedThemeName;
                bool secondCycleBgOpacityMatches = Math.Abs(secondLoadConfig.BackgroundOpacity - originalConfig.BackgroundOpacity) < 0.001f;
                bool secondCycleFgOpacityMatches = Math.Abs(secondLoadConfig.ForegroundOpacity - originalConfig.ForegroundOpacity) < 0.001f;
                
                return themeNameMatches && bgOpacityMatches && fgOpacityMatches && 
                       secondCycleThemeMatches && secondCycleBgOpacityMatches && secondCycleFgOpacityMatches;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Null Theme Name Handling
    /// Configuration should handle null theme names gracefully.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property NullThemeNameHandling_ShouldHandleGracefully()
    {
        return Prop.ForAll(ValidOpacityValues(), opacity =>
        {
            try
            {
                // Create configuration with null theme name
                var config = new ThemeConfiguration
                {
                    SelectedThemeName = null,
                    BackgroundOpacity = opacity,
                    ForegroundOpacity = opacity
                };
                
                // Save and load should work without exceptions
                config.Save();
                var loadedConfig = ThemeConfiguration.Load();
                
                // Null theme name should be preserved
                bool nullPreserved = loadedConfig.SelectedThemeName == null;
                
                // Opacity should still be preserved
                bool bgOpacityPreserved = Math.Abs(loadedConfig.BackgroundOpacity - opacity) < 0.001f;
                bool fgOpacityPreserved = Math.Abs(loadedConfig.ForegroundOpacity - opacity) < 0.001f;
                
                return nullPreserved && bgOpacityPreserved && fgOpacityPreserved;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }
}