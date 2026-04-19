using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using caTTY.Core.Terminal;

namespace caTTY.Display.Utils;

/// <summary>
/// Provides helper methods for shell selection UI across different components.
/// Centralizes shell detection, option generation, and session creation logic.
/// </summary>
public static class ShellSelectionHelper
{
    /// <summary>
    /// Represents a shell option for display in the UI.
    /// </summary>
    public class ShellOption
    {
        /// <summary>
        /// Gets or sets the shell type.
        /// </summary>
        public required ShellType ShellType { get; set; }

        /// <summary>
        /// Gets or sets the display name for the UI.
        /// </summary>
        public required string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the WSL distribution name (only for WSL shells).
        /// </summary>
        public string? WslDistribution { get; set; }

        /// <summary>
        /// Gets or sets the custom shell ID (only for CustomGame shells).
        /// </summary>
        public string? CustomShellId { get; set; }

        /// <summary>
        /// Gets or sets the tooltip text for the option.
        /// </summary>
        public string? Tooltip { get; set; }
    }

    /// <summary>
    /// Gets all available shell options on the current system.
    /// </summary>
    /// <returns>List of available shell options</returns>
    public static List<ShellOption> GetAvailableShellOptions()
    {
        var options = new List<ShellOption>();

        // Add PowerShell if available
        if (ShellAvailabilityChecker.IsShellAvailable(ShellType.PowerShell))
        {
            options.Add(new ShellOption
            {
                ShellType = ShellType.PowerShell,
                DisplayName = "Windows PowerShell",
                Tooltip = "Traditional Windows PowerShell (powershell.exe)"
            });
        }

        // Add PowerShell Core if available
        if (ShellAvailabilityChecker.IsShellAvailable(ShellType.PowerShellCore))
        {
            options.Add(new ShellOption
            {
                ShellType = ShellType.PowerShellCore,
                DisplayName = "PowerShell Core",
                Tooltip = "Modern cross-platform PowerShell (pwsh.exe)"
            });
        }

        // Add Cmd if available
        if (ShellAvailabilityChecker.IsShellAvailable(ShellType.Cmd))
        {
            options.Add(new ShellOption
            {
                ShellType = ShellType.Cmd,
                DisplayName = "Command Prompt",
                Tooltip = "Windows Command Prompt (cmd.exe)"
            });
        }

        // Add WSL distributions if available
        if (ShellAvailabilityChecker.IsShellAvailable(ShellType.Wsl))
        {
            var distros = WslDistributionDetector.GetInstalledDistributions();

            if (distros.Count == 0)
            {
                // Add generic WSL option if no distributions detected
                options.Add(new ShellOption
                {
                    ShellType = ShellType.Wsl,
                    DisplayName = "WSL (Default Distribution)",
                    Tooltip = "Windows Subsystem for Linux - Default distribution"
                });
            }
            else
            {
                // Add option for each detected distribution
                foreach (var distro in distros)
                {
                    options.Add(new ShellOption
                    {
                        ShellType = ShellType.Wsl,
                        DisplayName = $"WSL - {distro.DisplayName}",
                        WslDistribution = distro.Name,
                        Tooltip = $"Windows Subsystem for Linux - {distro.Name}"
                    });
                }
            }
        }

        // Note: Custom shells are not included in the selection menu
        // Custom shells would require additional UI for configuration (file picker, etc.)

        // Add custom game shells from registry
        var customShells = CustomShellRegistry.Instance.GetAvailableShells();
        foreach (var (shellId, metadata) in customShells)
        {
            options.Add(new ShellOption
            {
                ShellType = ShellType.CustomGame,
                DisplayName = metadata.Name,
                CustomShellId = shellId,
                Tooltip = metadata.Description
            });
        }

        return options;
    }

    /// <summary>
    /// Creates launch options from a shell option.
    /// </summary>
    /// <param name="option">The shell option to convert</param>
    /// <returns>Process launch options configured for the specified shell</returns>
    public static ProcessLaunchOptions CreateLaunchOptions(ShellOption option)
    {
        return option.ShellType switch
        {
            ShellType.PowerShell => ShellConfiguration.PowerShell(),
            ShellType.PowerShellCore => ShellConfiguration.PowerShellCore(),
            ShellType.Cmd => ShellConfiguration.Cmd(),
            ShellType.Wsl when option.WslDistribution != null => ShellConfiguration.Wsl(option.WslDistribution),
            ShellType.Wsl => ShellConfiguration.Wsl(),
            ShellType.CustomGame when option.CustomShellId != null => ProcessLaunchOptions.CreateCustomGame(option.CustomShellId),
            _ => ProcessLaunchOptions.CreateDefault()
        };
    }

    /// <summary>
    /// Creates a new terminal session with the specified shell option.
    /// </summary>
    /// <param name="sessionManager">The session manager to use</param>
    /// <param name="option">The shell option to use for the new session</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    public static async Task CreateSessionWithShell(
        SessionManager sessionManager,
        ShellOption option,
        CancellationToken cancellationToken = default)
    {
        var launchOptions = CreateLaunchOptions(option);
        var title = GenerateSessionTitle(option);
        await sessionManager.CreateSessionAsync(title, launchOptions, cancellationToken);
    }

    /// <summary>
    /// Generates a session title based on the shell option.
    /// </summary>
    /// <param name="option">The shell option</param>
    /// <returns>Generated session title</returns>
    private static string GenerateSessionTitle(ShellOption option)
    {
        return option.ShellType switch
        {
            ShellType.PowerShell => "PowerShell",
            ShellType.PowerShellCore => "pwsh",
            ShellType.Cmd => "cmd",
            ShellType.Wsl when option.WslDistribution != null => option.WslDistribution,
            ShellType.Wsl => "WSL",
            ShellType.CustomGame => option.DisplayName,
            _ => "Terminal"
        };
    }
}
