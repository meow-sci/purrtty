using Brutal.ImGuiApi;
using purrTTY.Core.Terminal;
using purrTTY.Core.Utils;
using purrTTY.Display.Configuration;
using purrTTY.Display.Controllers;
using purrTTY.Display.Controllers.TerminalUi.Input;
using purrTTY.Display.Rendering;
using purrTTY.GameMod.InWorld;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.GameMod.InWorld.UI;
using StarMap.API;
using purrTTY.Logging;
using ModMenu;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod;

/// <summary>
///     KSA game mod for purrTTY terminal emulator.
///     Provides a terminal window that can be toggled with a configurable hotkey.
/// </summary>
[StarMapMod]
public class TerminalMod
{
    private const string ToggleHotkeyPopupId = "purrTTY Hot Key Settings##purrtty_toggle_hotkey_modal";

    private ITerminalController? _controller;
    private bool _isDisposed;
    private bool _isInitialized;
    private ITerminalEmulator? _terminal;
    private bool _terminalVisible;
    private ToggleHotkeyBinding _toggleHotkey = ToggleHotkeyBinding.Default;
    private ToggleHotkeyBinding _draftToggleHotkey = ToggleHotkeyBinding.Default;
    private ToggleHotkeyBinding? _lastCapturedToggleHotkey;
    private bool _isCapturingToggleHotkey;
    private bool _toggleHotkeyModalOpenRequested;
    private bool _suppressNextTerminalKeyboardInputFrame;
    private InWorldTerminalManager? _inWorld;
    private InWorldSettings? _inWorldSettings;
    private InWorldSettingsWindow? _inWorldSettingsWindow;

    private bool IsTerminalVisible => _controller?.IsVisible ?? _terminalVisible;

    /// <summary>
    ///     Shared toggle action used by both hotkey and game menu bar entry.
    /// </summary>
    internal static Action? Toggle;

    /// <summary>
    ///     Returns the current terminal visibility state. Used by the game menu bar to show a checkmark.
    /// </summary>
    internal static Func<bool>? GetIsVisible;

    /// <summary>
    ///     Returns the active toggle hotkey text for menu display.
    /// </summary>
    internal static Func<string>? GetToggleHotkeyShortcut;

    /// <summary>
    ///     Opens the toggle hotkey settings modal.
    /// </summary>
    internal static Action? OpenToggleHotkeySettings;

    /// <summary>
    ///     In-world (render-to-texture) feature accessors. Replaces the old
    ///     three-menu-item bundle with a single "open settings window" action.
    ///     Wired in OnFullyLoaded; nulled in DisposeResources.
    /// </summary>
    internal static Action? ToggleInWorldSettingsWindow;
    internal static Func<bool>? IsInWorldSettingsWindowOpen;
    internal static Func<bool>? IsInWorldFeatureAvailable;

    /// <summary>
    ///     Gets a value indicating whether the mod should be unloaded immediately.
    /// </summary>
    public bool ImmediateUnload => false;

    [ModMenuEntry("purrTTY")]
    public static void DrawMenu()
    {
        DrawToggleMenuItem();
        DrawInWorldSettingsMenuItem();
    }

    /// <summary>
    ///     Draws the shared menu item used by both ModMenu and the fallback injected menu.
    /// </summary>
    internal static void DrawToggleMenuItem()
    {
        var isVisible = GetIsVisible?.Invoke() ?? false;
        var hotkeyShortcut = GetToggleHotkeyShortcut?.Invoke() ?? ToggleHotkeyBinding.Default.ToShortcutString();

        if (ImGui.MenuItem("Toggle Terminal", hotkeyShortcut, isVisible))
        {
            Toggle?.Invoke();
        }

        if (ImGui.MenuItem("Toggle Hotkey"))
        {
            OpenToggleHotkeySettings?.Invoke();
        }
    }

    /// <summary>
    ///     Single menu entry that opens the unified in-world terminal settings
    ///     window. Replaces the previous trio of menu items (toggle, status,
    ///     pick-target-part). Greyed out when the in-world feature failed to
    ///     initialize (e.g. no renderer, GPU resource creation threw).
    /// </summary>
    internal static void DrawInWorldSettingsMenuItem()
    {
        bool available = IsInWorldFeatureAvailable?.Invoke() ?? false;
        bool windowOpen = IsInWorldSettingsWindowOpen?.Invoke() ?? false;

        if (!available)
        {
            ImGui.BeginDisabled();
        }
        if (ImGui.MenuItem("In-World Terminal Settings…", "", windowOpen))
        {
            ToggleInWorldSettingsWindow?.Invoke();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open the in-world terminal settings window. F11 still toggles rendering.");
        }
        if (!available)
        {
            ImGui.EndDisabled();
        }
    }


