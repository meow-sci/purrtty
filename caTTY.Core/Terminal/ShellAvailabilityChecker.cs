using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace caTTY.Core.Terminal;

/// <summary>
/// Utility class for checking shell availability on the current system.
/// Uses the same logic as ProcessManager to determine if shells are available.
/// </summary>
public static class ShellAvailabilityChecker
{
    /// <summary>
    /// Checks if a specific shell type is available on the current system.
    /// </summary>
    /// <param name="shellType">The shell type to check</param>
    /// <returns>True if the shell is available, false otherwise</returns>
    public static bool IsShellAvailable(ShellType shellType)
    {
        try
        {
            return shellType switch
            {
                ShellType.Auto => true, // Auto is always available as it falls back to other shells
                ShellType.Wsl => IsWslAvailable(),
                ShellType.PowerShell => IsPowerShellAvailable(),
                ShellType.PowerShellCore => IsPowerShellCoreAvailable(),
                ShellType.Cmd => IsCmdAvailable(),
                ShellType.Custom => true, // Custom shells are checked when the path is provided
                ShellType.CustomGame => true, // Game Console is always available
                _ => false
            };
        }
        catch
        {
            // If any exception occurs during checking, assume the shell is not available
            return false;
        }
    }

    /// <summary>
    /// Gets all available shell types on the current system.
    /// </summary>
    /// <returns>List of available shell types</returns>
    public static List<ShellType> GetAvailableShells()
    {
        var availableShells = new List<ShellType>();

        foreach (ShellType shellType in Enum.GetValues<ShellType>())
        {
            if (IsShellAvailable(shellType))
            {
                availableShells.Add(shellType);
            }
        }

        return availableShells;
    }

    /// <summary>
    /// Gets available shell types with their display names.
    /// </summary>
    /// <returns>List of tuples containing shell type and display name for available shells</returns>
    public static List<(ShellType ShellType, string DisplayName)> GetAvailableShellsWithNames()
    {
        var availableShells = new List<(ShellType, string)>();

        var shellDefinitions = new[]
        {
            (ShellType.PowerShell, "Windows PowerShell"),
            (ShellType.Wsl, "WSL2 (Windows Subsystem for Linux)"),
            (ShellType.Cmd, "Command Prompt"),
            (ShellType.CustomGame, "Game Console")
        };

        foreach (var (shellType, displayName) in shellDefinitions)
        {
            if (IsShellAvailable(shellType))
            {
                availableShells.Add((shellType, displayName));
            }
        }

        return availableShells;
    }

    /// <summary>
    /// Checks if WSL is available on the system.
    /// Uses the same logic as ProcessManager.ResolveWsl.
    /// </summary>
    private static bool IsWslAvailable()
    {
        // Check PATH first
        string? shellPath = FindExecutableInPath("wsl.exe") ?? FindExecutableInPath("wsl");

        if (shellPath != null && File.Exists(shellPath))
        {
            return true;
        }

        // Try common WSL installation paths
        string[] wslPaths = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wsl.exe")
        ];

        return wslPaths.Any(File.Exists);
    }

    /// <summary>
    /// Checks if Windows PowerShell is available on the system.
    /// Uses the same logic as ProcessManager.ResolvePowerShell.
    /// </summary>
    private static bool IsPowerShellAvailable()
    {
        string? shellPath = FindExecutableInPath("powershell.exe");
        
        if (shellPath != null && File.Exists(shellPath))
        {
            return true;
        }

        // Try common PowerShell installation path
        string commonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");

        return File.Exists(commonPath);
    }

    /// <summary>
    /// Checks if PowerShell Core is available on the system.
    /// Uses the same logic as ProcessManager.ResolvePowerShellCore.
    /// </summary>
    private static bool IsPowerShellCoreAvailable()
    {
        string? shellPath = FindExecutableInPath("pwsh.exe") ?? FindExecutableInPath("pwsh");
        return shellPath != null && File.Exists(shellPath);
    }

    /// <summary>
    /// Checks if Command Prompt is available on the system.
    /// Uses the same logic as ProcessManager.ResolveCmd.
    /// </summary>
    private static bool IsCmdAvailable()
    {
        string? shellPath = FindExecutableInPath("cmd.exe");
        
        if (shellPath != null && File.Exists(shellPath))
        {
            return true;
        }

        // Try common cmd.exe installation path
        string commonPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        return File.Exists(commonPath);
    }

    /// <summary>
    /// Finds an executable in the system PATH.
    /// Uses the same logic as ProcessManager.FindExecutableInPath.
    /// </summary>
    /// <param name="executableName">Name of the executable to find</param>
    /// <returns>Full path to the executable if found, null otherwise</returns>
    private static string? FindExecutableInPath(string executableName)
    {
        try
        {
            string? pathVariable = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathVariable))
            {
                return null;
            }

            string[] paths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

            foreach (string path in paths)
            {
                try
                {
                    string fullPath = Path.Combine(path, executableName);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                    // Skip invalid paths
                    continue;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}