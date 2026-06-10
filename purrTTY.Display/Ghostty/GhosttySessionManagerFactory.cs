using System.Reflection;
using Microsoft.Extensions.Logging;
using purrTTY.Display.Configuration;
using purrTTY.Logging;
using CoreTerminal = purrTTY.Core.Terminal;
using PurrTTY.Terminal.Sessions;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Builds libghostty-vt-backed <see cref="SessionManager"/>s from the persisted
/// theme/shell configuration. Each terminal window owns one session manager
/// (its tabs); the default launch options come from the configured default shell.
/// </summary>
public static class GhosttySessionManagerFactory
{
    private static bool _shellsDiscovered;

    public static SessionManager CreateWithPersistedConfiguration(int maxSessions = 20, ILogger? logger = null)
        => CreateSessionManager(ThemeConfiguration.Load(), maxSessions, logger);

    /// <summary>Creates a session manager from an already-loaded configuration.</summary>
    public static SessionManager CreateSessionManager(
        ThemeConfiguration themeConfig,
        int maxSessions = 20,
        ILogger? logger = null)
    {
        ModLog.Log.Debug($"GhosttySessionManagerFactory: Configured shell type: {themeConfig.DefaultShellType}");

        // Game-console sessions can be launched from the menus at any time, so
        // discovery is unconditional (it is cheap and runs once).
        EnsureGameShellsDiscovered();

        var defaultLaunchOptions = themeConfig.CreateLaunchOptions();

        if (themeConfig.TryGetTerminalGridDimensions(out int columns, out int rows))
        {
            defaultLaunchOptions.InitialWidth = columns;
            defaultLaunchOptions.InitialHeight = rows;
        }
        else
        {
            defaultLaunchOptions.InitialWidth = 80;
            defaultLaunchOptions.InitialHeight = 24;
        }

        defaultLaunchOptions.WorkingDirectory = Environment.CurrentDirectory;

        return new SessionManager(maxSessions, defaultLaunchOptions, logger);
    }

    /// <summary>
    /// Forces the custom-shells assembly to load and runs shell discovery once,
    /// so <c>GameConsoleShell</c> is available to launch from the game menus.
    /// </summary>
    public static void EnsureGameShellsDiscovered()
    {
        if (_shellsDiscovered)
        {
            return;
        }

        try
        {
            EnsureCustomShellsAssemblyIsLoaded();
            CoreTerminal.CustomShellRegistry.Instance.DiscoverShells();
            var shells = CoreTerminal.CustomShellRegistry.Instance.GetAvailableShells();
            ModLog.Log.Debug(
                $"GhosttySessionManagerFactory: discovered shells: {string.Join(", ", shells.Select(s => s.Id))}");
            _shellsDiscovered = true;
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"GhosttySessionManagerFactory: shell discovery failed: {ex.Message}");
        }
    }

    private static void EnsureCustomShellsAssemblyIsLoaded()
    {
        var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "purrTTY.CustomShells");
        if (alreadyLoaded != null)
        {
            return;
        }

        Assembly.Load("purrTTY.CustomShells");
    }
}
