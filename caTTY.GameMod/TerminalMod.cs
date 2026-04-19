using Brutal.ImGuiApi;
using caTTY.Core.Rpc;
using caTTY.Core.Rpc.Socket;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers;
using caTTY.Display.Rendering;
using caTTY.TermSequenceRpc;
using caTTY.TermSequenceRpc.SocketRpc;
using Microsoft.Extensions.Logging.Abstractions;
using ModMenu;
using StarMap.API;

namespace caTTY.GameMod;

/// <summary>
///     KSA game mod for caTTY terminal emulator.
///     Provides a terminal window that can be toggled with F12 key.
/// </summary>
[StarMapMod]
public class TerminalMod
{
    private ITerminalController? _controller;
    private bool _isDisposed;
    private bool _isInitialized;
    private ITerminalEmulator? _terminal;
    private static bool _terminalVisible = false;
    private ISocketRpcServer? _socketRpcServer;


    /// <summary>
    ///     Gets a value indicating whether the mod should be unloaded immediately.
    /// </summary>
    public bool ImmediateUnload => false;

    /// <summary>
    ///     ModMenu entry to toggle the terminal window.
    /// </summary>
    [ModMenuEntry("caTTY Terminal")]
    public static void DrawSubMenuEntry()
    {
        if (ImGui.MenuItem("caTTY Terminal"))
        {
            _terminalVisible = !_terminalVisible;
            Console.WriteLine($"caTTY Terminal Window {(_terminalVisible ? "shown" : "hidden")}");
        }
    }

    /// <summary>
    ///     Called after the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        // Console.WriteLine("caTTY OnAfterUi");
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        try
        {
            // Handle terminal toggle keybind (F12)
            if (ImGui.IsKeyPressed(ImGuiKey.F12))
            {
                _terminalVisible = !_terminalVisible;
                Console.WriteLine($"caTTY GameMod: Terminal {(_terminalVisible ? "shown" : "hidden")}");
            }

            // Sync controller visibility with global state (handles both ModMenu and F12 toggles)
            if (_controller != null && _controller.IsVisible != _terminalVisible)
            {
                _controller.IsVisible = _terminalVisible;
            }

            // Update and render terminal if visible
            if (_terminalVisible && _controller != null)
            {
                _controller.Update((float)dt);
                _controller.Render();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"caTTY GameMod OnAfterUi error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            // Don't let exceptions crash the game
        }
    }

