using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.GameMod.InWorld.UI;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Coordinator for the in-world (render-to-texture) terminal feature: it owns a
///     list of <see cref="InWorldTerminalInstance"/>s (each a dedicated shell rendered
///     into an off-screen GPU texture and drawn on a world-space quad), arbitrates
///     input focus, drives every instance's per-frame render, and exposes the static
///     seams the render postfix + game-key gate read.
///     <para>
///         Today exactly one instance is created (the per-instance settings are still
///         a single <see cref="InWorldSettings"/>); the list + per-instance extraction
///         are the groundwork for N named instances. <see cref="Initialize"/> is cheap
///         (no GPU); instances are built lazily on <see cref="Enable"/> and torn down
///         on <see cref="Dispose"/>. <see cref="Disable"/> just stops drawing.
///     </para>
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    private readonly InWorldSettings _settings = InWorldSettings.LoadOrDefault();
    private ThemeConfiguration? _config;
    private ThemeCatalog? _catalog;

    private readonly List<InWorldTerminalInstance> _instances = new();
    private InWorldLaunchUI? _launchUI;
    private bool _disposed;

    /// <summary>
    ///     Read by the <c>SuperMeshRenderSystem.RenderMainPass</c> postfix to decide
    ///     whether to draw any quad. Flipped on the main thread (the same thread the
    ///     postfix runs on), so no synchronization is needed; cleared before GPU
    ///     teardown so an in-flight postfix can't dereference freed handles.
    /// </summary>
    public static bool Active;

    /// <summary>
    ///     Whether an in-world terminal currently owns keyboard input. Read by the
    ///     game-key gate (Patch01) + the hotkey guard so typing at the quad never
    ///     leaks to vehicle/camera controls. Main-thread only. (With one instance this
    ///     is a plain bool; the N-instance refactor turns it into a focused-instance id.)
    /// </summary>
    public static bool IsInputFocused;

    /// <summary>The live coordinator the render postfix reaches the instances through.</summary>
    public static InWorldTerminalManager? Instance { get; private set; }

    /// <summary>The persisted in-world settings (mutated live by the launch UI).</summary>
    public InWorldSettings Settings => _settings;

    /// <summary>True while the in-world terminal is on (drawing + rendering).</summary>
    public bool IsActive => Active;

    /// <summary>
    ///     Stores the shared config/catalog and auto-enables when persisted on (or
    ///     the dev gate is set). Cheap: no GPU work happens here. Call from
    ///     <c>OnFullyLoaded</c>.
    /// </summary>
    public void Initialize(ThemeConfiguration config, ThemeCatalog catalog)
    {
        _config = config;
        _catalog = catalog;
        _launchUI = new InWorldLaunchUI(this);

        if (_settings.Enabled || IsDevGateEnabled())
        {
            Enable();
        }
    }

    /// <summary>Menu action: toggle the in-world terminal on/off using current settings.</summary>
    public void Toggle()
    {
        if (Active)
        {
            Disable();
        }
        else
        {
            Enable();
        }
    }

    /// <summary>Menu action: open the launch / reconfigure popup.</summary>
    public void OpenConfigure() => _launchUI?.RequestOpen();

    /// <summary>Menu action: toggle in-world input focus (the focus path for billboard mode).</summary>
    public void ToggleFocus()
    {
        if (Active)
        {
            IsInputFocused = !IsInputFocused;
        }
    }

    /// <summary>
    ///     Builds the GPU resources + dedicated session + quad for the (single)
    ///     instance and starts drawing. Idempotent. A build failure is logged and the
    ///     feature stays off — it must never crash the game.
    /// </summary>
    public void Enable()
    {
        if (_disposed)
        {
            return;
        }

        if (_config == null || _catalog == null)
        {
            ModLog.Log.Error("purrTTY in-world: Enable() called before Initialize()");
            return;
        }

        if (_instances.Count == 0)
        {
            try
            {
                _instances.Add(new InWorldTerminalInstance(_config, _catalog, _settings));
            }
            catch (Exception ex)
            {
                ModLog.Log.Error($"purrTTY in-world: resource build failed ({ex.Message}); disabling");
                return; // instance ctor already tore down any partial allocation
            }
        }

        // Publish to the render postfix once everything is built: Instance first,
        // then Active (the postfix checks Active before touching Instance).
        Instance = this;
        Active = true;

        if (!_settings.Enabled)
        {
            _settings.Enabled = true;
            _settings.Save();
        }

        ModLog.Log.Debug($"purrTTY in-world: enabled ({_settings.Mode} mode)");
    }

    /// <summary>
    ///     Stops drawing + rendering. Instances (and their shells) are kept so a
    ///     re-enable is instant; everything is freed on <see cref="Dispose"/>.
    /// </summary>
    public void Disable()
    {
        // Clear Active so neither the postfix nor the per-frame loop touch the
        // resources; we deliberately do NOT free them here (freeing while the
        // current frame's already-recorded quad draw is in flight would be a
        // use-after-free). They are released on Unload.
        Active = false;
        IsInputFocused = false;

        if (_settings.Enabled)
        {
            _settings.Enabled = false;
            _settings.Save();
        }

        ModLog.Log.Debug("purrTTY in-world: disabled");
    }

    /// <summary>
    ///     Appends every live instance's quad draw to the scene-pass command buffer.
    ///     Called from the render postfix (which guards on <see cref="Active"/> and
    ///     wraps this in a try/catch that disables on failure).
    /// </summary>
    public void RecordDrawAll(CommandBuffer commandBuffer)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            _instances[i].RecordDraw(commandBuffer);
        }
    }

    /// <summary>
    ///     Drives one off-screen frame per instance (which each world-space quad
    ///     samples) while active. Call once per frame from the main thread (StarMap
    ///     <c>OnAfterGui</c>). A render failure stops the loop rather than spamming.
    /// </summary>
    public void OnAfterGui(double dt)
    {
        // Launch/config UI renders in the main context — available even when the
        // in-world terminal is off, so the player can enable/configure it.
        _launchUI?.Draw();

        if (!Active || _instances.Count == 0)
        {
            return;
        }

        // Mutually exclusive with 2D-window focus: if a 2D terminal owns the
        // keyboard this frame, the quad does not (prevents double-input).
        if (GhosttyTerminalController.IsAnyTerminalActive)
        {
            IsInputFocused = false;
        }

        // Click-to-focus runs in the main context (current here), reading the main
        // ImGui IO where GLFW delivers mouse events.
        TickPicker();

        // With one instance, "focused" is that instance while IsInputFocused holds.
        var focused = IsInputFocused ? _instances[0] : null;

        var io = ImGui.GetIO();
        if (focused != null && !io.WantTextInput && focused.Content.ActiveSession is { } session)
        {
            // Shared encoder; no suppression / blink-reset needed for the quad.
            TerminalInputEncoder.ProcessKeyboard(session, io);
        }

        // App-mouse: when focused in part mode, map the cursor's quad hit to a cell
        // and forward press/drag/wheel so in-world TUIs (vim, htop) respond to clicks.
        if (focused != null && !_settings.IsBillboard)
        {
            float2? hitUv = null;
            if (focused.TryRaycast(Cursor.InputRay, out _, out float2 uv))
            {
                hitUv = uv;
            }

            focused.Content.ProcessMouse(hitUv, io);
        }

        // Only the focused instance shows a focused cursor/selection.
        for (int i = 0; i < _instances.Count; i++)
        {
            _instances[i].Content.HasFocus = ReferenceEquals(_instances[i], focused);
        }

        try
        {
            for (int i = 0; i < _instances.Count; i++)
            {
                _instances[i].Frame(dt);
            }
        }
        catch (Exception ex)
        {
            // Disable on first failure: a render-loop throw otherwise repeats every
            // frame and can corrupt Vulkan state. Resources are released at Unload.
            Active = false;
            IsInputFocused = false;
            ModLog.Log.Error($"purrTTY in-world: per-frame render failed, disabling ({ex.Message})");
        }
    }

    /// <summary>
    ///     Click-to-focus for part mode (billboard focus is menu-driven). On a left
    ///     click it ray-tests the quad: a hit grabs input focus, a click in empty
    ///     world space releases it; clicks captured by ImGui are ignored. Escape is
    ///     deliberately NOT a release key — it must reach the shell (vim, less, …).
    ///     Folded in from the former QuadPicker; the N-instance refactor turns the
    ///     single ray-test into a nearest-hit test over all part-mode quads.
    /// </summary>
    private void TickPicker()
    {
        if (_settings.IsBillboard)
        {
            return;
        }

        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            return;
        }

        if (_instances[0].TryRaycast(Cursor.InputRay, out _, out _))
        {
            IsInputFocused = true;
        }
        else if (!ImGui.GetIO().WantCaptureMouse)
        {
            // Clicked empty world space (not over an ImGui widget) → release.
            IsInputFocused = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop the postfix BEFORE freeing GPU resources.
        Active = false;
        IsInputFocused = false;
        Instance = null;

        // Each instance drains its fences and frees its GPU graph + shell in order.
        for (int i = 0; i < _instances.Count; i++)
        {
            _instances[i].Dispose();
        }

        _instances.Clear();
    }

    // Dev convenience: auto-enable on load when PURRTTY_INWORLD is set, independent
    // of the persisted toggle. The menu (Enable/Disable) is the user-facing path.
    private static bool IsDevGateEnabled()
    {
        var v = Environment.GetEnvironmentVariable("PURRTTY_INWORLD");
        return !string.IsNullOrEmpty(v) &&
               (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
