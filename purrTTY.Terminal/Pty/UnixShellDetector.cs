namespace purrTTY.Core.Terminal;

/// <summary>
/// Detects installed shells on Unix hosts (Linux/macOS) — the Unix counterpart of
/// <see cref="WslDistributionDetector"/>. Sources are <c>/etc/shells</c> plus the
/// user's <c>$SHELL</c>; entries are deduplicated by executable name (distros list
/// the same shell under both /bin and /usr/bin) and the $SHELL entry is marked as
/// the default. Results are cached for the process lifetime (installed shells do
/// not change mid-game).
/// </summary>
public static class UnixShellDetector
{
    /// <summary>
    /// Represents a shell installed on the system.
    /// </summary>
    public class UnixShell
    {
        /// <summary>
        /// Gets or sets the absolute path to the shell executable.
        /// </summary>
        public required string Path { get; set; }

        /// <summary>
        /// Gets or sets whether this is the user's default shell ($SHELL).
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Gets or sets the formatted display name for UI presentation.
        /// </summary>
        public required string DisplayName { get; set; }
    }

    private static List<UnixShell>? _cachedShells;
    private static readonly object CacheLock = new();

    /// <summary>
    /// Gets the list of installed shells, default first then alphabetical.
    /// Returns an empty list on Windows or when detection fails.
    /// </summary>
    /// <param name="forceRefresh">If true, ignores the cache and re-detects shells</param>
    public static List<UnixShell> GetInstalledShells(bool forceRefresh = false)
    {
        if (OperatingSystem.IsWindows())
        {
            return new List<UnixShell>();
        }

        lock (CacheLock)
        {
            if (!forceRefresh && _cachedShells != null)
            {
                return _cachedShells;
            }

            try
            {
                _cachedShells = DetectShells();
            }
            catch
            {
                // Detection failure (unreadable /etc/shells etc.) degrades to the
                // generic "Default Shell" menu entry; no logging dependency here so
                // the detector stays usable outside the game process (tests).
                _cachedShells = new List<UnixShell>();
            }

            return _cachedShells;
        }
    }

    /// <summary>
    /// Clears the cached shell list, forcing a refresh on the next call.
    /// </summary>
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            _cachedShells = null;
        }
    }

    private static List<UnixShell> DetectShells()
    {
        string? defaultShell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(defaultShell) && !File.Exists(defaultShell))
        {
            defaultShell = null;
        }

        var candidates = new List<string>();
        if (defaultShell != null)
        {
            candidates.Add(defaultShell);
        }

        const string etcShells = "/etc/shells";
        if (File.Exists(etcShells))
        {
            foreach (string rawLine in File.ReadAllLines(etcShells))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] != '/')
                {
                    continue;
                }

                if (File.Exists(line))
                {
                    candidates.Add(line);
                }
            }
        }

        // Dedupe by executable name; the $SHELL entry (added first) wins its name,
        // so e.g. Ubuntu's /bin/bash + /usr/bin/bash collapse to one menu item.
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in candidates)
        {
            string name = System.IO.Path.GetFileName(path);
            if (name.Length > 0)
            {
                byName.TryAdd(name, path);
            }
        }

        var shells = new List<UnixShell>(byName.Count);
        foreach ((string name, string path) in byName)
        {
            bool isDefault = defaultShell != null && path == defaultShell;
            shells.Add(new UnixShell
            {
                Path = path,
                IsDefault = isDefault,
                DisplayName = isDefault ? $"{name} (Default)" : name,
            });
        }

        shells.Sort(static (a, b) => a.IsDefault != b.IsDefault
            ? (a.IsDefault ? -1 : 1)
            : string.CompareOrdinal(a.DisplayName, b.DisplayName));

        return shells;
    }
}
