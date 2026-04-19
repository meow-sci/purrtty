using NUnit.Framework;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers;
using caTTY.Core.Terminal;
using System.IO;
using System.Linq;

namespace caTTY.Display.Tests.Integration;

/// <summary>
/// Integration tests for shell availability checking in the UI.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ShellAvailabilityIntegrationTests
{
    private string? _originalAppDataOverride;
    private string? _tempConfigDirectory;

    [SetUp]
    public void SetUp()
    {
        // Create a temporary directory for test configuration
        _tempConfigDirectory = Path.Combine(Path.GetTempPath(), "caTTY_Test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempConfigDirectory);

        // Override the app data directory for testing
        _originalAppDataOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _tempConfigDirectory;
    }

    [TearDown]
    public void TearDown()
    {
        // Restore original app data override
        ThemeConfiguration.OverrideConfigDirectory = _originalAppDataOverride;

        // Clean up temporary directory
        if (_tempConfigDirectory != null && Directory.Exists(_tempConfigDirectory))
        {
            Directory.Delete(_tempConfigDirectory, true);
        }
    }

    [Test]
    public void TerminalController_WithAvailableShells_ShouldInitializeSuccessfully()
    {
        // Arrange - Create configuration with an available shell (Cmd should always be available on Windows)
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell,
            SelectedThemeName = "TestTheme"
        };
        config.Save();

        // Act - Create TerminalController (should load configuration and check availability)
        using var sessionManager = new SessionManager();
        using var controller = new TerminalController(sessionManager);

        // Assert - Should initialize without issues
        var defaultOptions = sessionManager.DefaultLaunchOptions;
        Assert.That(defaultOptions.ShellType, Is.EqualTo(ShellType.PowerShell));
    }

    [Test]
    public void ShellAvailabilityChecker_ShouldFindAtLeastBasicShells()
    {
        // Act
        var availableShells = ShellAvailabilityChecker.GetAvailableShells();

        // Assert - On Windows, we should at least have Cmd, Auto, and Custom
        Assert.That(availableShells, Contains.Item(ShellType.Auto), "Auto shell should always be available");
        Assert.That(availableShells, Contains.Item(ShellType.Custom), "Custom shell should always be available");
        Assert.That(availableShells, Contains.Item(ShellType.Cmd), "Command Prompt should be available on Windows");
        
    }

    [Test]
    public void ShellAvailabilityChecker_WithNamesMethod_ShouldProvideUserFriendlyNames()
    {
        // Act
        var availableShells = ShellAvailabilityChecker.GetAvailableShellsWithNames();

        // Assert
        Assert.That(availableShells.Count, Is.GreaterThan(0), "Should have at least one available shell");
        
        foreach (var (shellType, displayName) in availableShells)
        {
            Assert.That(displayName, Is.Not.Null.And.Not.Empty, $"Display name for {shellType} should not be empty");
            Assert.That(displayName.Length, Is.GreaterThan(5), $"Display name '{displayName}' should be descriptive");
        }
        
    }

    [Test]
    public void ShellAvailabilityChecker_PowerShellAvailability_ShouldBeConsistent()
    {
        // Act
        bool powerShellAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.PowerShell);
        bool powerShellCoreAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.PowerShellCore);
        
        var availableShells = ShellAvailabilityChecker.GetAvailableShells();

        // Assert - Consistency check
        if (powerShellAvailable)
        {
            Assert.That(availableShells, Contains.Item(ShellType.PowerShell), 
                "If PowerShell is available individually, it should be in the available shells list");
        }
        
        if (powerShellCoreAvailable)
        {
            Assert.That(availableShells, Contains.Item(ShellType.PowerShellCore), 
                "If PowerShell Core is available individually, it should be in the available shells list");
        }
        
    }

    [Test]
    public void ShellAvailabilityChecker_WslAvailability_ShouldBeConsistent()
    {
        // Act
        bool wslAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.Wsl);
        var availableShells = ShellAvailabilityChecker.GetAvailableShells();

        // Assert - Consistency check
        if (wslAvailable)
        {
            Assert.That(availableShells, Contains.Item(ShellType.Wsl), 
                "If WSL is available individually, it should be in the available shells list");
        }
        else
        {
            Assert.That(availableShells, Does.Not.Contain(ShellType.Wsl), 
                "If WSL is not available individually, it should not be in the available shells list");
        }
        
    }

    [Test]
    public void TerminalController_WithUnavailableShellConfiguration_ShouldFallBackToAvailableShell()
    {
        // This test simulates what happens when a user has a configuration for a shell
        // that becomes unavailable (e.g., PowerShell Core gets uninstalled)
        
        // Arrange - Create configuration with PowerShell Core (might not be available)
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShellCore,
            SelectedThemeName = "TestTheme"
        };
        config.Save();

        // Act - Create TerminalController
        using var sessionManager = new SessionManager();
        using var controller = new TerminalController(sessionManager);

        // Assert - Should not throw, and should have some valid default
        var defaultOptions = sessionManager.DefaultLaunchOptions;
        Assert.That(defaultOptions, Is.Not.Null, "Default launch options should not be null");
        Assert.That(defaultOptions.ShellType, Is.Not.EqualTo((ShellType)(-1)), "Should have a valid shell type");
        
        // Check if PowerShell Core is available
        bool powerShellCoreAvailable = ShellAvailabilityChecker.IsShellAvailable(ShellType.PowerShellCore);
        
        if (powerShellCoreAvailable)
        {
            // If PowerShell Core is available, it should be used
            Assert.That(defaultOptions.ShellType, Is.EqualTo(ShellType.PowerShellCore), 
                "Should use PowerShell Core when it's available");
        }
        else
        {
            // If PowerShell Core is not available, should fall back to an available shell
            var availableShells = ShellAvailabilityChecker.GetAvailableShells();
            Assert.That(availableShells, Contains.Item(defaultOptions.ShellType), 
                "Should fall back to an available shell when configured shell is not available");
            Assert.That(defaultOptions.ShellType, Is.Not.EqualTo(ShellType.PowerShellCore), 
                "Should not use PowerShell Core when it's not available");
        }
        
    }

    [Test]
    public void ShellAvailabilityChecker_PerformanceTest_ShouldBeReasonablyFast()
    {
        // This test ensures that shell availability checking doesn't take too long
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Act - Check all shells multiple times
        for (int i = 0; i < 10; i++)
        {
            foreach (ShellType shellType in System.Enum.GetValues<ShellType>())
            {
                ShellAvailabilityChecker.IsShellAvailable(shellType);
            }
        }
        
        stopwatch.Stop();
        
        // Assert - Should complete within reasonable time (1 second for 10 iterations)
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000), 
            "Shell availability checking should be reasonably fast");
        
    }
}