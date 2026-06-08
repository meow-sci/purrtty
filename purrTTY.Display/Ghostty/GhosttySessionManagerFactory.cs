using System.Reflection;
using Microsoft.Extensions.Logging;
using purrTTY.Display.Configuration;
using purrTTY.Logging;
using CoreTerminal = purrTTY.Core.Terminal;
using PurrTTY.Terminal.Sessions;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Builds the new libghostty-vt-backed <see cref="SessionManager"/> from the
/// same persisted theme/shell configuration the legacy factory used. Mirrors
/// <c>SessionManagerFactory.CreateWithPersistedConfiguration</c> but targets the
/// new backend's session manager.
/// </summary>
public static class GhosttySessionManagerFactory
{
    public static SessionManager CreateWithPersistedConfiguration(int maxSessions = 20, ILogger? logger = null)
    {
        var themeConfig = ThemeConfiguration.Load();
        ModLog.Log.Debug($"GhosttySessionManagerFactory: Configured shell type: {themeConfig.DefaultShellType}");

        if (themeConfig.DefaultShellType == CoreTerminal.ShellType.CustomGame)
        {
            try
            {
                EnsureCustomShellsAssemblyIsLoaded();
                CoreTerminal.CustomShellRegistry.Instance.DiscoverShells();
                var shells = CoreTerminal.CustomShellRegistry.Instance.GetAvailableShells();
                ModLog.Log.Debug(
                    $"GhosttySessionManagerFactory: discovered shells: {string.Join(", ", shells.Select(s => s.Id))}");
            }
            catch (Exception ex)
            {
                ModLog.Log.Debug($"GhosttySessionManagerFactory: shell discovery failed: {ex.Message}");
            }
        }

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
