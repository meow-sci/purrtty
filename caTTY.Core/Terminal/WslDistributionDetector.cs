using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SystemProcess = System.Diagnostics.Process;
using SystemProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace caTTY.Core.Terminal;

/// <summary>
/// Detects installed WSL distributions on the current system.
/// Uses the `wsl --list --quiet` command to retrieve distribution names.
/// Results are cached for performance (5 minute expiration).
/// </summary>
public static class WslDistributionDetector
{
    /// <summary>
    /// Represents a WSL distribution installed on the system.
    /// </summary>
    public class WslDistribution
    {
        /// <summary>
        /// Gets or sets the distribution name as reported by WSL.
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// Gets or sets whether this is the default WSL distribution.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Gets or sets the formatted display name for UI presentation.
        /// </summary>
        public required string DisplayName { get; set; }
    }

    // Cache for detected distributions
    private static List<WslDistribution>? _cachedDistributions = null;
    private static DateTime? _cacheTime = null;
    private const int CacheExpirationMinutes = 5;

    /// <summary>
    /// Gets the list of installed WSL distributions.
    /// Results are cached for 5 minutes to avoid repeated process execution.
    /// </summary>
    /// <param name="forceRefresh">If true, ignores cache and re-detects distributions</param>
    /// <returns>List of installed WSL distributions, or empty list if WSL is not available</returns>
    public static List<WslDistribution> GetInstalledDistributions(bool forceRefresh = false)
    {
        try
        {
            // Check if WSL is available at all
            if (!ShellAvailabilityChecker.IsShellAvailable(ShellType.Wsl))
            {
                return new List<WslDistribution>();
            }

            // Check cache validity
            if (!forceRefresh && _cachedDistributions != null && _cacheTime != null)
            {
                TimeSpan cacheAge = DateTime.UtcNow - _cacheTime.Value;
                if (cacheAge.TotalMinutes < CacheExpirationMinutes)
                {
                    return _cachedDistributions;
                }
            }

            // Execute wsl --list --quiet to get distributions
            var distributions = ExecuteWslListCommand();

            // Update cache
            _cachedDistributions = distributions;
            _cacheTime = DateTime.UtcNow;

            return distributions;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WslDistributionDetector: Error detecting WSL distributions: {ex.Message}");
            return new List<WslDistribution>();
        }
    }

    /// <summary>
    /// Checks if WSL is available on the system.
    /// </summary>
    /// <returns>True if WSL is available, false otherwise</returns>
    public static bool IsWslAvailable()
    {
        return ShellAvailabilityChecker.IsShellAvailable(ShellType.Wsl);
    }

    /// <summary>
    /// Clears the cached distribution list, forcing a refresh on the next call to GetInstalledDistributions.
    /// </summary>
    public static void ClearCache()
    {
        _cachedDistributions = null;
        _cacheTime = null;
    }

    /// <summary>
    /// Executes the `wsl --list --quiet` command and parses the output.
    /// </summary>
    /// <returns>List of detected distributions</returns>
    private static List<WslDistribution> ExecuteWslListCommand()
    {
        var distributions = new List<WslDistribution>();

        try
        {
            var startInfo = new SystemProcessStartInfo
            {
                FileName = "wsl",
                Arguments = "--list --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode  // WSL outputs UTF-16 with BOM on Windows
            };

            using var process = SystemProcess.Start(startInfo);
            if (process == null)
            {
                Console.WriteLine("WslDistributionDetector: Failed to start wsl process");
                return distributions;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string error = process.StandardError.ReadToEnd();
                Console.WriteLine($"WslDistributionDetector: wsl command failed with exit code {process.ExitCode}: {error}");
                return distributions;
            }

            // Parse output - each line is a distribution name
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // First distribution is usually the default, but we don't rely on order
            // Instead, we'll mark the first one as default if there's no other indicator
            bool isFirst = true;

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    continue;
                }

                // Remove BOM if present
                if (trimmedLine.Length > 0 && trimmedLine[0] == '\uFEFF')
                {
                    trimmedLine = trimmedLine.Substring(1);
                }

                // WSL --list --quiet outputs distribution names one per line
                // The default distribution is typically first, but not always marked
                bool isDefault = isFirst;
                isFirst = false;

                var distro = new WslDistribution
                {
                    Name = trimmedLine,
                    IsDefault = isDefault,
                    DisplayName = FormatDistributionDisplayName(trimmedLine, isDefault)
                };

                distributions.Add(distro);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WslDistributionDetector: Exception while executing wsl command: {ex.Message}");
        }

        return distributions;
    }

    /// <summary>
    /// Formats a distribution name for display in the UI.
    /// </summary>
    /// <param name="name">The distribution name</param>
    /// <param name="isDefault">Whether this is the default distribution</param>
    /// <returns>Formatted display name</returns>
    private static string FormatDistributionDisplayName(string name, bool isDefault)
    {
        return isDefault ? $"{name} (Default)" : name;
    }
}
