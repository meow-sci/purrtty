using System;
using System.IO;
using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Rendering;

namespace caTTY.Display.Tests.Integration;

/// <summary>
/// Integration tests for opacity persistence when shell configuration changes.
/// Verifies that global opacity settings are preserved independently of shell type selection.
/// </summary>
[TestFixture]
public class OpacityPersistenceIntegrationTests
{
    private string? _originalAppDataDirectory;

    [SetUp]
    public void SetUp()
    {
        // Use a temporary directory for test configuration files
        _originalAppDataDirectory = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        // Ensure clean state for each test
        OpacityManager.ResetOpacity();
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test configuration directory
        if (ThemeConfiguration.OverrideConfigDirectory != null && Directory.Exists(ThemeConfiguration.OverrideConfigDirectory))
        {
            Directory.Delete(ThemeConfiguration.OverrideConfigDirectory, true);
        }
        
        // Restore original app data directory
        ThemeConfiguration.OverrideConfigDirectory = _originalAppDataDirectory;
        
        // Reset opacity to default state
        OpacityManager.ResetOpacity();
    }

    [Test]
    public void ShellTypeChange_WithCustomOpacity_ShouldPreserveOpacitySettings()
    {
        // Arrange - Set custom opacity values
        const float customBackgroundOpacity = 0.7f;
        const float customForegroundOpacity = 0.8f;
        
        OpacityManager.SetBackgroundOpacity(customBackgroundOpacity);
        OpacityManager.SetForegroundOpacity(customForegroundOpacity);
        
        // Create initial configuration with PowerShell
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell,
            BackgroundOpacity = customBackgroundOpacity,
            ForegroundOpacity = customForegroundOpacity,
            SelectedThemeName = "TestTheme"
        };
        config.Save();
        
        // Act - Change shell type to WSL (simulating UI shell type change)
        config.DefaultShellType = ShellType.Wsl;
        
        // Sync opacity values before saving (this is what the fix does)
        config.BackgroundOpacity = OpacityManager.CurrentBackgroundOpacity;
        config.ForegroundOpacity = OpacityManager.CurrentForegroundOpacity;
        config.Save();
        
        // Assert - Load configuration and verify opacity is preserved
        var loadedConfig = ThemeConfiguration.Load();
        
        Assert.That(loadedConfig.DefaultShellType, Is.EqualTo(ShellType.Wsl), 
            "Shell type should be updated to WSL");
        Assert.That(loadedConfig.BackgroundOpacity, Is.EqualTo(customBackgroundOpacity).Within(0.001f), 
            "Background opacity should be preserved when shell type changes");
        Assert.That(loadedConfig.ForegroundOpacity, Is.EqualTo(customForegroundOpacity).Within(0.001f), 
            "Foreground opacity should be preserved when shell type changes");
        
