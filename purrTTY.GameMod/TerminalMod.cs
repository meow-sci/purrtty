using Brutal.ImGuiApi;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using StarMap.API;
using purrTTY.Logging;
using purrTTY.GameMod.InWorld;
using purrTTY.GameMod.UI;
using ModMenu;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace purrTTY.GameMod;

/// <summary>
///     KSA game mod for purrTTY terminal emulator.
///     Provides multi-window, multi-tab terminal sessions toggled with a
///     configurable hotkey and driven from the top-level game menus
///     (menu content lives in <see cref="TerminalMenus"/>; this class owns
///     lifecycle, the toggle hotkey, and the settings modals).
/// </summary>
[StarMapMod]
public class TerminalMod
{
    private const string ToggleHotkeyPopupId = "purrTTY Hot Key Settings##purrtty_toggle_hotkey_modal";

    private GhosttyTerminalController? _controller;
    private InWorldTerminalManager? _inWorld;
    private bool _isDisposed;
    private bool _isInitialized;
    private bool _terminalVisible;
    private ToggleHotkeyBinding _toggleHotkey = ToggleHotkeyBinding.Default;

    // Rendered shortcut text for the menu, rebuilt only when the binding
    // changes (the menu reads it every frame; ToShortcutString allocates).
    private string _toggleHotkeyShortcut = ToggleHotkeyBinding.Default.ToShortcutString();
    private ToggleHotkeyBinding _draftToggleHotkey = ToggleHotkeyBinding.Default;
    private ToggleHotkeyBinding? _lastCapturedToggleHotkey;
    private bool _isCapturingToggleHotkey;
    private bool _toggleHotkeyModalOpenRequested;
    private bool _suppressNextTerminalKeyboardInputFrame;

    private ThemeDialog? _themeDialog;

    private bool IsTerminalVisible => _controller?.IsVisible ?? _terminalVisible;

    /// <summary>
    ///     Gets a value indicating whether the mod should be unloaded immediately.
    /// </summary>
    public bool ImmediateUnload => false;

    [ModMenuEntry("purrTTY")]
    public static void DrawMenu()
    {
        TerminalMenus.DrawMenuContent();
    }

