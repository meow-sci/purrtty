using System.Reflection;
using Brutal.ImGuiApi;
using caTTY.TestGameMod.Display;
using StarMap.API;

namespace TestCaTTY.GameMod;

/// <summary>
///     KSA game mod for caTTY terminal emulator.
///     Provides a terminal window that can be toggled with F12 key.
/// </summary>
[StarMapMod]
public class TerminalMod
{
    // Font loading
    private static readonly Dictionary<string, ImFontPtr> _loadedFonts = new();
    private static bool _fontsLoaded;
    private readonly bool _isDisposed = false;

    private readonly TestModFonts controller = new();
    private bool _isInitialized;

    private bool _showUi;

    /// <summary>
    ///     Gets a value indicating whether the mod should be unloaded immediately.
    /// </summary>
    public bool ImmediateUnload => false;

    /// <summary>
    ///     Called after the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        // Console.WriteLine("TestCaTTY OnAfterUi");
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        try
        {
            // Handle terminal toggle keybind (F12)
            if (ImGui.IsKeyPressed(ImGuiKey.F12))
            {
                _showUi = !_showUi;
            }

            if (_showUi)
            {
                controller.Render();

                // PushHackFont(out bool fontUsed, 40.0f);
                // ImGui.SetNextWindowSize(new float2(400, 400), ImGuiCond.FirstUseEver);
                // ImGui.Begin("TestCaTTY Win");
                // ImGui.Text("Test text");
                // ImGui.End();
                // MaybePopFont(fontUsed);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TestCaTTY GameMod OnAfterUi error: {ex.Message}");
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
        // Console.WriteLine("TestCaTTY OnBeforeUi");
        // No pre-UI logic needed currently
    }

    /// <summary>
    ///     Called when all mods are loaded.
    /// </summary>
    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        Console.WriteLine("TestCaTTY OnFullyLoaded");
        try
        {
            InitializeTerminal();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TestCaTTY GameMod initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Called for immediate loading.
    /// </summary>
    [StarMapImmediateLoad]
    public void OnImmediatLoad()
    {
        Console.WriteLine("TestCaTTY OnImmediatLoad");
        // No immediate load logic needed
    }

    /// <summary>
    ///     Called when the mod is unloaded.
    /// </summary>
    [StarMapUnload]
    public void Unload()
    {
        Console.WriteLine("TestCaTTY Unload");
        try
        {
            DisposeResources();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TestCaTTY GameMod unload error: {ex.Message}");
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

        Console.WriteLine("TestCaTTY GameMod: Initializing terminal...");

        try
        {
            // Load fonts first
            LoadFonts();

            _isInitialized = true;
            Console.WriteLine("TestCaTTY GameMod: Terminal initialized successfully. Press F12 to toggle.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TestCaTTY GameMod: Terminal initialization failed: {ex.Message}");
            DisposeResources();
            throw;
        }
    }


    /// <summary>
    ///     Loads fonts explicitly for the game mod.
    ///     Based on BRUTAL ImGui font loading pattern for game mods.
    /// </summary>
    private static void LoadFonts()
    {
        if (_fontsLoaded)
        {
            return;
        }

        try
        {
            // Get the directory where the mod DLL is located
            string? dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (!string.IsNullOrEmpty(dllDir))
            {
                string fontsDir = Path.Combine(dllDir, "TerminalFonts");
                Console.WriteLine($"TestCaTTY GameMod: Loading fonts from directory: {fontsDir}");

                if (Directory.Exists(fontsDir))
                {
                    // Get all .ttf and .otf files from Fonts folder
                    string[] fontFiles = Directory.GetFiles(fontsDir, "*.iamttf");

                    if (fontFiles.Length > 0)
                    {
                        ImGuiIOPtr io = ImGui.GetIO();
                        ImFontAtlasPtr atlas = io.Fonts;

                        for (int i = 0; i < fontFiles.Length; i++)
                        {
                            string fontPath = fontFiles[i];
                            string fontName = Path.GetFileNameWithoutExtension(fontPath);

                            Console.WriteLine($"TestCaTTY GameMod: Loading font: {fontPath}");


                            if (File.Exists(fontPath))
                            {
                                // Use a reasonable default font size (14pt)
                                float fontSize = 32.0f;
                                var fontPathStr = new ImString(fontPath);
                                ImFontPtr font = atlas.AddFontFromFileTTF(fontPathStr, fontSize);
                                _loadedFonts[fontName] = font;

                                Console.WriteLine($"TestCaTTY GameMod: Loaded font '{fontName}' from {fontPath}");
                            }
                        }

                        Console.WriteLine(
                            $"TestCaTTY GameMod: Loaded {_loadedFonts.Count} fonts - {string.Join(", ", _loadedFonts.Keys)}");
                    }
                    else
                    {
                        Console.WriteLine("TestCaTTY GameMod: No font files found in Fonts folder");
                    }
                }
                else
                {
                    Console.WriteLine($"TestCaTTY GameMod: Fonts directory not found at: {fontsDir}");
                }
            }

            _fontsLoaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"TestCaTTY GameMod: Error loading fonts: {ex.Message}");
        }
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

        Console.WriteLine("TestCaTTY GameMod: Disposing resources...");
    }
}
