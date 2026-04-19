using System;
using System.Linq;
using caTTY.Core.Terminal;
using caTTY.Display.Utils;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Utils;

[TestFixture]
[Category("Unit")]
public class ShellSelectionHelperTests
{
    [Test]
    public void GetAvailableShellOptions_ReturnsNonNullList()
    {
        var options = ShellSelectionHelper.GetAvailableShellOptions();

        Assert.That(options, Is.Not.Null);
        Assert.That(options.Count, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GetAvailableShellOptions_OnlyIncludesAvailableShells()
    {
        var options = ShellSelectionHelper.GetAvailableShellOptions();

        // Each shell option should correspond to an available shell
        foreach (var option in options)
        {
            bool isAvailable = ShellAvailabilityChecker.IsShellAvailable(option.ShellType);
            Assert.That(isAvailable, Is.True,
                $"Shell option {option.DisplayName} (type: {option.ShellType}) is not available");
        }
    }

    [Test]
    public void GetAvailableShellOptions_AllOptionsHaveDisplayName()
    {
        var options = ShellSelectionHelper.GetAvailableShellOptions();

        foreach (var option in options)
        {
            Assert.That(option.DisplayName, Is.Not.Null);
            Assert.That(option.DisplayName, Is.Not.Empty);
        }
    }

    [Test]
    public void GetAvailableShellOptions_AllOptionsHaveTooltip()
    {
        var options = ShellSelectionHelper.GetAvailableShellOptions();

        foreach (var option in options)
        {
            Assert.That(option.Tooltip, Is.Not.Null);
            Assert.That(option.Tooltip, Is.Not.Empty);
        }
    }

    [Test]
    public void CreateLaunchOptions_PowerShell_ReturnsCorrectOptions()
    {
        var option = new ShellSelectionHelper.ShellOption
        {
            ShellType = ShellType.PowerShell,
            DisplayName = "Windows PowerShell",
            Tooltip = "Test"
        };

        var launchOptions = ShellSelectionHelper.CreateLaunchOptions(option);

        Assert.That(launchOptions, Is.Not.Null);
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.PowerShell));
    }

    [Test]
    public void CreateLaunchOptions_PowerShellCore_ReturnsCorrectOptions()
    {
        var option = new ShellSelectionHelper.ShellOption
        {
            ShellType = ShellType.PowerShellCore,
            DisplayName = "PowerShell Core",
            Tooltip = "Test"
        };

        var launchOptions = ShellSelectionHelper.CreateLaunchOptions(option);

        Assert.That(launchOptions, Is.Not.Null);
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.PowerShellCore));
    }

    [Test]
    public void CreateLaunchOptions_Cmd_ReturnsCorrectOptions()
    {
        var option = new ShellSelectionHelper.ShellOption
        {
            ShellType = ShellType.Cmd,
            DisplayName = "Command Prompt",
            Tooltip = "Test"
        };

        var launchOptions = ShellSelectionHelper.CreateLaunchOptions(option);

        Assert.That(launchOptions, Is.Not.Null);
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Cmd));
    }

    [Test]
    public void CreateLaunchOptions_Wsl_WithDistro_ReturnsCorrectOptions()
    {
        var option = new ShellSelectionHelper.ShellOption
        {
            ShellType = ShellType.Wsl,
            DisplayName = "WSL - Ubuntu",
            WslDistribution = "Ubuntu",
            Tooltip = "Test"
        };

        var launchOptions = ShellSelectionHelper.CreateLaunchOptions(option);

        Assert.That(launchOptions, Is.Not.Null);
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Wsl));
        // Distribution is stored in Arguments as "--distribution", "Ubuntu"
        Assert.That(launchOptions.Arguments, Contains.Item("--distribution"));
        Assert.That(launchOptions.Arguments, Contains.Item("Ubuntu"));
    }

    [Test]
    public void CreateLaunchOptions_Wsl_WithoutDistro_ReturnsCorrectOptions()
    {
        var option = new ShellSelectionHelper.ShellOption
        {
            ShellType = ShellType.Wsl,
            DisplayName = "WSL (Default Distribution)",
            Tooltip = "Test"
        };

        var launchOptions = ShellSelectionHelper.CreateLaunchOptions(option);

        Assert.That(launchOptions, Is.Not.Null);
        Assert.That(launchOptions.ShellType, Is.EqualTo(ShellType.Wsl));
    }

    [Test]
    public void ShellOption_CanBeCreatedWithRequiredProperties()
    {
        var option = new ShellSelectionHelper.ShellOption
        {
            ShellType = ShellType.PowerShell,
            DisplayName = "Test Shell",
            Tooltip = "Test Tooltip"
        };

        Assert.That(option.ShellType, Is.EqualTo(ShellType.PowerShell));
        Assert.That(option.DisplayName, Is.EqualTo("Test Shell"));
        Assert.That(option.Tooltip, Is.EqualTo("Test Tooltip"));
        Assert.That(option.WslDistribution, Is.Null);
    }

    [Test]
    public void ShellOption_CanHaveWslDistribution()
    {
        var option = new ShellSelectionHelper.ShellOption
        {
            ShellType = ShellType.Wsl,
            DisplayName = "WSL - Ubuntu",
            WslDistribution = "Ubuntu",
            Tooltip = "Test"
        };

        Assert.That(option.WslDistribution, Is.EqualTo("Ubuntu"));
    }

    [Test]
    public void GetAvailableShellOptions_DoesNotIncludeCustomGame()
    {
        var options = ShellSelectionHelper.GetAvailableShellOptions();

        var customGameOptions = options.Where(o => o.ShellType == ShellType.CustomGame).ToList();

        Assert.That(customGameOptions.Count, Is.EqualTo(0),
            "CustomGame shells should not be included in shell selection options");
    }

    [Test]
    public void GetAvailableShellOptions_WslDistributionsHaveDistributionName()
    {
        var options = ShellSelectionHelper.GetAvailableShellOptions();

        var wslOptions = options.Where(o => o.ShellType == ShellType.Wsl).ToList();

        foreach (var wslOption in wslOptions)
        {
            // WSL options should either have a distribution name or be the default option
            if (wslOption.DisplayName.Contains("Default Distribution"))
            {
                // Default option may not have WslDistribution set
                // This is acceptable
            }
            else
            {
                // Specific distribution should have WslDistribution set
                Assert.That(wslOption.WslDistribution, Is.Not.Null,
                    $"WSL option '{wslOption.DisplayName}' should have WslDistribution set");
            }
        }
    }
}