        // Verify OpacityManager still has correct values
        Assert.That(OpacityManager.CurrentBackgroundOpacity, Is.EqualTo(customBackgroundOpacity).Within(0.001f),
            "OpacityManager background opacity should remain unchanged");
        Assert.That(OpacityManager.CurrentForegroundOpacity, Is.EqualTo(customForegroundOpacity).Within(0.001f),
            "OpacityManager foreground opacity should remain unchanged");
    }

    [Test]
    public void ShellTypeChange_WithoutOpacitySync_ShouldResetOpacityToDefault()
    {
        // Arrange - Set custom opacity values
        const float customBackgroundOpacity = 0.6f;
        const float customForegroundOpacity = 0.9f;
        
        OpacityManager.SetBackgroundOpacity(customBackgroundOpacity);
        OpacityManager.SetForegroundOpacity(customForegroundOpacity);
        
        // Create initial configuration with PowerShell and custom opacity
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell,
            BackgroundOpacity = customBackgroundOpacity,
            ForegroundOpacity = customForegroundOpacity,
            SelectedThemeName = "TestTheme"
        };
        config.Save();
        
        // Act - Simulate the bug: create a NEW config instance (like TerminalController does)
        // and change shell type WITHOUT syncing opacity from OpacityManager
        var newConfig = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell,
            SelectedThemeName = "TestTheme"
            // Note: BackgroundOpacity and ForegroundOpacity will be default (1.0f)
        };
        newConfig.Save();
        
        // Assert - Load configuration and verify opacity was reset to default
        var loadedConfig = ThemeConfiguration.Load();
        
        Assert.That(loadedConfig.DefaultShellType, Is.EqualTo(ShellType.PowerShell), 
            "Shell type should be updated to Cmd");
        Assert.That(loadedConfig.BackgroundOpacity, Is.EqualTo(1.0f).Within(0.001f), 
            "Background opacity should be reset to default when not synced");
        Assert.That(loadedConfig.ForegroundOpacity, Is.EqualTo(1.0f).Within(0.001f), 
            "Foreground opacity should be reset to default when not synced");
        
        // OpacityManager should still have the custom values (demonstrating the disconnect)
        Assert.That(OpacityManager.CurrentBackgroundOpacity, Is.EqualTo(customBackgroundOpacity).Within(0.001f),
            "OpacityManager background opacity should remain unchanged");
        Assert.That(OpacityManager.CurrentForegroundOpacity, Is.EqualTo(customForegroundOpacity).Within(0.001f),
            "OpacityManager foreground opacity should remain unchanged");
    }

    [Test]
    public void MultipleShellTypeChanges_WithOpacitySync_ShouldAlwaysPreserveOpacity()
    {
        // Arrange - Set custom opacity values
        const float customBackgroundOpacity = 0.5f;
        const float customForegroundOpacity = 0.75f;
        
        OpacityManager.SetBackgroundOpacity(customBackgroundOpacity);
        OpacityManager.SetForegroundOpacity(customForegroundOpacity);
        
        var shellTypes = new[] { ShellType.PowerShell, ShellType.Wsl, ShellType.Cmd, ShellType.PowerShellCore };
        
        // Act & Assert - Change shell type multiple times
        foreach (var shellType in shellTypes)
        {
            var config = ThemeConfiguration.Load();
            config.DefaultShellType = shellType;
            
            // Sync opacity values before saving (the fix)
            config.BackgroundOpacity = OpacityManager.CurrentBackgroundOpacity;
            config.ForegroundOpacity = OpacityManager.CurrentForegroundOpacity;
            config.Save();
            
            // Verify opacity is preserved after each change
            var loadedConfig = ThemeConfiguration.Load();
            
            Assert.That(loadedConfig.DefaultShellType, Is.EqualTo(shellType), 
                $"Shell type should be updated to {shellType}");
            Assert.That(loadedConfig.BackgroundOpacity, Is.EqualTo(customBackgroundOpacity).Within(0.001f), 
                $"Background opacity should be preserved when changing to {shellType}");
            Assert.That(loadedConfig.ForegroundOpacity, Is.EqualTo(customForegroundOpacity).Within(0.001f), 
                $"Foreground opacity should be preserved when changing to {shellType}");
        }
    }

    [Test]
    public void OpacityChange_AfterShellTypeChange_ShouldWorkCorrectly()
    {
        // Arrange - Start with default opacity and PowerShell
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell,
            SelectedThemeName = "TestTheme"
        };
        config.Save();
        
        // Act - Change shell type first
        config.DefaultShellType = ShellType.Wsl;
        config.BackgroundOpacity = OpacityManager.CurrentBackgroundOpacity;
        config.ForegroundOpacity = OpacityManager.CurrentForegroundOpacity;
        config.Save();
        
        // Then change opacity
        const float newBackgroundOpacity = 0.8f;
        const float newForegroundOpacity = 0.6f;
        
        OpacityManager.SetBackgroundOpacity(newBackgroundOpacity);
        OpacityManager.SetForegroundOpacity(newForegroundOpacity);
        
        // Assert - Verify opacity change worked after shell type change
        Assert.That(OpacityManager.CurrentBackgroundOpacity, Is.EqualTo(newBackgroundOpacity).Within(0.001f),
            "OpacityManager should accept new background opacity after shell type change");
        Assert.That(OpacityManager.CurrentForegroundOpacity, Is.EqualTo(newForegroundOpacity).Within(0.001f),
            "OpacityManager should accept new foreground opacity after shell type change");
        
        // Verify the configuration file was updated by OpacityManager
        var finalConfig = ThemeConfiguration.Load();
        Assert.That(finalConfig.BackgroundOpacity, Is.EqualTo(newBackgroundOpacity).Within(0.001f),
            "Configuration file should have updated background opacity");
        Assert.That(finalConfig.ForegroundOpacity, Is.EqualTo(newForegroundOpacity).Within(0.001f),
            "Configuration file should have updated foreground opacity");
        Assert.That(finalConfig.DefaultShellType, Is.EqualTo(ShellType.Wsl),
            "Shell type should remain unchanged");
    }
}