    /// <summary>
    ///     Called after the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        // ModLog.Log.Debug("purrTTY OnAfterUi");
        if (!_isInitialized || _isDisposed)
        {
            return;
        }

        try
        {
            bool hotkeyModalVisible = RenderToggleHotkeyModal();

            // Handle terminal toggle keybind (dynamic, defaults to F12)
            if (!hotkeyModalVisible && IsToggleHotkeyPressed())
            {
                bool wasVisible = IsTerminalVisible;
                ToggleTerminal();

                if (!wasVisible && IsTerminalVisible)
                {
                    // Swallow the hotkey press so printable toggle keys do not appear as terminal input when opening.
                    _suppressNextTerminalKeyboardInputFrame = true;
                }
            }

            // Update and render terminal if visible
            if (IsTerminalVisible && _controller != null)
            {
                _controller.Update((float)dt);
                _controller.Render();
            }

            // In-world (render-to-texture) terminal toggle hotkey (default F11).
            if (!hotkeyModalVisible && IsInWorldToggleHotkeyPressed())
            {
                _inWorld?.Toggle();
            }

            // The settings window must render in the MAIN ImGui context (it is
            // a normal screen-space window). Drawn before _inWorld.OnAfterGui so
            // any state edits land before this frame's secondary-context build.
            _inWorldSettingsWindow?.Render();

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
    ///     Called before the GUI is rendered.
    /// </summary>
    /// <param name="dt">Delta time.</param>
    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        // ModLog.Log.Debug("purrTTY OnBeforeUi");
        // No pre-UI logic needed currently
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
            Patcher.patch();

            // Note: GameConsoleShell is automatically discovered via CustomShellRegistry.DiscoverShells()
            // No manual registration needed - it will be found when GetAvailableShells() is called

            // Reserved for optional game-integration services independent of terminal emulation

            InitializeTerminal();

            try
            {
                _inWorldSettings = InWorldSettings.LoadOrDefault();
                _inWorld = new InWorldTerminalManager(_inWorldSettings);
                _inWorld.Initialize();

                _inWorldSettingsWindow = new InWorldSettingsWindow(_inWorld, _inWorldSettings);

                ToggleInWorldSettingsWindow = () => _inWorldSettingsWindow?.Toggle();
                IsInWorldSettingsWindowOpen = () => _inWorldSettingsWindow?.IsOpen ?? false;
                IsInWorldFeatureAvailable   = () => _inWorld != null && _inWorldSettings != null;

                // Swap the Phase 5 placeholder for a real terminal mirror. The
                // controller's heavyweight Render() has already run earlier in
                // OnAfterUi, so cursor/scroll/session state is settled by the
                // time this builder fires inside the secondary context.
                //
                // The texture-space rect + font scale are read fresh inside the
                // closure each frame so the new settings window's slider edits
                // take effect immediately without rebuilding BuildUi.
                if (_controller != null)
                {
                    var ctrl = _controller;
                    var s    = _inWorldSettings;
                    _inWorld.SetBuildUi(() =>
                    {
                        var texSize  = new float2(s.TextureWidth, s.TextureHeight);
                        var winPos   = new float2(s.RenderWindowOffsetU, s.RenderWindowOffsetV) * texSize;
                        var winSize  = new float2(s.RenderWindowSizeU,   s.RenderWindowSizeV)   * texSize;
                        ctrl.RenderContentOnly(winPos, winSize, s.RenderFontScale);
                    });
                }

                ModLog.Log.Debug("purrTTY GameMod: InWorldTerminalManager initialized (Phase 1 scaffold).");
            }
            catch (Exception inWorldEx)
            {
                // Never let a Phase 1 bug break the existing terminal.
                ModLog.Log.Debug($"purrTTY GameMod: InWorldTerminalManager init failed: {inWorldEx.Message}");
                _inWorld = null;
                _inWorldSettingsWindow = null;
            }
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY GameMod initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Called for immediate loading.
    /// </summary>
    [StarMapImmediateLoad]
    public void OnImmediatLoad()
    {
        ModLog.Log.Debug("purrTTY OnImmediatLoad");
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
    ///     Initializes the terminal emulator and related components.
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

            // Create session manager with persisted shell configuration
            var sessionManager = SessionManagerFactory.CreateWithPersistedConfiguration(
                maxSessions: 20);
            var session = sessionManager.CreateSessionAsync().Result;

            _terminal = session.Terminal;

            ModLog.Log.Debug($"purrTTY online");

            var fontConfig = TerminalFontConfig.CreateForGameMod();
            ModLog.Log.Debug(
                $"purrTTY GameMod using explicit font configuration: Regular={fontConfig.RegularFontName}, Bold={fontConfig.BoldFontName}");
            _controller = new TerminalController(sessionManager, fontConfig);

            // Option 2: Automatic detection (alternative approach - convenient for development)
            // Uncomment the following lines to use automatic detection instead:
            // ModLog.Log.Debug("purrTTY GameMod using automatic font detection");
            // _controller = new TerminalController(sessionManager);

            // Option 3: Explicit automatic detection (alternative approach - shows detection explicitly)
            // Uncomment the following lines to use explicit automatic detection:
            // var autoConfig = FontContextDetector.DetectAndCreateConfig();
            // ModLog.Log.Debug($"purrTTY GameMod using detected font configuration: Regular={autoConfig.RegularFontName}, Bold={autoConfig.BoldFontName}");
            // _controller = new TerminalController(sessionManager, autoConfig);

            // NOTE: The session manager already starts the shell process based on persisted configuration.
            // The shell (whether CustomGame, PowerShell, WSL, etc.) is started when CreateSessionAsync() is called above.
            // Do NOT start a separate shell process here - that would result in two shells running simultaneously.

            _controller.IsVisible = _terminalVisible;

            Toggle = ToggleTerminal;
            GetIsVisible = () => IsTerminalVisible;
            GetToggleHotkeyShortcut = () => _toggleHotkey.ToShortcutString();
            OpenToggleHotkeySettings = RequestOpenToggleHotkeyModal;

            // Keep terminal-side special key handling aligned with the configured global toggle hotkey.
            SpecialKeyHandler.ReservedGlobalHotkey = IsReservedTerminalHotkey;
            KeyboardInputHandler.ShouldSuppressKeyboardInputThisFrame = ConsumeKeyboardInputSuppression;

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

        bool previousVisibility = IsTerminalVisible;
        ModLog.Log.Debug(
            $"DEBUG: SetTerminalVisibility called, changing visibility from {previousVisibility} to {isVisible}");

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
    }

    private bool IsToggleHotkeyPressed()
    {
        return _toggleHotkey.MatchesPress(ImGui.GetIO());
    }

    private bool IsInWorldToggleHotkeyPressed()
    {
        if (_inWorldSettings is null)
        {
            return false;
        }

        return ImGui.IsKeyPressed(_inWorldSettings.ToggleKey);
    }

    private bool IsReservedTerminalHotkey(ImGuiKey key, KeyModifiers modifiers)
    {
        return _toggleHotkey.Key == key &&
               _toggleHotkey.Shift == modifiers.Shift &&
               _toggleHotkey.Ctrl == modifiers.Ctrl &&
               _toggleHotkey.Alt == modifiers.Alt &&
               _toggleHotkey.Super == modifiers.Meta;
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
        _draftToggleHotkey = binding;

        try
        {
            var config = ThemeConfiguration.Load();
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
    ///     Gets a loaded font by name, or null if not found.
    /// </summary>
    public static ImFontPtr? GetFont(string fontName)
    {
        return PurrTTYFontManager.LoadedFonts.TryGetValue(fontName, out ImFontPtr font) ? font : null;
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
            // Dispose the in-world manager first so it can release any frame-loop hooks
            // before the terminal/controller is torn down.
            _inWorld?.Dispose();
            _inWorld = null;
            _inWorldSettings = null;
            _inWorldSettingsWindow = null;

            // Dispose components (the session manager handles process cleanup)
            _controller?.Dispose();
            _controller = null;

            _terminal?.Dispose();
            _terminal = null;

            Toggle = null;
            GetIsVisible = null;
            GetToggleHotkeyShortcut = null;
            OpenToggleHotkeySettings = null;
            ToggleInWorldSettingsWindow = null;
            IsInWorldSettingsWindowOpen = null;
            IsInWorldFeatureAvailable = null;
            SpecialKeyHandler.ReservedGlobalHotkey = null;
            KeyboardInputHandler.ShouldSuppressKeyboardInputThisFrame = null;

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
