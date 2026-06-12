using ShellCommandResolver = purrTTY.Core.Terminal.Process.ShellCommandResolver;

namespace purrTTY.Core.Terminal;

/// <summary>
/// Utility class for checking shell availability on the current system.
/// Availability is defined as resolvability: a shell is available exactly when
/// <see cref="Process.ShellCommandResolver"/> can resolve it — the same PATH
/// scan, well-known install paths, and <c>.exe</c> fallback the actual launch
/// will use, so the menus can never offer a shell the launch would reject.
/// </summary>
public static class ShellAvailabilityChecker
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ShellType, bool> ShellAvailabilityCache = new();

    /// <summary>
    /// Checks if a specific shell type is available on the current system.
    /// Results are cached per shell type for the process lifetime (shell installs
    /// do not change mid-game), so only the first call per type pays for the PATH
    /// scan — which can block for seconds when PATH contains a dead network share.
    /// </summary>
    /// <param name="shellType">The shell type to check</param>
    /// <returns>True if the shell is available, false otherwise</returns>
    public static bool IsShellAvailable(ShellType shellType)
    {
        return ShellAvailabilityCache.GetOrAdd(shellType, static type =>
        {
            switch (type)
            {
                case ShellType.Auto: // falls back across shells — always offerable
                case ShellType.Custom: // checked when the path is provided
                case ShellType.CustomGame: // Game Console is always available
                    return true;

                default:
                    try
                    {
                        _ = ShellCommandResolver.ResolveShellCommandArgv(
                            new ProcessLaunchOptions { ShellType = type });
                        return true;
                    }
                    catch
                    {
                        // Resolution failure (including unsupported types) means
                        // the shell cannot be launched.
                        return false;
                    }
            }
        });
    }
}
