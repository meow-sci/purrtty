using NUnit.Framework;
using caTTY.Core.Terminal;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
/// Unit tests for ShellConfiguration helper methods.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ShellConfigurationTests
{
    [Test]
    public void Default_ShouldReturnValidDefaultOptions()
    {
        // Act
        var options = ShellConfiguration.Default();

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.InitialWidth, Is.EqualTo(80));
        Assert.That(options.InitialHeight, Is.EqualTo(24));
        Assert.That(options.CreateWindow, Is.False);
        Assert.That(options.UseShellExecute, Is.False);
        Assert.That(options.EnvironmentVariables, Contains.Key("TERM"));
    }

    [Test]
    public void Wsl_ShouldReturnValidWslOptions()
    {
        // Act
        var options = ShellConfiguration.Wsl();

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Wsl));
    }

    [Test]
    public void Wsl_WithDistribution_ShouldReturnValidWslOptions()
    {
        // Arrange
        const string distribution = "Ubuntu";

        // Act
        var options = ShellConfiguration.Wsl(distribution);

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(options.Arguments, Contains.Item("--distribution"));
        Assert.That(options.Arguments, Contains.Item(distribution));
    }

    [Test]
    public void Wsl_WithDistributionAndWorkingDirectory_ShouldReturnValidWslOptions()
    {
        // Arrange
        const string distribution = "Ubuntu";
        const string workingDirectory = "/home/user";

        // Act
        var options = ShellConfiguration.Wsl(distribution, workingDirectory);

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(options.Arguments, Contains.Item("--distribution"));
        Assert.That(options.Arguments, Contains.Item(distribution));
        Assert.That(options.Arguments, Contains.Item("--cd"));
        Assert.That(options.Arguments, Contains.Item(workingDirectory));
    }

    [Test]
    public void PowerShell_ShouldReturnValidPowerShellOptions()
    {
        // Act
        var options = ShellConfiguration.PowerShell();

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.PowerShell));
    }

    [Test]
    public void PowerShellCore_ShouldReturnValidPowerShellCoreOptions()
    {
        // Act
        var options = ShellConfiguration.PowerShellCore();

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.PowerShellCore));
        Assert.That(options.Arguments, Contains.Item("-NoLogo"));
        Assert.That(options.Arguments, Contains.Item("-NoProfile"));
    }

    [Test]
    public void Cmd_ShouldReturnValidCmdOptions()
    {
        // Act
        var options = ShellConfiguration.Cmd();

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Cmd));
    }

    [Test]
    public void Custom_ShouldReturnValidCustomOptions()
    {
        // Arrange
        const string shellPath = "test.exe";
        const string arg1 = "-arg1";
        const string arg2 = "-arg2";

        // Act
        var options = ShellConfiguration.Custom(shellPath, arg1, arg2);

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(options.CustomShellPath, Is.EqualTo(shellPath));
        Assert.That(options.Arguments, Contains.Item(arg1));
        Assert.That(options.Arguments, Contains.Item(arg2));
    }

    [Test]
    public void CustomGame_ShouldReturnValidCustomGameOptions()
    {
        // Arrange
        const string customShellId = "game-rcs-shell";

        // Act
        var options = ShellConfiguration.CustomGame(customShellId);

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.CustomGame));
        Assert.That(options.CustomShellId, Is.EqualTo(customShellId));
        Assert.That(options.Arguments, Is.Empty, "Custom game shell should have no default arguments");
        Assert.That(options.InitialWidth, Is.EqualTo(80));
        Assert.That(options.InitialHeight, Is.EqualTo(24));
        Assert.That(options.CreateWindow, Is.False);
        Assert.That(options.UseShellExecute, Is.False);
    }

    [Test]
    public void Common_Ubuntu_ShouldReturnValidUbuntuOptions()
    {
        // Act
        var options = ShellConfiguration.Common.Ubuntu;

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(options.Arguments, Contains.Item("--distribution"));
        Assert.That(options.Arguments, Contains.Item("Ubuntu"));
    }

    [Test]
    public void Common_Debian_ShouldReturnValidDebianOptions()
    {
        // Act
        var options = ShellConfiguration.Common.Debian;

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(options.Arguments, Contains.Item("--distribution"));
        Assert.That(options.Arguments, Contains.Item("Debian"));
    }

    [Test]
    public void Common_Alpine_ShouldReturnValidAlpineOptions()
    {
        // Act
        var options = ShellConfiguration.Common.Alpine;

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(options.Arguments, Contains.Item("--distribution"));
        Assert.That(options.Arguments, Contains.Item("Alpine"));
    }

    [Test]
    public void Common_GitBash_ShouldReturnValidGitBashOptions()
    {
        // Act
        var options = ShellConfiguration.Common.GitBash;

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(options.CustomShellPath, Is.EqualTo(@"C:\Program Files\Git\bin\bash.exe"));
        Assert.That(options.Arguments, Contains.Item("--login"));
    }

    [Test]
    public void Common_Msys2Bash_ShouldReturnValidMsys2BashOptions()
    {
        // Act
        var options = ShellConfiguration.Common.Msys2Bash;

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(options.CustomShellPath, Is.EqualTo(@"C:\msys64\usr\bin\bash.exe"));
        Assert.That(options.Arguments, Contains.Item("--login"));
    }

    [Test]
    public void Common_CygwinBash_ShouldReturnValidCygwinBashOptions()
    {
        // Act
        var options = ShellConfiguration.Common.CygwinBash;

        // Assert
        Assert.That(options, Is.Not.Null);
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(options.CustomShellPath, Is.EqualTo(@"C:\cygwin64\bin\bash.exe"));
        Assert.That(options.Arguments, Contains.Item("--login"));
    }
}