    /// <summary>
    ///     Called after the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        try
        {
            bool modalVisible = RenderToggleHotkeyModal();
            modalVisible |= _themeDialog?.Render() ?? false;

            // Handle terminal toggle keybind (dynamic, defaults to F12). Suppressed
            // while the in-world terminal is focused so the key reaches that shell.
            if (!modalVisible && !InWorldTerminalManager.IsInputFocused && IsToggleHotkeyPressed())
            {
                bool wasVisible = IsTerminalVisible;
                ToggleTerminal();

                if (!wasVisible && IsTerminalVisible)
                {
                    // Swallow the hotkey press so printable toggle keys do not appear as terminal input when opening.
                    _suppressNextTerminalKeyboardInputFrame = true;
                }
            }

            // Update runs even while hidden: it drains hidden sessions' PTY
            // backlogs on a low cadence (their inboxes otherwise grow unbounded
            // under chatty output). Render() is also called unconditionally and
            // early-outs when hidden — that early-out clears the game-key gate
            // (_anyTerminalActive). Calling it only while visible would strand
            // the flag `true` after hiding a focused terminal, killing the
            // game's entire keyboard pipeline until the terminal is shown again.
            if (_controller != null)
            {
                _controller.Update((float)dt);
                _controller.Render();
            }

            // In-world render-to-texture loop (prototype; gated by PURRTTY_INWORLD).
            // No-op until the renderer/quad subsystem has been initialized.
            _inWorld?.OnAfterGui(dt);
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod OnAfterUi error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            // Don't let exceptions crash the game
        }
    }

    /// <summary>
    ///     Called when all mods are loaded.
    /// </summary>
    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        ModLog.Log.Debug("purrTTY OnFullyLoaded");
        try
        {
            // patch() applies each Harmony patch independently and never throws —
            // an optional patch (menu fallback, console capture) failing must not
            // block terminal init, and a required one failing is logged loudly but
            // still lets the terminal come up.
            Patcher.patch();

            InitializeTerminal();

            // In-world render-to-texture quad (plans/GAME_SPACE_QUAD_PLAN.md).
            // Built here because the renderer is only live from OnFullyLoaded on.
            TryInitializeInWorld();
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY GameMod initialization failed: {ex.Message}");
        }
    }

    private void TryInitializeInWorld()
    {
        // The in-world terminal is menu-driven (Enable/Disable); the manager
        // auto-enables when persisted on, or when the PURRTTY_INWORLD dev gate is
        // set. Needs the controller's shared config + catalog (loading a second
        // config is the gotcha-6 config-revert trap).
        if (_controller == null)
        {
            return;
        }

        try
        {
            _inWorld = new InWorldTerminalManager();
            _inWorld.Initialize(_controller.Configuration, _controller.Catalog);

            TerminalMenus.ToggleInWorld = () => _inWorld?.Toggle();
            TerminalMenus.OpenInWorldConfigure = () => _inWorld?.OpenConfigure();
            TerminalMenus.IsInWorldActive = () => _inWorld?.IsActive ?? false;
            TerminalMenus.FocusInWorld = () => _inWorld?.ToggleFocus();
            TerminalMenus.IsInWorldFocused = () => InWorldTerminalManager.IsInputFocused;
        }
        catch (Exception ex)
        {
            // Never let an experimental subsystem take the terminal down.
            ModLog.Log.Error($"purrTTY GameMod: in-world terminal init failed: {ex.Message}");
            _inWorld?.Dispose();
            _inWorld = null;
        }
    }

    /// <summary>
    ///     Called for immediate loading.
    /// </summary>
    [StarMapImmediateLoad]
    public void OnImmediateLoad()
    {
        ModLog.Log.Debug("purrTTY OnImmediateLoad");
        // No immediate load logic needed
    }

    /// <summary>
    ///     Called when the mod is unloaded.
    /// </summary>
    [StarMapUnload]
    public void Unload()
    {
        ModLog.Log.Debug("purrTTY Unload");
        try
        {
            Patcher.unload();

            DisposeResources();
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod unload error: {ex.Message}");
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

            ModLog.Log.Debug($"purrTTY GameMod: Production config path set to: {productionConfigRoot}");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: WARNING - Failed to set production config path: {ex.Message}");
            ModLog.Log.Debug("Configuration will use temporary directory and settings will not persist.");
        }
    }

    /// <summary>
    ///     Initializes the terminal controller and related components.
    ///     Guards against double initialization.
    /// </summary>
    private void InitializeTerminal()
    {
        if (_isInitialized || _isDisposed)
        {
            return;
        }

        ModLog.Log.Debug("purrTTY GameMod: Initializing terminal...");

        try
        {
            SetProductionConfigPath();
            LoadToggleHotkeyFromConfiguration();

            // Load fonts first
            PurrTTYFontManager.LoadFonts();

            // Game-console shells are launchable from the menus at any time.
            GhosttySessionManagerFactory.EnsureGameShellsDiscovered();

            // All shell detection (PATH probes, `wsl --list`, /etc/shells) runs
            // once here on a background thread; the menus only read the snapshot.
            ShellMenuCache.BeginDetection();

            var themeConfig = ThemeConfiguration.Load();
            var catalog = new ThemeCatalog();

            var controller = new GhosttyTerminalController(themeConfig, catalog);
            controller.KeyboardSuppression = ConsumeKeyboardInputSuppression;
            _controller = controller;

            // Do NOT pre-open a window/session here: a shell must only ever be
            // launched in response to an explicit player action. Toggling the
            // terminal visible with no open windows opens a default-shell window
            // on demand (GhosttyTerminalController.IsVisible), and the menu
            // "New Window"/custom-shell paths open their own window with their
            // requested shell — pre-opening here stranded an unwanted default
            // (WSL2/Auto) shell alongside whatever the player actually launched.
            controller.IsVisible = _terminalVisible;

            ModLog.Log.Debug("purrTTY online");

            TerminalMenus.Toggle = ToggleTerminal;
            TerminalMenus.GetIsVisible = () => IsTerminalVisible;
            TerminalMenus.GetToggleHotkeyShortcut = () => _toggleHotkeyShortcut;
            TerminalMenus.OpenToggleHotkeySettings = RequestOpenToggleHotkeyModal;
            _themeDialog = new ThemeDialog(controller);
            TerminalMenus.OpenThemeDialog = _themeDialog.RequestOpen;
            TerminalMenus.MenuController = controller;

            _isInitialized = true;
            ModLog.Log.Debug($"purrTTY GameMod: Terminal initialized successfully. Press {_toggleHotkey.ToDisplayString()} to toggle.");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: Terminal initialization failed: {ex.Message}");
            DisposeResources();
            throw;
        }
    }

    /// <summary>
    ///     Toggles the terminal window visibility.
    /// </summary>
    private void ToggleTerminal()
    {
        SetTerminalVisibility(!IsTerminalVisible);
    }

    /// <summary>
    ///     Synchronizes terminal visibility across the game mod and the controller.
    /// </summary>
    /// <param name="isVisible">The desired visibility state.</param>
    private void SetTerminalVisibility(bool isVisible)
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        _terminalVisible = isVisible;
        if (_controller != null)
        {
            _controller.IsVisible = isVisible;
        }

        ModLog.Log.Debug($"purrTTY GameMod: Terminal {(IsTerminalVisible ? "shown" : "hidden")}");
    }

    private void LoadToggleHotkeyFromConfiguration()
    {
        try
        {
            var config = ThemeConfiguration.Load();
            _toggleHotkey = ToggleHotkeyBinding.FromConfiguration(config);
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
        }
        catch (Exception ex)
        {
            _toggleHotkey = ToggleHotkeyBinding.Default;
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
            ModLog.Log.Debug($"purrTTY GameMod: Failed to load toggle hotkey from config, defaulting to F12. Error: {ex.Message}");
        }

        _toggleHotkeyShortcut = _toggleHotkey.ToShortcutString();
    }

    private bool IsToggleHotkeyPressed()
    {
        return _toggleHotkey.MatchesPress(ImGui.GetIO());
    }

    private void RequestOpenToggleHotkeyModal()
    {
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        _draftToggleHotkey = _toggleHotkey;
        _lastCapturedToggleHotkey = _toggleHotkey;
        _isCapturingToggleHotkey = false;
        _toggleHotkeyModalOpenRequested = true;
    }

    private bool RenderToggleHotkeyModal()
    {
        if (_toggleHotkeyModalOpenRequested)
        {
            _toggleHotkeyModalOpenRequested = false;
            ImGui.OpenPopup(ToggleHotkeyPopupId);
        }

        bool open = true;
        ImGui.SetNextWindowSize(new float2(760f, 0f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(ToggleHotkeyPopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return false;
        }

        if (_isCapturingToggleHotkey)
        {
            UpdateToggleHotkeyCapture();
            RenderCaptureHotkeyModalContent();
        }
        else
        {
            RenderStandardHotkeyModalContent();
        }

        RenderHotkeyModalSaveCancelButtons();

        if (!open)
        {
            _isCapturingToggleHotkey = false;
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return true;
    }

    private void RenderStandardHotkeyModalContent()
    {
        RenderHotkeySummaryTable("Current Hot Key", GetCurrentHotkeyDisplay());

        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;

        if (ImGui.Button(" Capture Now ##purrtty_capture_hotkey", new float2(buttonWidth, 0f)))
        {
            _isCapturingToggleHotkey = true;
            _lastCapturedToggleHotkey = null;
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Reset ##purrtty_reset_hotkey", new float2(buttonWidth, 0f)))
        {
            _draftToggleHotkey = ToggleHotkeyBinding.Default;
            _lastCapturedToggleHotkey = ToggleHotkeyBinding.Default;
        }
    }

    private void RenderCaptureHotkeyModalContent()
    {
        ImGui.Text("Press desired hotkey now");
        RenderHotkeySummaryTable("Hot Key", GetCaptureHotkeyDisplay());

        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;

        bool hasCapturedHotkey = _lastCapturedToggleHotkey.HasValue;

        if (!hasCapturedHotkey)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Use This ##purrtty_use_captured_hotkey", new float2(buttonWidth, 0f)) &&
            _lastCapturedToggleHotkey is ToggleHotkeyBinding capturedHotkey)
        {
            _draftToggleHotkey = capturedHotkey;
            ApplyToggleHotkeySetting(_draftToggleHotkey);
            _isCapturingToggleHotkey = false;
        }

        if (!hasCapturedHotkey)
        {
            ImGui.EndDisabled();
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Cancel ##purrtty_cancel_capture_hotkey", new float2(buttonWidth, 0f)))
        {
            _isCapturingToggleHotkey = false;
        }
    }

    private void RenderHotkeyModalSaveCancelButtons()
    {
        ImGui.Spacing();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (availW - gap) / 2f;

        if (_isCapturingToggleHotkey)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Save ##purrtty_save_hotkey", new float2(buttonWidth, 0f)))
        {
            ApplyToggleHotkeySetting(_draftToggleHotkey);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Cancel ##purrtty_cancel_hotkey", new float2(buttonWidth, 0f)))
        {
            _draftToggleHotkey = _toggleHotkey;
            _lastCapturedToggleHotkey = _toggleHotkey;
            _isCapturingToggleHotkey = false;
            ImGui.CloseCurrentPopup();
        }

        if (_isCapturingToggleHotkey)
        {
            ImGui.EndDisabled();
        }
    }

    private void RenderHotkeySummaryTable(string label, string value)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##purrtty_toggle_hotkey_summary", 2,
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##hotkey_label", ImGuiTableColumnFlags.WidthFixed, 260f);
            ImGui.TableSetupColumn("##hotkey_value", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text(value);

            ImGui.EndTable();
        }

        ImGui.PopStyleVar();
    }

    private string GetCurrentHotkeyDisplay()
    {
        return _draftToggleHotkey.IsDefault
            ? $"{_draftToggleHotkey.ToDisplayString()} (default)"
            : _draftToggleHotkey.ToDisplayString();
    }

    private string GetCaptureHotkeyDisplay()
    {
        if (_lastCapturedToggleHotkey is ToggleHotkeyBinding capturedHotkey)
        {
            return capturedHotkey.ToDisplayString();
        }

        var io = ImGui.GetIO();
        var modifierParts = new List<string>(4);

        if (io.KeyCtrl)
        {
            modifierParts.Add("Ctrl");
        }

        if (io.KeyShift)
        {
            modifierParts.Add("Shift");
        }

        if (io.KeyAlt)
        {
            modifierParts.Add("Alt");
        }

        if (ImGuiHotkeyHelpers.GetSuperModifier(io))
        {
            modifierParts.Add("Super");
        }

        return modifierParts.Count == 0
            ? "Waiting for key press..."
            : $"{string.Join(" + ", modifierParts)} + ...";
    }

    private void UpdateToggleHotkeyCapture()
    {
        var io = ImGui.GetIO();
        bool super = ImGuiHotkeyHelpers.GetSuperModifier(io);

        foreach (ImGuiKey key in ToggleHotkeyBinding.CapturableKeys)
        {
            if (!ImGui.IsKeyPressed(key))
            {
                continue;
            }

            _lastCapturedToggleHotkey = new ToggleHotkeyBinding(
                key,
                io.KeyShift,
                io.KeyCtrl,
                io.KeyAlt,
                super);
            break;
        }
    }

    private void ApplyToggleHotkeySetting(ToggleHotkeyBinding binding)
    {
        _toggleHotkey = binding;
        _toggleHotkeyShortcut = binding.ToShortcutString();
        _draftToggleHotkey = binding;

        try
        {
            // Use the controller's live configuration instance so a later display
            // settings save does not clobber the new hotkey with stale values.
            var config = _controller?.Configuration ?? ThemeConfiguration.Load();
            _toggleHotkey.WriteToConfiguration(config);
            config.Save();
            ModLog.Log.Debug($"purrTTY GameMod: Toggle hotkey updated to {_toggleHotkey.ToDisplayString()}");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: Failed to persist toggle hotkey: {ex.Message}");
        }
    }

    private bool ConsumeKeyboardInputSuppression()
    {
        if (!_suppressNextTerminalKeyboardInputFrame)
        {
            return false;
        }

        _suppressNextTerminalKeyboardInputFrame = false;
        return true;
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

        ModLog.Log.Debug("purrTTY GameMod: Disposing resources...");

        try
        {
            // Dispose in reverse construction order: the in-world subsystem (built
            // after the controller in OnFullyLoaded) owns GPU resources and goes first.
            _inWorld?.Dispose();
            _inWorld = null;

            // Dispose components (the controller disposes its windows, whose session
            // managers handle process cleanup)
            _controller?.Dispose();
            _controller = null;

            TerminalMenus.Toggle = null;
            TerminalMenus.GetIsVisible = null;
            TerminalMenus.GetToggleHotkeyShortcut = null;
            TerminalMenus.OpenToggleHotkeySettings = null;
            TerminalMenus.OpenThemeDialog = null;
            TerminalMenus.MenuController = null;
            _themeDialog = null;
            TerminalMenus.ToggleInWorld = null;
            TerminalMenus.OpenInWorldConfigure = null;
            TerminalMenus.IsInWorldActive = null;
            TerminalMenus.FocusInWorld = null;
            TerminalMenus.IsInWorldFocused = null;

            _isInitialized = false;
            _isDisposed = true;

            ModLog.Log.Debug("purrTTY GameMod: Resources disposed successfully");
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod: Error during resource disposal: {ex.Message}");
        }
    }
}
