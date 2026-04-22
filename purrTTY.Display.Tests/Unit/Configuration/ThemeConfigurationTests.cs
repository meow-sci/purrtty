using System;
using System.IO;
using purrTTY.Display.Configuration;
using NUnit.Framework;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.Display.Tests.Unit.Configuration;

/// <summary>
/// Unit tests for ThemeConfiguration class.
/// Tests specific error handling scenarios and edge cases.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ThemeConfigurationTests
{
    private string _tempConfigDirectory = null!;
    private string _originalAppData = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temporary directory for test configuration files
        _tempConfigDirectory = Path.Combine(Path.GetTempPath(), $"purrTTY_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempConfigDirectory);

        // Store original AppData path and set temporary one for testing
        _originalAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // We can't easily override Environment.GetFolderPath, so we'll test with actual paths
        // but clean up afterwards
    }

    [TearDown]
    public void TearDown()
    {
        // Reset override to prevent test isolation issues
        ThemeConfiguration.OverrideConfigDirectory = null;

        // Clean up temporary directory
        if (Directory.Exists(_tempConfigDirectory))
        {
            try
            {
                Directory.Delete(_tempConfigDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // Clean up any test config files in production path (MyDocuments)
        try
        {
            var docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var productionConfigDirectory = Path.Combine(docsPath, "My Games", "Kitten Space Agency", ".purrTTY");
            var productionConfigFile = Path.Combine(productionConfigDirectory, "purrtty.toml");

            if (File.Exists(productionConfigFile))
            {
                var content = File.ReadAllText(productionConfigFile);
                // Only delete if it looks like a test file
                if (content.Contains("IsolationTest") || content.Contains("TestTheme") || content.Contains("0.123") || content.Contains("Adventure"))
                {
                    File.Delete(productionConfigFile);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        // Clean up any test config files in actual AppData
        try
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var configDirectory = Path.Combine(appDataPath, ".purrTTY");
            var configFile = Path.Combine(configDirectory, "purrtty.toml");

            if (File.Exists(configFile))
            {
                var content = File.ReadAllText(configFile);
                // Only delete if it looks like a test file
                if (content.Contains("TestTheme") || content.Contains("0.123"))
                {
                    File.Delete(configFile);
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Test missing configuration file scenarios.
    /// Requirements: 6.2, 6.3
    /// </summary>
    [Test]
    public void Load_WhenConfigurationFileDoesNotExist_ShouldReturnDefaultConfiguration()
    {
        // Use a temporary directory to isolate test from real config files
        var tempDir = Path.Combine(Path.GetTempPath(), $"purrTTY_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Set override to use temp directory
            ThemeConfiguration.OverrideConfigDirectory = tempDir;

            // Load configuration - file doesn't exist in temp directory
            var config = ThemeConfiguration.Load();

            // Should return default configuration
            Assert.That(config, Is.Not.Null);
            Assert.That(config.SelectedThemeName, Is.Null);
            Assert.That(config.BackgroundOpacity, Is.EqualTo(1.0f));
            Assert.That(config.ForegroundOpacity, Is.EqualTo(1.0f));
        }
        finally
        {
            // Reset override and clean up
            ThemeConfiguration.OverrideConfigDirectory = null;
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Test invalid configuration file scenarios.
    /// Requirements: 6.2, 6.3
    /// </summary>
    [Test]
    public void Load_WhenConfigurationFileIsInvalid_ShouldReturnDefaultConfiguration()
    {
        // Use a temporary directory to isolate test from real config files
        var tempDir = Path.Combine(Path.GetTempPath(), $"purrTTY_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Set override to use temp directory
            ThemeConfiguration.OverrideConfigDirectory = tempDir;

            var configDirectory = Path.Combine(tempDir, ".purrTTY");
            var configFile = Path.Combine(configDirectory, "purrtty.toml");

            // Ensure directory exists
            Directory.CreateDirectory(configDirectory);

            // Write invalid TOML
            var invalidTomlContent = "SelectedThemeName = ";
            File.WriteAllText(configFile, invalidTomlContent);

            // Load configuration
            var config = ThemeConfiguration.Load();

            // Should return default configuration despite invalid TOML
            Assert.That(config, Is.Not.Null);
            Assert.That(config.SelectedThemeName, Is.Null);
            Assert.That(config.BackgroundOpacity, Is.EqualTo(1.0f));
            Assert.That(config.ForegroundOpacity, Is.EqualTo(1.0f));
        }
        finally
        {
            // Reset override and clean up
            ThemeConfiguration.OverrideConfigDirectory = null;
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Test configuration file with malformed TOML structure.
    /// Requirements: 6.2, 6.3
    /// </summary>
    [Test]
    public void Load_WhenConfigurationFileHasMalformedToml_ShouldReturnDefaultConfiguration()
    {
        // Use a temporary directory to isolate test from real config files
        var tempDir = Path.Combine(Path.GetTempPath(), $"purrTTY_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Set override to use temp directory
            ThemeConfiguration.OverrideConfigDirectory = tempDir;

            var configDirectory = Path.Combine(tempDir, ".purrTTY");
            var configFile = Path.Combine(configDirectory, "purrtty.toml");

            Directory.CreateDirectory(configDirectory);

            // Test various malformed TOML scenarios
            var malformedTomlCases = new[]
            {
                "SelectedThemeName = \"Test\"\nBackgroundOpacity =", // Missing value
                "SelectedThemeName = \"Test\"\nBackgroundOpacity = [", // Unterminated array
                "BackgroundOpacity = \"not_a_number\"", // Wrong type
                "ForegroundOpacity = \"not_a_number\"", // Wrong type
                "UnknownProperty = \"value\"", // Unknown property only
                "", // Empty file
                "null", // Invalid TOML
                "[]", // Invalid TOML root
                "SelectedThemeName \"Test\"" // Missing equals sign
            };

            foreach (var malformedToml in malformedTomlCases)
            {
                File.WriteAllText(configFile, malformedToml);

                var config = ThemeConfiguration.Load();

                // Should always return a valid default configuration
                Assert.That(config, Is.Not.Null, $"Failed for TOML: {malformedToml}");
                Assert.That(config.SelectedThemeName, Is.Null, $"Failed for TOML: {malformedToml}");
                Assert.That(config.BackgroundOpacity, Is.EqualTo(1.0f), $"Failed for TOML: {malformedToml}");
                Assert.That(config.ForegroundOpacity, Is.EqualTo(1.0f), $"Failed for TOML: {malformedToml}");
            }
        }
        finally
        {
            // Reset override and clean up
            ThemeConfiguration.OverrideConfigDirectory = null;
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    /// <summary>
    /// Test configuration save when directory doesn't exist.
    /// Requirements: 6.2, 6.3
    /// </summary>
    [Test]
    public void Save_WhenDirectoryDoesNotExist_ShouldCreateDirectoryAndSave()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        var config = new ThemeConfiguration
        {
            SelectedThemeName = "TestTheme",
            BackgroundOpacity = 0.75f,
            ForegroundOpacity = 0.85f
        };

        // Ensure directory doesn't exist initially
        var appDataPath = Path.GetTempPath();
        ThemeConfiguration.OverrideConfigDirectory = appDataPath;
        var configDirectory = Path.Combine(appDataPath, ".purrTTY");
        var configFile = Path.Combine(configDirectory, "purrtty.toml");

        if (Directory.Exists(configDirectory))
        {
            Directory.Delete(configDirectory, true);
        }

        try
        {
            // Save configuration
            config.Save();

            // Directory and file should be created
            Assert.That(Directory.Exists(configDirectory), Is.True, $"configDirectory={configDirectory} did not exist");
            Assert.That(File.Exists(configFile), Is.True, $"configFile={configFile} did not exist");

            // Content should be correct
            var loadedConfig = ThemeConfiguration.Load();
            Assert.That(loadedConfig.SelectedThemeName, Is.EqualTo("TestTheme"));
            Assert.That(loadedConfig.BackgroundOpacity, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(loadedConfig.ForegroundOpacity, Is.EqualTo(0.85f).Within(0.001f));
        }
        finally
        {
            // Reset override to ensure test isolation
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;

            // Clean up
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }
            if (Directory.Exists(configDirectory))
            {
                Directory.Delete(configDirectory, true);
            }
        }
    }

    /// <summary>
    /// Test valid configuration save and load cycle.
    /// </summary>
    [Test]
    public void SaveAndLoad_WithValidConfiguration_ShouldPreserveAllValues()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var originalConfig = new ThemeConfiguration
        {
            SelectedThemeName = "Adventure",
            BackgroundOpacity = 0.85f,
            ForegroundOpacity = 0.75f
        };

        var configFile = Path.Combine(_tempConfigDirectory, ".purrTTY", "purrtty.toml");

        try
        {
            // Save configuration
            originalConfig.Save();

            // Load configuration back
            var loadedConfig = ThemeConfiguration.Load();

            // All values should be preserved
            Assert.That(loadedConfig.SelectedThemeName, Is.EqualTo("Adventure"));
            Assert.That(loadedConfig.BackgroundOpacity, Is.EqualTo(0.85f).Within(0.001f));
            Assert.That(loadedConfig.ForegroundOpacity, Is.EqualTo(0.75f).Within(0.001f));
        }
        finally
        {
            // Clean up
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }
        }
    }

    /// <summary>
    /// Test configuration with null theme name.
    /// </summary>
    [Test]
    public void SaveAndLoad_WithNullThemeName_ShouldHandleGracefully()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var config = new ThemeConfiguration
        {
            SelectedThemeName = null,
            BackgroundOpacity = 0.5f,
            ForegroundOpacity = 0.6f
        };

        var configFile = Path.Combine(_tempConfigDirectory, ".purrTTY", "purrtty.toml");

        try
        {
            // Save and load should work without exceptions
            config.Save();
            var loadedConfig = ThemeConfiguration.Load();

            // Null theme name should be preserved
            Assert.That(loadedConfig.SelectedThemeName, Is.Null);
            Assert.That(loadedConfig.BackgroundOpacity, Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(loadedConfig.ForegroundOpacity, Is.EqualTo(0.6f).Within(0.001f));
        }
        finally
        {
            // Clean up
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }
        }
    }

    /// <summary>
    /// Test configuration with edge case opacity values.
    /// </summary>
    [Test]
    public void SaveAndLoad_WithEdgeCaseOpacityValues_ShouldPreserveValues()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var edgeCases = new[] { 0.0f, 1.0f, 0.001f, 0.999f };
    var configFile = Path.Combine(_tempConfigDirectory, ".purrTTY", "purrtty.toml");

        try
        {
            foreach (var opacity in edgeCases)
            {
                var config = new ThemeConfiguration
                {
                    SelectedThemeName = $"TestTheme_{opacity}",
                    BackgroundOpacity = opacity,
                    ForegroundOpacity = opacity
                };

                config.Save();
                var loadedConfig = ThemeConfiguration.Load();

                Assert.That(loadedConfig.BackgroundOpacity, Is.EqualTo(opacity).Within(0.0001f),
                    $"Failed to preserve background opacity value: {opacity}");
                Assert.That(loadedConfig.ForegroundOpacity, Is.EqualTo(opacity).Within(0.0001f),
                    $"Failed to preserve foreground opacity value: {opacity}");
                Assert.That(loadedConfig.SelectedThemeName, Is.EqualTo($"TestTheme_{opacity}"));
            }
        }
        finally
        {
            // Clean up
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }
        }
    }

    /// <summary>
    /// Safeguard test to ensure tests never write to production configuration directory.
    /// This prevents regression where test isolation is broken.
    /// </summary>
    [Test]
    public void ConfigurationIsolation_WhenOverrideSet_ShouldNeverUseProductionPath()
    {
        // Arrange
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var productionDocsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var productionConfigDirectory = Path.Combine(productionDocsPath, "My Games", "Kitten Space Agency", ".purrTTY");
        var productionConfigFile = Path.Combine(productionConfigDirectory, "purrtty.toml");

        // Record the modification time of the production file before the test (if it exists)
        DateTime? productionFileModTimeBeforeTest = null;
        if (File.Exists(productionConfigFile))
        {
            productionFileModTimeBeforeTest = File.GetLastWriteTimeUtc(productionConfigFile);
        }

        var config = new ThemeConfiguration
        {
            SelectedThemeName = "IsolationTest",
            BackgroundOpacity = 0.5f,
            ForegroundOpacity = 0.6f
        };

        try
        {
            // Act - Save to the overridden temp directory
            config.Save();

            // Assert - Verify file was saved to temp directory, not production path
            var tempConfigFile = Path.Combine(_tempConfigDirectory, ".purrTTY", "purrtty.toml");

            Assert.That(File.Exists(tempConfigFile), Is.True,
                "Configuration should be saved to temp directory when override is set");

            // Check that production file wasn't modified by our operation
            if (File.Exists(productionConfigFile))
            {
                var productionFileModTimeAfterTest = File.GetLastWriteTimeUtc(productionConfigFile);
                Assert.That(productionFileModTimeAfterTest, Is.EqualTo(productionFileModTimeBeforeTest),
                    "Configuration should never be written to production path when override is set");
            }

            // Act - Load the configuration
            var loadedConfig = ThemeConfiguration.Load();

            // Assert - Verify the loaded config is from the temp directory
            Assert.That(loadedConfig.SelectedThemeName, Is.EqualTo("IsolationTest"),
                "Configuration should be loaded from temp directory, not production path");
        }
        finally
        {
            // Clean up
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;

            // Verify temp directory is cleaned up after test
            var tempConfigFile = Path.Combine(_tempConfigDirectory, ".purrTTY", "purrtty.toml");
            if (File.Exists(tempConfigFile))
            {
                File.Delete(tempConfigFile);
            }
        }
    }

    [Test]
    public void TryGetTerminalWindowState_WithPartialWindowState_ShouldReturnFalse()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            TerminalWindowPosX = 42.0f,
            TerminalWindowPosY = 84.0f,
            TerminalWindowWidth = null,
            TerminalWindowHeight = 600.0f
        };

        // Act
        var hasWindowState = config.TryGetTerminalWindowState(out _, out _);

        // Assert
        Assert.That(hasWindowState, Is.False);
    }

    [Test]
    public void SaveAndLoad_WithTerminalWindowState_ShouldPreserveWindowBounds()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var originalConfig = new ThemeConfiguration
        {
            SelectedThemeName = "Adventure",
            BackgroundOpacity = 0.85f,
            ForegroundOpacity = 0.75f
        };
        originalConfig.SetTerminalWindowState(new float2(300.0f, 180.0f), new float2(1280.0f, 720.0f), 132, 43);

        var configFile = Path.Combine(_tempConfigDirectory, ".purrTTY", "purrtty.toml");

        try
        {
            // Save configuration
            originalConfig.Save();

            // Load configuration back
            var loadedConfig = ThemeConfiguration.Load();

            // All values should be preserved
            Assert.That(loadedConfig.TryGetTerminalWindowState(out var position, out var size, out int columns, out int rows), Is.True);
            Assert.That(position.X, Is.EqualTo(300.0f).Within(0.001f));
            Assert.That(position.Y, Is.EqualTo(180.0f).Within(0.001f));
            Assert.That(size.X, Is.EqualTo(1280.0f).Within(0.001f));
            Assert.That(size.Y, Is.EqualTo(720.0f).Within(0.001f));
            Assert.That(columns, Is.EqualTo(132));
            Assert.That(rows, Is.EqualTo(43));
        }
        finally
        {
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }
        }
    }

    [Test]
    public void Save_ShouldWriteSettingsAndDoNotTouchTables()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var config = new ThemeConfiguration
        {
            SelectedThemeName = "Adventure",
            BackgroundOpacity = 0.85f,
            ForegroundOpacity = 0.75f,
            DefaultShellType = purrTTY.Core.Terminal.ShellType.PowerShellCore
        };
        config.SetTerminalWindowState(new float2(400.0f, 220.0f), new float2(1280.0f, 720.0f), 140, 50);

        var configFile = Path.Combine(_tempConfigDirectory, ".purrTTY", "purrtty.toml");

        try
        {
            config.Save();

            var content = File.ReadAllText(configFile);
            var tomlTable = TomlSerializer.Deserialize<TomlTable>(content);

            Assert.That(content, Contains.Substring("[settings]"));
            Assert.That(content, Contains.Substring("[do-not-touch]"));
            Assert.That(tomlTable, Is.Not.Null);
            Assert.That(tomlTable!.ContainsKey("settings"), Is.True);
            Assert.That(tomlTable.ContainsKey("do-not-touch"), Is.True);
        }
        finally
        {
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }
        }
    }

    [Test]
    public void Load_WithLegacyFlatConfiguration_ShouldReadExistingValues()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var configDirectory = Path.Combine(_tempConfigDirectory, ".purrTTY");
        var configFile = Path.Combine(configDirectory, "purrtty.toml");

        Directory.CreateDirectory(configDirectory);
        File.WriteAllText(configFile, """
SelectedThemeName = "LegacyTheme"
BackgroundOpacity = 0.8
ForegroundOpacity = 0.9
DefaultShellType = "PowerShellCore"
DefaultShellArguments = []
TerminalWindowPosX = 50.0
TerminalWindowPosY = 75.0
TerminalWindowWidth = 900.0
TerminalWindowHeight = 550.0
TerminalColumns = 111
TerminalRows = 37
""");

        try
        {
            var loadedConfig = ThemeConfiguration.Load();

            Assert.That(loadedConfig.SelectedThemeName, Is.EqualTo("LegacyTheme"));
            Assert.That(loadedConfig.DefaultShellType, Is.EqualTo(purrTTY.Core.Terminal.ShellType.PowerShellCore));
            Assert.That(loadedConfig.TryGetTerminalWindowState(out var position, out var size, out int columns, out int rows), Is.True);
            Assert.That(position.X, Is.EqualTo(50.0f));
            Assert.That(position.Y, Is.EqualTo(75.0f));
            Assert.That(size.X, Is.EqualTo(900.0f));
            Assert.That(size.Y, Is.EqualTo(550.0f));
            Assert.That(columns, Is.EqualTo(111));
            Assert.That(rows, Is.EqualTo(37));
        }
        finally
        {
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;
            if (File.Exists(configFile))
            {
                File.Delete(configFile);
            }
        }
    }

    [Test]
    public void CreateWithPersistedConfiguration_WithSavedGridDimensions_ShouldSeedSessionManagerDimensions()
    {
        var originalOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;

        var config = new ThemeConfiguration
        {
            DefaultShellType = purrTTY.Core.Terminal.ShellType.PowerShell
        };
        config.SetTerminalWindowState(new float2(320.0f, 160.0f), new float2(1200.0f, 800.0f), 144, 48);

        try
        {
            config.Save();

            using var sessionManager = SessionManagerFactory.CreateWithPersistedConfiguration();
            var (columns, rows) = sessionManager.LastKnownTerminalDimensions;

            Assert.That(columns, Is.EqualTo(144));
            Assert.That(rows, Is.EqualTo(48));
        }
        finally
        {
            ThemeConfiguration.OverrideConfigDirectory = originalOverride;
        }
    }
}
