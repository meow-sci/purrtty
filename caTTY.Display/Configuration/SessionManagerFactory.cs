using System.Reflection;
using caTTY.Core.Rpc;
using caTTY.Core.Rpc.Socket;
using caTTY.Core.Terminal;

namespace caTTY.Display.Configuration;

/// <summary>
/// Factory for creating SessionManager instances with proper configuration loading.
/// Ensures persisted shell settings are applied during initialization.
/// </summary>
public static class SessionManagerFactory
{
    /// <summary>
    /// Creates a SessionManager with persisted shell configuration loaded.
    /// This ensures that the user's saved shell preferences are used from startup.
    /// </summary>
    /// <param name="maxSessions">Maximum number of concurrent sessions (default: 20)</param>
    /// <param name="rpcHandler">Optional RPC handler for CSI RPC commands (null disables CSI RPC)</param>
    /// <param name="oscRpcHandler">Optional OSC RPC handler for OSC-based RPC commands (null disables OSC RPC)</param>
    /// <returns>A SessionManager configured with persisted shell settings</returns>
    public static SessionManager CreateWithPersistedConfiguration(
        int maxSessions = 20,
        IRpcHandler? rpcHandler = null,
        IOscRpcHandler? oscRpcHandler = null)
    {
        // Load persisted configuration to determine initial shell type
        var themeConfig = ThemeConfiguration.Load();
        Console.WriteLine($"SessionManagerFactory: Configured shell type: {themeConfig.DefaultShellType}");
        if (themeConfig.DefaultShellType == ShellType.CustomGame)
        {
            Console.WriteLine($"SessionManagerFactory: Configured custom game shell ID: {themeConfig.DefaultCustomGameShellId}");
        }

        // Ensure custom shell registry discovers available shells BEFORE creating sessions
        // This is critical for CustomGame shell types to be found
        if (themeConfig.DefaultShellType == ShellType.CustomGame)
        {
            try
            {
                // Step 1: Ensure caTTY.CustomShells assembly is loaded into memory
                // This is necessary because the assembly might not be loaded yet when discovery runs
                EnsureCustomShellsAssemblyIsLoaded();

                // Step 2: Discover shells (will now find GameConsoleShell)
                CustomShellRegistry.Instance.DiscoverShells();
                var discoveredShells = CustomShellRegistry.Instance.GetAvailableShells();
                Console.WriteLine($"SessionManagerFactory: Custom shell discovery completed. Available shells: {string.Join(", ", discoveredShells.Select(s => s.Id))}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SessionManagerFactory: Warning - Failed to discover custom shells: {ex.Message}");
                // Continue anyway - will fail more clearly when trying to create the session
            }
        }

        // Create launch options from persisted configuration
        var defaultLaunchOptions = themeConfig.CreateLaunchOptions();
        Console.WriteLine($"SessionManagerFactory: Created launch options with shell type: {defaultLaunchOptions.ShellType}");

        // Set default terminal dimensions and working directory
        defaultLaunchOptions.InitialWidth = 80;
        defaultLaunchOptions.InitialHeight = 24;
        defaultLaunchOptions.WorkingDirectory = Environment.CurrentDirectory;

        // Create session manager with persisted shell configuration and RPC handlers
        return new SessionManager(maxSessions, defaultLaunchOptions, rpcHandler, oscRpcHandler);
    }

    /// <summary>
    /// Ensures the caTTY.CustomShells assembly is loaded into the current AppDomain.
    /// This is necessary because the CustomShellRegistry discovery process scans AppDomain.CurrentDomain.GetAssemblies(),
    /// which only includes assemblies that have been loaded into memory. If caTTY.CustomShells hasn't been referenced
    /// yet by the executing code, it won't be in the assembly list.
    /// </summary>
    private static void EnsureCustomShellsAssemblyIsLoaded()
    {
        try
        {
            // Check if the assembly is already loaded
            var alreadyLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "caTTY.CustomShells");

            if (alreadyLoaded != null)
            {
                Console.WriteLine("SessionManagerFactory: caTTY.CustomShells assembly already loaded");
                return;
            }

            // Assembly is not loaded, so load it explicitly
            // This forces the assembly into the AppDomain so CustomShellRegistry can discover it
            Console.WriteLine("SessionManagerFactory: Loading caTTY.CustomShells assembly explicitly");
            Assembly.Load("caTTY.CustomShells");
            Console.WriteLine("SessionManagerFactory: Successfully loaded caTTY.CustomShells assembly");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SessionManagerFactory: Failed to load caTTY.CustomShells assembly: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Creates a SessionManager with default configuration (PowerShell on Windows).
    /// This is primarily for testing scenarios where persisted configuration should be ignored.
    /// </summary>
    /// <param name="maxSessions">Maximum number of concurrent sessions (default: 20)</param>
    /// <param name="rpcHandler">Optional RPC handler for CSI RPC commands (null disables CSI RPC)</param>
    /// <param name="oscRpcHandler">Optional OSC RPC handler for OSC-based RPC commands (null disables OSC RPC)</param>
    /// <returns>A SessionManager with default shell configuration</returns>
    public static SessionManager CreateWithDefaultConfiguration(
        int maxSessions = 20,
        IRpcHandler? rpcHandler = null,
        IOscRpcHandler? oscRpcHandler = null)
    {
        return new SessionManager(maxSessions, null, rpcHandler, oscRpcHandler);
    }
}