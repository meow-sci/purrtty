using Brutal.VulkanApi;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.GameMod.InWorld.UI;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Top-level coordinator for the in-world (render-to-texture) terminal feature:
///     a dedicated shell is rendered into an off-screen GPU texture and that texture
///     is drawn on a world-space quad (part-anchored or camera-billboard).
///     <para>
///         <see cref="Initialize"/> is cheap (no GPU); the GPU resources + session +
///         quad are built lazily on <see cref="Enable"/> and torn down on
///         <see cref="Dispose"/>. <see cref="Disable"/> just stops drawing (keeping
///         resources for an instant re-enable). The render postfix reads the static
///         <see cref="Active"/>/<see cref="Instance"/>.
///     </para>
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    private readonly InWorldSettings _settings = InWorldSettings.LoadOrDefault();
    private ThemeConfiguration? _config;
    private ThemeCatalog? _catalog;

    private OffscreenRenderTarget? _target;
    private OffscreenImGuiContext? _ctx;
    private OffscreenImGuiBackend? _backend;
    private PerFrameRenderer? _perFrame;
    private InWorldTerminalRenderer? _content;
    private InWorldQuad? _quad;
    private InWorldLaunchUI? _launchUI;
    private bool _disposed;

    /// <summary>
    ///     Read by the <c>SuperMeshRenderSystem.RenderMainPass</c> postfix to decide
    ///     whether to draw the quad. Flipped on the main thread (the same thread the
    ///     postfix runs on), so no synchronization is needed; cleared before GPU
    ///     teardown so an in-flight postfix can't dereference freed handles.
    /// </summary>
    public static bool Active;

    /// <summary>The live manager instance the render postfix reaches the quad through.</summary>
    public static InWorldTerminalManager? Instance { get; private set; }

    /// <summary>The world-space quad drawn by the render postfix (null until built).</summary>
    public InWorldQuad? Quad => _quad;

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

    /// <summary>
    ///     Builds the GPU resources + dedicated session + quad (once) and starts
    ///     drawing. Idempotent. A build failure is logged, partial resources are torn
    ///     down, and the feature stays off — it must never crash the game.
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

        if (_target == null && !BuildResources(_config, _catalog))
        {
            return; // build failed (already logged + torn down)
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
    ///     Stops drawing + rendering. Resources (and the shell) are kept so a
    ///     re-enable is instant; everything is freed on <see cref="Dispose"/>.
    /// </summary>
    public void Disable()
    {
        // Clear Active so neither the postfix nor the per-frame loop touch the
        // resources; we deliberately do NOT free them here (freeing while the
        // current frame's already-recorded quad draw is in flight would be a
        // use-after-free). They are released on Unload.
        Active = false;

        if (_settings.Enabled)
        {
            _settings.Enabled = false;
            _settings.Save();
        }

        ModLog.Log.Debug("purrTTY in-world: disabled");
    }

    /// <summary>
    ///     Drives one off-screen terminal frame (which the world-space quad samples)
    ///     while active. Call once per frame from the main thread (StarMap
    ///     <c>OnAfterGui</c>). A render failure stops the loop rather than spamming.
    /// </summary>
    public void OnAfterGui(double dt)
    {
        // Launch/config UI renders in the main context — available even when the
        // in-world terminal is off, so the player can enable/configure it.
        _launchUI?.Draw();

        if (!Active || _perFrame == null)
        {
            return;
        }

        try
        {
            _perFrame.Frame(dt);
        }
        catch (Exception ex)
        {
            // Disable on first failure: a render-loop throw otherwise repeats every
            // frame and can corrupt Vulkan state. Resources are released at Unload.
            Active = false;
            ModLog.Log.Error($"purrTTY in-world: per-frame render failed, disabling ({ex.Message})");
        }
    }

    private bool BuildResources(ThemeConfiguration config, ThemeCatalog catalog)
    {
        try
        {
            var renderer = Program.GetRenderer();
            if (renderer == null)
            {
                ModLog.Log.Error("purrTTY in-world: Program.GetRenderer() returned null; cannot enable");
                return false;
            }

            int width = Math.Clamp(_settings.TextureWidth, 256, 4096);
            int height = Math.Clamp(_settings.TextureHeight, 256, 4096);

            // R8G8B8A8Unorm (not SRGB): UnlitMesh.frag applies gammaToLinear() to the
            // sampled texel, expecting gamma-encoded bytes. An SRGB target would
            // double-decode and render the in-world terminal noticeably dark.
            _target = new OffscreenRenderTarget(
                renderer, "purrTTY-Offscreen", width, height, VkFormat.R8G8B8A8UNorm, renderer.DepthFormat);

            // Secondary ImGui context (shares the main font atlas) + a second Vulkan
            // ImGui backend bound to the off-screen pass. The backend ctor mutates
            // the current context's IO, so build it under With(...).
            _ctx = new OffscreenImGuiContext(width, height);
            _ctx.With(() =>
            {
                _backend = new OffscreenImGuiBackend(renderer, _target.RenderPass,
                    minImageCount: 2, imageCount: 2, descriptorPoolSize: 256);
            });

            _perFrame = new PerFrameRenderer(renderer, _target, _ctx, _backend!, framesInFlight: 2);

            // Dedicated terminal session (its own shell) drawn into the off-screen
            // target via the shared FrameGridRenderer. Self-contained.
            _content = new InWorldTerminalRenderer(config, catalog);
            _perFrame.BuildUi = _content.BuildUi;

            // World-space quad sampling the texture; reads InWorldSettings live (so
            // launch-UI edits update it instantly).
            _quad = new InWorldQuad(renderer, _target, _settings);

            ModLog.Log.Debug($"purrTTY in-world: resources built ({width}x{height})");
            return true;
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: resource build failed ({ex.Message}); disabling");
            TeardownResources();
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop the postfix BEFORE freeing GPU resources.
        Active = false;
        Instance = null;

        TeardownResources();
    }

    private void TeardownResources()
    {
        // Dedicated terminal session: closes the shell + its native surface. Safe on
        // the tick thread (Unload). The per-frame loop has already stopped.
        _content?.Dispose();
        _content = null;

        // The quad (pipeline + descriptor set referencing the off-screen image)
        // before the target it samples.
        _quad?.Dispose();
        _quad = null;

        // Per-frame renderer drains its fences (waiting out in-flight off-screen
        // work) then frees its command buffers + pool. Must precede the backend so
        // no in-flight command buffer references freed backend resources.
        _perFrame?.Dispose();
        _perFrame = null;

        // The ImGui backend's teardown mutates the secondary context's IO, so it
        // must run with that context current and before the context is destroyed.
        if (_backend != null)
        {
            var backend = _backend;
            _backend = null;
            if (_ctx != null)
            {
                _ctx.With(backend.Dispose);
            }
            else
            {
                backend.Dispose();
            }
        }

        _ctx?.Dispose();
        _ctx = null;

        _target?.Dispose();
        _target = null;
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
