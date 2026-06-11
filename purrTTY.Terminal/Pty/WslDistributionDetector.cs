using System;
using System.Collections.Generic;
using System.Text;
using SystemProcess = System.Diagnostics.Process;
using SystemProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace purrTTY.Core.Terminal;

/// <summary>
/// Detects installed WSL distributions on the current system.
/// Uses the `wsl --list --quiet` command to retrieve distribution names.
/// Results are cached for the process lifetime (installed distributions do not
/// change mid-game); detection is expected to run once, off the render thread,
/// because spawning wsl.exe can block for seconds while the WSL service starts.
/// Detection failure degrades to an empty list (no WSL menu entries). Like
/// <see cref="UnixShellDetector"/>, deliberately no game-logging dependency so
/// the detector stays usable from the test host.
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

    // Cache for detected distributions (process lifetime)
    private static List<WslDistribution>? _cachedDistributions;
    private static readonly object CacheLock = new();

    // Bounds the wait on `wsl --list` so a wedged WSL service cannot stall the
    // detection thread forever; generous because a cold service start is slow.
    private const int WslListTimeoutMs = 15_000;

    /// <summary>
    /// Gets the list of installed WSL distributions.
    /// Results are cached for the process lifetime to avoid repeated process execution;
    /// the cache is consulted before any availability probing so cached calls do no work.
    /// </summary>
    /// <param name="forceRefresh">If true, ignores cache and re-detects distributions</param>
    /// <returns>List of installed WSL distributions, or empty list if WSL is not available</returns>
    public static List<WslDistribution> GetInstalledDistributions(bool forceRefresh = false)
    {
        lock (CacheLock)
        {
            if (!forceRefresh && _cachedDistributions != null)
            {
                return _cachedDistributions;
            }

            try
            {
                _cachedDistributions = ShellAvailabilityChecker.IsShellAvailable(ShellType.Wsl)
                    ? ExecuteWslListCommand()
                    : new List<WslDistribution>();
            }
            catch
            {
                _cachedDistributions = new List<WslDistribution>();
            }

            return _cachedDistributions;
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
        lock (CacheLock)
        {
            _cachedDistributions = null;
        }
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
                return distributions;
            }

            // Drain both pipes asynchronously (a full stderr pipe would block the
            // child) and bound the wait — a wedged WSL service must not hang the
            // detection thread indefinitely.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(WslListTimeoutMs))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Already exited or not killable; nothing more to do.
                }

                return distributions;
            }

            // Nonzero exit covers both "WSL not set up" and "no distributions
            // installed" — either way there is nothing to offer in the menus.
            if (process.ExitCode != 0)
            {
                return distributions;
            }

            string output = outputTask.GetAwaiter().GetResult();

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
        catch
        {
            // Spawn/read failure degrades to "no distributions" (no WSL menu entries).
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
