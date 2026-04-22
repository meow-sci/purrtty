using NUnit.Framework;
using purrTTY.Display.Configuration;
using purrTTY.Core.Terminal;
using Tomlyn;
using Tomlyn.Serialization;

namespace purrTTY.Display.Tests.Unit.Configuration;

/// <summary>
/// Tests for ThemeConfiguration TOML serialization, particularly shell type string representation.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ThemeConfigurationSerializationTests
{
    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
        DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull
    };

    [Test]
    public void Serialize_WithDifferentShellTypes_ShouldUseStringRepresentation()
    {
        // Test each shell type
        var testCases = new[]
        {
            ShellType.Wsl,
            ShellType.PowerShell,
            ShellType.PowerShellCore,
            ShellType.Cmd,
            ShellType.Custom,
            ShellType.Auto
        };

        foreach (var shellType in testCases)
        {
            // Arrange
            var config = new ThemeConfiguration
            {
                DefaultShellType = shellType,
                SelectedThemeName = "TestTheme",
                BackgroundOpacity = 0.8f,
                ForegroundOpacity = 0.9f
            };

            // Act
            var toml = TomlSerializer.Serialize(config, TomlOptions);

            // Assert
            Assert.That(toml, Contains.Substring($"DefaultShellType = \"{shellType}\""));
            Assert.That(toml, Does.Not.Contain($"DefaultShellType = {(int)shellType}"));
        }
    }

    [Test]
    public void Deserialize_WithStringShellType_ShouldSetCorrectEnum()
    {
        // Test each shell type string
        var testCases = new[]
        {
            ("Wsl", ShellType.Wsl),
            ("PowerShell", ShellType.PowerShell),
            ("PowerShellCore", ShellType.PowerShellCore),
            ("Cmd", ShellType.Cmd),
            ("Custom", ShellType.Custom),
            ("Auto", ShellType.Auto)
        };

        foreach (var (shellTypeString, expectedEnum) in testCases)
        {
            // Arrange
            var toml = $$"""
            SelectedThemeName = "TestTheme"
            BackgroundOpacity = 0.8
            ForegroundOpacity = 0.9
            DefaultShellType = "{{shellTypeString}}"
            DefaultShellArguments = []
            """;

            // Act
            var config = TomlSerializer.Deserialize<ThemeConfiguration>(toml, TomlOptions);

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.DefaultShellType, Is.EqualTo(expectedEnum));            
        }
    }

    [Test]
    public void Deserialize_WithUnknownShellType_ShouldFallbackToWsl()
    {
        // Arrange - TOML with unknown shell type (e.g., from future version)
        var toml = """
        SelectedThemeName = "TestTheme"
        BackgroundOpacity = 0.8
        ForegroundOpacity = 0.9
        DefaultShellType = "FutureShellType"
        DefaultShellArguments = []
        """;

        // Act
        var config = TomlSerializer.Deserialize<ThemeConfiguration>(toml, TomlOptions);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.DefaultShellType, Is.EqualTo(ShellType.Wsl));
    }

    [Test]
    public void RoundTrip_SerializeAndDeserialize_ShouldPreserveShellType()
    {
        // Arrange
        var originalConfig = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell,
            SelectedThemeName = "DarkTheme",
            BackgroundOpacity = 0.7f,
            ForegroundOpacity = 0.95f,
            CustomShellPath = @"C:\test\shell.exe",
            WslDistribution = "Ubuntu"
        };

        // Act
        var toml = TomlSerializer.Serialize(originalConfig, TomlOptions);
        var deserializedConfig = TomlSerializer.Deserialize<ThemeConfiguration>(toml, TomlOptions);

        // Assert
        Assert.That(deserializedConfig, Is.Not.Null);
        Assert.That(deserializedConfig!.DefaultShellType, Is.EqualTo(originalConfig.DefaultShellType));
        Assert.That(deserializedConfig.SelectedThemeName, Is.EqualTo(originalConfig.SelectedThemeName));
        Assert.That(deserializedConfig.BackgroundOpacity, Is.EqualTo(originalConfig.BackgroundOpacity));
        Assert.That(deserializedConfig.ForegroundOpacity, Is.EqualTo(originalConfig.ForegroundOpacity));
        Assert.That(deserializedConfig.CustomShellPath, Is.EqualTo(originalConfig.CustomShellPath));
        Assert.That(deserializedConfig.WslDistribution, Is.EqualTo(originalConfig.WslDistribution));
        
    }

    [Test]
    public void Serialize_ShouldProduceReadableToml()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShellCore,
            SelectedThemeName = "MonokaiPro",
            BackgroundOpacity = 0.85f,
            ForegroundOpacity = 1.0f,
            CustomShellPath = null,
            WslDistribution = "Debian"
        };

        // Act
        var toml = TomlSerializer.Serialize(config, TomlOptions);

        // Verify it contains human-readable shell type
        Assert.That(toml, Contains.Substring("DefaultShellType = \"PowerShellCore\""));
        Assert.That(toml, Contains.Substring("SelectedThemeName = \"MonokaiPro\""));
        Assert.That(toml, Contains.Substring("WslDistribution = \"Debian\""));
    }
}