using NUnit.Framework;
using caTTY.Display.Configuration;
using caTTY.Core.Terminal;

namespace caTTY.Display.Tests.Unit.Configuration;

/// <summary>
/// Tests for shell configuration functionality in ThemeConfiguration.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ShellConfigurationTests
{
    [Test]
    public void DefaultShellType_ShouldBePowerShell()
    {
        // Arrange & Act
        var config = new ThemeConfiguration();
        
        // Assert
        Assert.That(config.DefaultShellType, Is.EqualTo(ShellType.PowerShell));
    }

    [Test]
    public void CreateLaunchOptions_WithWslDefault_ShouldCreateWslOptions()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Wsl,
            WslDistribution = null
        };
        
        // Act
        var launchOptions = config.CreateLaunchOptions();
        
        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(launchOptions.Arguments, Does.Not.Contain("--distribution"));
    }

    [Test]
    public void CreateLaunchOptions_WithWslDistribution_ShouldCreateWslOptionsWithDistribution()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Wsl,
            WslDistribution = "Ubuntu"
        };
        
        // Act
        var launchOptions = config.CreateLaunchOptions();
        
        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(launchOptions.Arguments, Contains.Item("--distribution"));
        Assert.That(launchOptions.Arguments, Contains.Item("Ubuntu"));
    }

    [Test]
    public void CreateLaunchOptions_WithPowerShell_ShouldCreatePowerShellOptions()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell
        };
        
        // Act
        var launchOptions = config.CreateLaunchOptions();
        
        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.PowerShell));
    }

    [Test]
    public void CreateLaunchOptions_WithPowerShellCore_ShouldCreatePowerShellCoreOptions()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShellCore
        };
        
        // Act
        var launchOptions = config.CreateLaunchOptions();
        
        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.PowerShellCore));
    }

    [Test]
    public void CreateLaunchOptions_WithCmd_ShouldCreateCmdOptions()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Cmd
        };
        
        // Act
        var launchOptions = config.CreateLaunchOptions();
        
        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Cmd));
    }

    [Test]
    public void CreateLaunchOptions_WithCustomShell_ShouldCreateCustomOptions()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Custom,
            CustomShellPath = @"C:\msys64\usr\bin\bash.exe"
        };
        
        // Act
        var launchOptions = config.CreateLaunchOptions();
        
        // Assert
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(launchOptions.CustomShellPath, Is.EqualTo(@"C:\msys64\usr\bin\bash.exe"));
    }

    [Test]
    public void GetShellDisplayName_WithWslDefault_ShouldReturnCorrectName()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Wsl,
            WslDistribution = null
        };
        
        // Act
        var displayName = config.GetShellDisplayName();
        
        // Assert
        Assert.That(displayName, Is.EqualTo("WSL2 (Default)"));
    }

    [Test]
    public void GetShellDisplayName_WithWslDistribution_ShouldReturnDistributionName()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Wsl,
            WslDistribution = "Ubuntu"
        };
        
        // Act
        var displayName = config.GetShellDisplayName();
        
        // Assert
        Assert.That(displayName, Is.EqualTo("WSL2 (Ubuntu)"));
    }

    [Test]
    public void GetShellDisplayName_WithPowerShell_ShouldReturnCorrectName()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShell
        };
        
        // Act
        var displayName = config.GetShellDisplayName();
        
        // Assert
        Assert.That(displayName, Is.EqualTo("Windows PowerShell"));
    }

    [Test]
    public void GetShellDisplayName_WithPowerShellCore_ShouldReturnCorrectName()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.PowerShellCore
        };
        
        // Act
        var displayName = config.GetShellDisplayName();
        
        // Assert
        Assert.That(displayName, Is.EqualTo("PowerShell Core"));
    }

    [Test]
    public void GetShellDisplayName_WithCmd_ShouldReturnCorrectName()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Cmd
        };
        
        // Act
        var displayName = config.GetShellDisplayName();
        
        // Assert
        Assert.That(displayName, Is.EqualTo("Command Prompt"));
    }

    [Test]
    public void GetShellDisplayName_WithCustomShell_ShouldReturnCustomName()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Custom,
            CustomShellPath = @"C:\msys64\usr\bin\bash.exe"
        };
        
        // Act
        var displayName = config.GetShellDisplayName();
        
        // Assert
        Assert.That(displayName, Is.EqualTo("Custom (bash.exe)"));
    }

    [Test]
    public void GetShellDisplayName_WithAutoDetect_ShouldReturnAutoDetectName()
    {
        // Arrange
        var config = new ThemeConfiguration
        {
            DefaultShellType = ShellType.Auto
        };
        
        // Act
        var displayName = config.GetShellDisplayName();
        
        // Assert
        Assert.That(displayName, Is.EqualTo("Auto-detect"));
    }
}