using NUnit.Framework;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers;
using caTTY.Core.Terminal;

namespace caTTY.Display.Tests.Integration;

/// <summary>
/// Integration tests for shell configuration functionality.
/// </summary>
[TestFixture]
[Category("Integration")]
public class ShellConfigurationIntegrationTests
{
    private SessionManager _sessionManager = null!;
    private TerminalController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        // Create session manager with default configuration
        _sessionManager = new SessionManager();
        
        // Create controller which will load and apply shell configuration
        _controller = new TerminalController(_sessionManager);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
        _sessionManager?.Dispose();
    }

    [Test]
    public void ShellConfiguration_ShouldBeLoadedOnControllerInitialization()
    {
        // Act - Controller should have loaded configuration during initialization
        var defaultLaunchOptions = _sessionManager.DefaultLaunchOptions;
        
        // Assert - Should have some shell type configured (default is WSL)
        Assert.That(defaultLaunchOptions.ShellType, Is.Not.EqualTo(ShellType.Auto));
    }

    [Test]
    public void ShellConfiguration_ShouldPersistBetweenControllerInstances()
    {
        // Arrange - Create a configuration and save it
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell
        };
        config.Save();

        // Act - Create a new controller which should load the saved configuration
        using var newSessionManager = new SessionManager();
        using var newController = new TerminalController(newSessionManager);
        
        // Assert - New controller should have loaded the PowerShell configuration
        var loadedLaunchOptions = newSessionManager.DefaultLaunchOptions;
        Assert.That(loadedLaunchOptions.ShellType, Is.EqualTo(ShellType.PowerShell));
    }

    [Test]
    public void ShellConfiguration_ShouldCreateCorrectLaunchOptions()
    {
        // Arrange
        var testCases = new[]
        {
            ShellType.Wsl,
            ShellType.PowerShell,
            ShellType.PowerShellCore,
            ShellType.Cmd
        };

        foreach (var shellType in testCases)
        {
            // Act
            var config = new ThemeConfiguration { DefaultShellType = shellType };
            var launchOptions = config.CreateLaunchOptions();
            
            // Assert
            Assert.That(launchOptions.ShellType, Is.EqualTo(shellType));
        }
    }

    [Test]
    public void ShellConfiguration_WithCustomShell_ShouldSetCustomPath()
    {
        // Arrange
        var customPath = @"C:\test\custom\shell.exe";
        var config = new ThemeConfiguration 
        { 
            DefaultShellType = ShellType.Custom,
            CustomShellPath = customPath
        };

        // Act
        var launchOptions = config.CreateLaunchOptions();

        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(launchOptions.CustomShellPath, Is.EqualTo(customPath));
    }

    [Test]
    public void ShellConfiguration_WithWslDistribution_ShouldSetDistribution()
    {
        // Arrange
        var distribution = "Ubuntu";
        var config = new ThemeConfiguration 
        { 
            DefaultShellType = ShellType.Wsl,
            WslDistribution = distribution
        };

        // Act
        var launchOptions = config.CreateLaunchOptions();

        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(launchOptions.Arguments, Contains.Item("--distribution"));
        Assert.That(launchOptions.Arguments, Contains.Item(distribution));
    }
}