    /// <summary>
    ///     Called before the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        // Console.WriteLine("caTTY OnBeforeUi");
        // No pre-UI logic needed currently
    }

    /// <summary>
    ///     Called when all mods are loaded.
    /// </summary>
    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        Console.WriteLine("caTTY OnFullyLoaded");
        try
        {
            Patcher.patch();

            // Note: GameConsoleShell is automatically discovered via CustomShellRegistry.DiscoverShells()
            // No manual registration needed - it will be found when GetAvailableShells() is called

            // Socket RPC server is a singleton game mod feature, independent of terminal emulation

            InitializeTerminal();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"caTTY GameMod initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Called for immediate loading.
    /// </summary>
    [StarMapImmediateLoad]
    public void OnImmediatLoad()
    {
        Console.WriteLine("caTTY OnImmediatLoad");
        // No immediate load logic needed
    }

    /// <summary>
    ///     Called when the mod is unloaded.
    /// </summary>
    [StarMapUnload]
    public void Unload()
    {
        Console.WriteLine("caTTY Unload");
        try
        {
            Patcher.unload();

            DisposeResources();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"caTTY GameMod unload error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the production configuration directory path for persistent storage.
    /// This ensures theme configuration is saved to the user's Documents folder.
    /// Must be called BEFORE any ThemeConfiguration access.
    /// </summary>
    private void SetProductionConfigPath()
    {
        try
        {
            var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var productionConfigRoot = Path.Combine(myDocuments, "My Games", "Kitten Space Agency");

            ThemeConfiguration.OverrideConfigDirectory = productionConfigRoot;

            Console.WriteLine($"caTTY GameMod: Production config path set to: {productionConfigRoot}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"caTTY GameMod: WARNING - Failed to set production config path: {ex.Message}");
            Console.WriteLine("Configuration will use temporary directory and settings will not persist.");
        }
    }

    /// <summary>
    ///     Initializes the terminal emulator and related components.
    ///     Guards against double initialization.
    /// </summary>
    private void InitializeTerminal()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        Console.WriteLine("caTTY GameMod: Initializing terminal...");

        try
        {
            SetProductionConfigPath();

            // Load fonts first
            CaTTYFontManager.LoadFonts();

            var _outputBuffer = new List<byte[]>();
            var (rpcHandler, oscRpcHandler, socketRpcHandler) = RpcBootstrapper.CreateAllKsaRpcHandlers(
                NullLogger.Instance,
                bytes => _outputBuffer.Add(bytes));

            // Start singleton socket RPC server at mod level (BEFORE creating sessions)
            try
            {
                _socketRpcServer = SocketRpcServerFactory.Create(socketRpcHandler, NullLogger.Instance);
                _socketRpcServer.StartAsync().Wait();

                // Register endpoint with factory for ProcessManager to query
                SocketRpcServerFactory.RegisterEndpoint(_socketRpcServer.Endpoint);

                Console.WriteLine($"[caTTY GameMod] Socket RPC server started at {_socketRpcServer.Endpoint}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[caTTY GameMod] Failed to start socket RPC server: {ex.Message}");
                _socketRpcServer = null;
                SocketRpcServerFactory.ClearEndpoint();
            }

            // Create session manager with persisted shell configuration and RPC handlers (CSI, OSC)
            var sessionManager = SessionManagerFactory.CreateWithPersistedConfiguration(
                maxSessions: 20,
                rpcHandler: rpcHandler,
                oscRpcHandler: oscRpcHandler);
            var session = sessionManager.CreateSessionAsync().Result;

            _terminal = session.Terminal;

            Console.WriteLine($"TerminalMod: rpc enabled={_terminal.IsRpcEnabled}");

            var fontConfig = TerminalFontConfig.CreateForGameMod();
            Console.WriteLine(
                $"caTTY GameMod using explicit font configuration: Regular={fontConfig.RegularFontName}, Bold={fontConfig.BoldFontName}");
            _controller = new TerminalController(sessionManager, fontConfig);

            // Option 2: Automatic detection (alternative approach - convenient for development)
            // Uncomment the following lines to use automatic detection instead:
            // Console.WriteLine("caTTY GameMod using automatic font detection");
            // _controller = new TerminalController(sessionManager);

            // Option 3: Explicit automatic detection (alternative approach - shows detection explicitly)
            // Uncomment the following lines to use explicit automatic detection:
            // var autoConfig = FontContextDetector.DetectAndCreateConfig();
            // Console.WriteLine($"caTTY GameMod using detected font configuration: Regular={autoConfig.RegularFontName}, Bold={autoConfig.BoldFontName}");
            // _controller = new TerminalController(sessionManager, autoConfig);

            // NOTE: The session manager already starts the shell process based on persisted configuration.
            // The shell (whether CustomGame, PowerShell, WSL, etc.) is started when CreateSessionAsync() is called above.
            // Do NOT start a separate shell process here - that would result in two shells running simultaneously.

            _isInitialized = true;
            Console.WriteLine("caTTY GameMod: Terminal initialized successfully. Press F12 to toggle.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"caTTY GameMod: Terminal initialization failed: {ex.Message}");
            DisposeResources();
            throw;
        }
    }

    /// <summary>
    ///     Toggles the terminal window visibility.
    /// </summary>
    private void ToggleTerminal()
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        _terminalVisible = !_terminalVisible;

        if (_controller != null)
        {
            _controller.IsVisible = _terminalVisible;
        }

        Console.WriteLine($"caTTY GameMod: Terminal {(_terminalVisible ? "shown" : "hidden")}");
    }

    /// <summary>
    ///     Gets a loaded font by name, or null if not found.
    /// </summary>
    public static ImFontPtr? GetFont(string fontName)
    {
        return CaTTYFontManager.LoadedFonts.TryGetValue(fontName, out ImFontPtr font) ? font : null;
    }

    /// <summary>
    ///     Disposes all resources and cleans up.
    ///     Guards against double disposal.
    /// </summary>
    private void DisposeResources()
    {
        if (_isDisposed)
        {
            return;
        }

        Console.WriteLine("caTTY GameMod: Disposing resources...");

        try
        {
            // Dispose components (the session manager handles process cleanup)
            _controller?.Dispose();
            _controller = null;

            _terminal?.Dispose();
            _terminal = null;

            // Stop and dispose singleton socket RPC server
            if (_socketRpcServer != null)
            {
                try
                {
                    _socketRpcServer.StopAsync().Wait(1000);
                    _socketRpcServer.Dispose();
                    _socketRpcServer = null;

                    // Clear endpoint registration
                    SocketRpcServerFactory.ClearEndpoint();

                    Console.WriteLine("[caTTY GameMod] Socket RPC server stopped");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[caTTY GameMod] Error stopping socket RPC server: {ex.Message}");
                }
            }

            _isInitialized = false;
            _isDisposed = true;

            Console.WriteLine("caTTY GameMod: Resources disposed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"caTTY GameMod: Error during resource disposal: {ex.Message}");
        }
    }
}
