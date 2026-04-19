using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for theme management functionality.
/// Tests universal properties for theme application, persistence, and event notifications.
/// </summary>
[TestFixture]
[Category("Property")]
public class ThemeManagementProperties
{
    /// <summary>
    /// Generator for valid theme names.
    /// </summary>
    public static Arbitrary<string> ValidThemeNames()
    {
        var names = new[] { "Adventure", "Monokai Pro", "Matrix", "Neon", "Coffee", "Default", "Default Light" };
        return Gen.Elements(names).ToArbitrary();
    }

    /// <summary>
    /// Property 3: Theme Validation Completeness
    /// For any TOML file missing required color sections, the validation should reject
    /// the file and log an appropriate error.
    /// Feature: toml-terminal-theming, Property 3: Theme Validation Completeness
    /// Validates: Requirements 1.3, 1.4
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeValidationCompleteness_ShouldRejectIncompleteThemes()
    {
        return Prop.ForAll<string>(Gen.Elements("colors.normal", "colors.bright", "colors.primary", "colors.cursor", "colors.selection").ToArbitrary(),
            missingSectionName =>
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"incomplete_theme_{Guid.NewGuid():N}.toml");

            try
            {
                // Create TOML content missing the specified section
                var baseContent = @"
[colors.normal]
black = '#040404'
red = '#d84a33'
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

                // Remove the specified section
                var lines = baseContent.Split('\n').ToList();
                var sectionStart = -1;
                var sectionEnd = -1;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim() == $"[{missingSectionName}]")
                    {
                        sectionStart = i;
                    }
                    else if (sectionStart >= 0 && lines[i].Trim().StartsWith("[") && i > sectionStart)
                    {
                        sectionEnd = i;
                        break;
                    }
                }

                if (sectionStart >= 0)
                {
                    if (sectionEnd == -1) sectionEnd = lines.Count;
                    lines.RemoveRange(sectionStart, sectionEnd - sectionStart);
                }

                var incompleteContent = string.Join('\n', lines);
                File.WriteAllText(tempFile, incompleteContent);

                // Attempt to load the incomplete theme
                var theme = TomlThemeLoader.LoadThemeFromFile(tempFile);

                // Theme should be rejected (return null)
                bool themeRejected = !theme.HasValue;

                // Test directory loading as well
                var tempDir = Path.GetDirectoryName(tempFile);
                var themes = TomlThemeLoader.LoadThemesFromDirectory(tempDir!);
                
                // The incomplete theme should not appear in the loaded themes list
                bool notInDirectoryLoad = !themes.Any(t => t.Name == Path.GetFileNameWithoutExtension(tempFile));

