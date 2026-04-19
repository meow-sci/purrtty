using NUnit.Framework;
using System.Text.Json;
using caTTY.Display.Configuration;
using caTTY.Core.Terminal;

namespace caTTY.Display.Tests.Unit.Configuration;

/// <summary>
/// Tests for ThemeConfiguration JSON serialization, particularly shell type string representation.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ThemeConfigurationSerializationTests
{
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
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

            // Assert
            Assert.That(json, Contains.Substring($"\"DefaultShellType\": \"{shellType}\""));
            Assert.That(json, Does.Not.Contain("\"DefaultShellType\": " + ((int)shellType).ToString()));
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
            var json = $@"{{
                ""SelectedThemeName"": ""TestTheme"",
                ""BackgroundOpacity"": 0.8,
                ""ForegroundOpacity"": 0.9,
                ""DefaultShellType"": ""{shellTypeString}"",
                ""CustomShellPath"": null,
                ""DefaultShellArguments"": [],
                ""WslDistribution"": null
            }}";

            // Act
            var config = JsonSerializer.Deserialize<ThemeConfiguration>(json);

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.DefaultShellType, Is.EqualTo(expectedEnum));            
        }
    }

    [Test]
    public void Deserialize_WithUnknownShellType_ShouldFallbackToWsl()
    {
        // Arrange - JSON with unknown shell type (e.g., from future version)
        var json = @"{
            ""SelectedThemeName"": ""TestTheme"",
            ""BackgroundOpacity"": 0.8,
            ""ForegroundOpacity"": 0.9,
            ""DefaultShellType"": ""FutureShellType"",
            ""CustomShellPath"": null,
            ""DefaultShellArguments"": [],
            ""WslDistribution"": null
        }";

        // Act
        var config = JsonSerializer.Deserialize<ThemeConfiguration>(json);

        // Assert
        Assert.That(config, Is.Not.Null);
        Assert.That(config!.DefaultShellType, Is.EqualTo(ShellType.Wsl));
        
    }

    [Test]
    public void Deserialize_WithNumericShellType_ShouldConvertCorrectly()
    {
        // Test multiple numeric values to ensure backward compatibility
        var testCases = new[]
        {
            (0, ShellType.Auto),
            (1, ShellType.PowerShell),
            (2, ShellType.Wsl),
            (3, ShellType.PowerShellCore),
            (4, ShellType.Cmd),
            (5, ShellType.Custom)
        };

        foreach (var (numericValue, expectedEnum) in testCases)
        {
            // Arrange - JSON with old numeric format
            var json = $@"{{
                ""SelectedThemeName"": ""TestTheme"",
                ""BackgroundOpacity"": 0.8,
                ""ForegroundOpacity"": 0.9,
                ""DefaultShellType"": {numericValue},
                ""CustomShellPath"": null,
                ""DefaultShellArguments"": [],
                ""WslDistribution"": null
            }}";

            // Act
            var config = JsonSerializer.Deserialize<ThemeConfiguration>(json);

            // Assert
            Assert.That(config, Is.Not.Null);
            Assert.That(config!.DefaultShellType, Is.EqualTo(expectedEnum));            
        }
    }

    [Test]
    public void Deserialize_WithInvalidNumericShellType_ShouldFallbackToWsl()
    {
        // Arrange - JSON with invalid numeric value (e.g., 999)
        var json = @"{
            ""SelectedThemeName"": ""TestTheme"",
            ""BackgroundOpacity"": 0.8,
            ""ForegroundOpacity"": 0.9,
            ""DefaultShellType"": 999,
            ""CustomShellPath"": null,
            ""DefaultShellArguments"": [],
            ""WslDistribution"": null
        }";

        // Act
        var config = JsonSerializer.Deserialize<ThemeConfiguration>(json);

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
        var json = JsonSerializer.Serialize(originalConfig, new JsonSerializerOptions { WriteIndented = true });
        var deserializedConfig = JsonSerializer.Deserialize<ThemeConfiguration>(json);

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
    public void Serialize_ShouldProduceReadableJson()
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
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

        // Verify it contains human-readable shell type
        Assert.That(json, Contains.Substring("\"DefaultShellType\": \"PowerShellCore\""));
        Assert.That(json, Contains.Substring("\"SelectedThemeName\": \"MonokaiPro\""));
        Assert.That(json, Contains.Substring("\"WslDistribution\": \"Debian\""));
    }
}