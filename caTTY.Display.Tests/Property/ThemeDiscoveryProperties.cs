using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using caTTY.Display.Rendering;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Display.Tests.Property;

/// <summary>
/// Property-based tests for TOML theme discovery and loading.
/// Tests universal properties that should hold across all theme discovery scenarios.
/// </summary>
[TestFixture]
[Category("Property")]
public class ThemeDiscoveryProperties
{
    /// <summary>
    /// Generator for valid directory structures with TOML files.
    /// Creates temporary directories with various combinations of TOML theme files.
    /// </summary>
    public static Arbitrary<string> ValidThemeDirectories()
    {
        return Gen.Fresh(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"themes_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            // Generate 0-5 valid TOML theme files
            var fileCount = Gen.Choose(0, 5).Sample(0, 1).First();
            var themeNames = new[] { "Adventure", "Monokai", "Matrix", "Neon", "Coffee" };

            for (int i = 0; i < fileCount; i++)
            {
                var themeName = themeNames[i % themeNames.Length];
                var fileName = $"{themeName}_{i}.toml";
                var filePath = Path.Combine(tempDir, fileName);

                // Create a valid TOML theme file
                var tomlContent = CreateValidTomlContent();
                File.WriteAllText(filePath, tomlContent);
            }

            return tempDir;
        }).ToArbitrary();
    }

    /// <summary>
    /// Generator for directories with mixed file types (TOML and non-TOML).
    /// </summary>
    public static Arbitrary<string> MixedFileDirectories()
    {
        return Gen.Fresh(() =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"mixed_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            // Add some TOML files
            var tomlCount = Gen.Choose(1, 3).Sample(0, 1).First();
            for (int i = 0; i < tomlCount; i++)
            {
                var filePath = Path.Combine(tempDir, $"theme_{i}.toml");
                File.WriteAllText(filePath, CreateValidTomlContent());
            }

            // Add some non-TOML files
            var nonTomlCount = Gen.Choose(1, 3).Sample(0, 1).First();
            var extensions = new[] { ".txt", ".json", ".xml", ".cfg" };
            for (int i = 0; i < nonTomlCount; i++)
            {
                var ext = extensions[i % extensions.Length];
                var filePath = Path.Combine(tempDir, $"config_{i}{ext}");
                File.WriteAllText(filePath, "not a toml file");
            }

            return tempDir;
        }).ToArbitrary();
    }

    /// <summary>
    /// Create valid TOML theme content for testing.
    /// </summary>
    private static string CreateValidTomlContent()
    {
        return @"
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
    }

    /// <summary>
    /// Property 1: Theme Discovery Completeness
    /// For any directory containing TOML files with .toml extensions, the theme discovery
    /// process should find and return all valid theme files in that directory.
    /// Feature: toml-terminal-theming, Property 1: Theme Discovery Completeness
    /// Validates: Requirements 1.1
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeDiscoveryCompleteness_ShouldFindAllValidTomlFiles()
    {
        return Prop.ForAll(ValidThemeDirectories(), themesDirectory =>
        {
            try
            {
                // Load themes from the directory
                var loadedThemes = TomlThemeLoader.LoadThemesFromDirectory(themesDirectory);

                // Count actual TOML files in the directory
                var tomlFiles = Directory.GetFiles(themesDirectory, "*.toml", SearchOption.TopDirectoryOnly);
                var expectedThemeCount = tomlFiles.Length;

                // All TOML files should be discovered and loaded successfully
                bool discoveredAllFiles = loadedThemes.Count == expectedThemeCount;

                // Each loaded theme should have a valid name derived from the filename
                bool allThemesHaveValidNames = loadedThemes.All(theme =>
                    !string.IsNullOrWhiteSpace(theme.Name) && theme.Name != "Unknown Theme");

                // Theme names should correspond to filenames (without .toml extension)
                var expectedNames = tomlFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet();
                var actualNames = loadedThemes.Select(t => t.Name).ToHashSet();
                bool namesMatch = expectedNames.SetEquals(actualNames);

                // No duplicate themes should be loaded
                bool noDuplicates = loadedThemes.Select(t => t.Name).Distinct().Count() == loadedThemes.Count;

                // All themes should have valid color palettes
                bool allThemesHaveValidColors = loadedThemes.All(theme =>
                    theme.Colors.Foreground.W == 1.0f && // Alpha should be 1.0
                    theme.Colors.Background.W == 1.0f &&
                    theme.Colors.Cursor.W == 1.0f &&
                    theme.Colors.Selection.W == 1.0f);

                return discoveredAllFiles && allThemesHaveValidNames && namesMatch &&
                       noDuplicates && allThemesHaveValidColors;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                // Clean up temporary directory
                try
                {
                    if (Directory.Exists(themesDirectory))
                    {
                        Directory.Delete(themesDirectory, true);
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
    /// Property: Theme Discovery with Mixed File Types
    /// Theme discovery should only load .toml files and ignore other file types.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ThemeDiscoveryMixedFiles_ShouldOnlyLoadTomlFiles()
    {
        return Prop.ForAll(MixedFileDirectories(), themesDirectory =>
        {
            try
            {
                // Load themes from the directory
                var loadedThemes = TomlThemeLoader.LoadThemesFromDirectory(themesDirectory);

                // Count only TOML files
                var tomlFiles = Directory.GetFiles(themesDirectory, "*.toml", SearchOption.TopDirectoryOnly);
                var expectedThemeCount = tomlFiles.Length;

                // Only TOML files should be loaded
                bool onlyTomlFilesLoaded = loadedThemes.Count == expectedThemeCount;

                // All loaded themes should have names matching TOML filenames
                var expectedNames = tomlFiles.Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet();
                var actualNames = loadedThemes.Select(t => t.Name).ToHashSet();
                bool namesMatchTomlFiles = expectedNames.SetEquals(actualNames);

                return onlyTomlFilesLoaded && namesMatchTomlFiles;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                // Clean up temporary directory
                try
                {
                    if (Directory.Exists(themesDirectory))
                    {
                        Directory.Delete(themesDirectory, true);
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
    /// Property: Empty Directory Handling
    /// Theme discovery should handle empty directories gracefully.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property ThemeDiscoveryEmptyDirectory_ShouldReturnEmptyList()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"empty_test_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDir);

                // Load themes from empty directory
                var loadedThemes = TomlThemeLoader.LoadThemesFromDirectory(tempDir);

                // Should return empty list
                return loadedThemes.Count == 0;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                // Clean up temporary directory
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
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
    /// Property: Non-existent Directory Handling
    /// Theme discovery should handle non-existent directories gracefully.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property ThemeDiscoveryNonExistentDirectory_ShouldReturnEmptyList()
    {
        return Prop.ForAll<string>(Gen.Fresh(() =>
            Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}")).ToArbitrary(),
            nonExistentDir =>
        {
            try
            {
                // Ensure directory doesn't exist
                if (Directory.Exists(nonExistentDir))
                {
                    Directory.Delete(nonExistentDir, true);
                }

                // Load themes from non-existent directory
                var loadedThemes = TomlThemeLoader.LoadThemesFromDirectory(nonExistentDir);

                // Should return empty list without throwing exception
                return loadedThemes.Count == 0;
            }
            catch (Exception)
            {
                // Should not throw exceptions for non-existent directories
                return false;
            }
        });
    }

    /// <summary>
    /// Property: Assembly Path Resolution
    /// The GetThemesDirectory method should consistently resolve to a path
    /// relative to the assembly location.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property AssemblyPathResolution_ShouldBeConsistent()
    {
        return Prop.ForAll<bool>(Gen.Constant(true).ToArbitrary(), _ =>
        {
            try
            {
                // Call GetThemesDirectory multiple times
                var path1 = TomlThemeLoader.GetThemesDirectory();
                var path2 = TomlThemeLoader.GetThemesDirectory();

                // Should return consistent results
                bool consistent = path1 == path2;

                // Should end with "TerminalThemes"
                bool endsWithCorrectName = path1.EndsWith("TerminalThemes");

                // Should be an absolute path
                bool isAbsolute = Path.IsPathRooted(path1);

                return consistent && endsWithCorrectName && isAbsolute;
            }
            catch (Exception)
            {
                return false;
            }
        });
    }
}