                return themeRejected && notInDirectoryLoad;
            }
            catch (Exception)
            {
                // Exceptions are acceptable for invalid themes
                return true;
            }
            finally
            {
                // Clean up temporary file
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        });
    }

    /// <summary>
    /// Property 4: Theme Name Extraction Consistency
    /// For any file path ending with .toml extension, the display name extraction
    /// should return the filename without the .toml extension.
    /// Feature: toml-terminal-theming, Property 4: Theme Name Extraction Consistency
    /// Validates: Requirements 1.5
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeNameExtractionConsistency_ShouldExtractCorrectNames()
    {
        return Prop.ForAll(ValidThemeNames(), themeName =>
        {
            try
            {
                // Create various file path formats
                var tempDir = Path.GetTempPath();
                var filePath1 = Path.Combine(tempDir, $"{themeName}.toml");
                var filePath2 = Path.Combine(tempDir, "themes", $"{themeName}.toml");
                var filePath3 = Path.Combine("C:", "themes", $"{themeName}.toml");

                // Extract names using the utility method
                var extractedName1 = TomlThemeLoader.GetThemeDisplayName(filePath1);
                var extractedName2 = TomlThemeLoader.GetThemeDisplayName(filePath2);
                var extractedName3 = TomlThemeLoader.GetThemeDisplayName(filePath3);

                // All should extract the same theme name
                bool allNamesMatch = extractedName1 == themeName &&
                                    extractedName2 == themeName &&
                                    extractedName3 == themeName;

                // Names should not be null or empty
                bool allNamesValid = !string.IsNullOrWhiteSpace(extractedName1) &&
                                    !string.IsNullOrWhiteSpace(extractedName2) &&
                                    !string.IsNullOrWhiteSpace(extractedName3);

                // Names should not contain the .toml extension
                bool noExtensions = !extractedName1.Contains(".toml") &&
                                   !extractedName2.Contains(".toml") &&
                                   !extractedName3.Contains(".toml");

                return allNamesMatch && allNamesValid && noExtensions;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property 10: Theme Change Notification Consistency
    /// For any theme change operation, all registered event handlers should be notified
    /// with the correct previous and new theme information.
    /// Feature: toml-terminal-theming, Property 10: Theme Change Notification Consistency
    /// Validates: Requirements 6.4
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeChangeNotificationConsistency_ShouldNotifyCorrectly()
    {
        return Prop.ForAll(ValidThemeNames(), newThemeName =>
        {
            try
            {
                // Initialize theme system to ensure we have available themes
                ThemeManager.InitializeThemes();

                // Get current theme before change
                var initialTheme = ThemeManager.CurrentTheme;

                // Set up event tracking
                ThemeChangedEventArgs? capturedEventArgs = null;
                var eventFired = false;

                EventHandler<ThemeChangedEventArgs> eventHandler = (sender, args) =>
                {
                    capturedEventArgs = args;
                    eventFired = true;
                };

                // Subscribe to theme change event
                ThemeManager.ThemeChanged += eventHandler;

                try
                {
                    // Apply a different theme (if available)
                    var availableThemes = ThemeManager.AvailableThemes;
                    var targetTheme = availableThemes.FirstOrDefault(t => t.Name != initialTheme.Name);

                    if (targetTheme.Name == null)
                    {
                        // If no different theme available, use built-in themes
                        targetTheme = initialTheme.Name == "Default" ? ThemeManager.DefaultLightTheme : ThemeManager.DefaultTheme;
                    }

                    // Apply the theme
                    ThemeManager.ApplyTheme(targetTheme);

                    // Verify event was fired
                    if (!eventFired) return false;

                    // Verify event arguments are correct
                    if (capturedEventArgs == null) return false;

                    bool correctPreviousTheme = capturedEventArgs.PreviousTheme.Name == initialTheme.Name;
                    bool correctNewTheme = capturedEventArgs.NewTheme.Name == targetTheme.Name;

                    // Verify current theme was actually changed
                    bool currentThemeUpdated = ThemeManager.CurrentTheme.Name == targetTheme.Name;

                    // Test applying the same theme (should still fire event)
                    eventFired = false;
                    capturedEventArgs = null;
                    ThemeManager.ApplyTheme(targetTheme);

                    bool sameThemeEventFired = eventFired;
                    bool sameThemeCorrectArgs = capturedEventArgs != null &&
                                               capturedEventArgs.PreviousTheme.Name == targetTheme.Name &&
                                               capturedEventArgs.NewTheme.Name == targetTheme.Name;

                    return correctPreviousTheme && correctNewTheme && currentThemeUpdated &&
                           sameThemeEventFired && sameThemeCorrectArgs;
                }
                finally
                {
                    // Unsubscribe from event
                    ThemeManager.ThemeChanged -= eventHandler;
                }
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Theme Application by Name
    /// Applying a theme by name should work correctly for all available themes.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeApplicationByName_ShouldWorkForAllThemes()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            try
            {
                // Initialize theme system
                ThemeManager.InitializeThemes();

                var availableThemes = ThemeManager.AvailableThemes;
                if (availableThemes.Count == 0) return false;

                // Test applying each available theme by name
                foreach (var theme in availableThemes)
                {
                    var success = ThemeManager.ApplyTheme(theme.Name);
                    if (!success) return false;

                    var currentTheme = ThemeManager.CurrentTheme;
                    if (currentTheme.Name != theme.Name) return false;
                }

                // Test applying non-existent theme name
                var nonExistentResult = ThemeManager.ApplyTheme("NonExistentTheme123");
                if (nonExistentResult) return false; // Should return false for non-existent themes

                // Test applying null/empty theme name
                var nullResult = ThemeManager.ApplyTheme(null!);
                var emptyResult = ThemeManager.ApplyTheme("");
                var whitespaceResult = ThemeManager.ApplyTheme("   ");

                return !nullResult && !emptyResult && !whitespaceResult;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Theme Refresh Consistency
    /// Refreshing themes should maintain current theme if it still exists.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property ThemeRefreshConsistency_ShouldMaintainCurrentTheme()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            try
            {
                // Initialize theme system
                ThemeManager.InitializeThemes();

                var initialTheme = ThemeManager.CurrentTheme;
                var initialAvailableCount = ThemeManager.AvailableThemes.Count;

                // Refresh themes
                ThemeManager.RefreshAvailableThemes();

                var afterRefreshTheme = ThemeManager.CurrentTheme;
                var afterRefreshCount = ThemeManager.AvailableThemes.Count;

                // Current theme should be maintained (built-in themes always exist)
                bool themePreserved = afterRefreshTheme.Name == initialTheme.Name;

                // Should have at least the built-in themes
                bool hasBuiltInThemes = afterRefreshCount >= 2; // Default and Default Light

                // Built-in themes should always be present
                bool hasDefaultTheme = ThemeManager.AvailableThemes.Any(t => t.Name == "Default");
                bool hasDefaultLightTheme = ThemeManager.AvailableThemes.Any(t => t.Name == "Default Light");

                return themePreserved && hasBuiltInThemes && hasDefaultTheme && hasDefaultLightTheme;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }
}