using Brutal.VulkanApi;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Top-level coordinator for the in-world (render-to-texture) terminal feature:
///     the terminal is rendered into an off-screen GPU texture and that texture is
///     drawn on a quad in 3D game space.
///     <para>
///         Built incrementally per <c>plans/GAME_SPACE_QUAD_PLAN.md</c>: the
///         off-screen target, the secondary ImGui context + Vulkan backend, the
///         per-frame render loop, the dedicated terminal session, and the
///         part-anchored world-space quad are in place. Input/focus and the launch
///         UI arrive in later phases.
///     </para>
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    // Fixed off-screen texture resolution for the prototype. Phase 7 (§5.10)
    // replaces this constant with a persisted InWorldSettings knob.
    private const int DefaultTextureSize = 1024;

    private OffscreenRenderTarget? _target;
    private OffscreenImGuiContext? _ctx;
    private OffscreenImGuiBackend? _backend;
    private PerFrameRenderer? _perFrame;
    private InWorldTerminalRenderer? _content;
    private InWorldQuad? _quad;
    private bool _initialized;
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

    /// <summary>The off-screen color/depth target the terminal renders into and the quad samples.</summary>
    public OffscreenRenderTarget? Target => _target;

    /// <summary>The world-space quad drawn by the render postfix (null until built).</summary>
    public InWorldQuad? Quad => _quad;

    /// <summary>True once all GPU resources built successfully and the per-frame loop is live.</summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    ///     Builds the GPU resources and the dedicated terminal session. Call once
    ///     after the renderer is live (StarMap <c>OnFullyLoaded</c>); it is unsafe
    ///     earlier. Any failure is logged, the partially-built resources are torn
    ///     down, and the feature is left disabled — it must never crash the game.
    /// </summary>
    /// <param name="config">The mod's loaded theme/shell configuration (shared with the 2D controller).</param>
    /// <param name="catalog">The theme catalog used to resolve the configured default theme.</param>
    public void Initialize(ThemeConfiguration config, ThemeCatalog catalog)
    {
        try
        {
            var renderer = Program.GetRenderer();
            if (renderer == null)
            {
                ModLog.Log.Error("purrTTY in-world: Program.GetRenderer() returned null; in-world terminal disabled");
                return;
            }

            // R8G8B8A8Unorm (not SRGB): UnlitMesh.frag applies gammaToLinear() to
            // the sampled texel before writing, on the assumption the texture holds
            // gamma-encoded data. With an SRGB-format target the GPU auto-decodes on
            // sample, so the shader's gammaToLinear() would run on already-linear
            // values and the in-world colors come out noticeably darker than the
            // on-screen terminal. UNORM keeps the ImGui-written bytes raw so the
            // shader's gamma decode is the single, correct one.
            _target = new OffscreenRenderTarget(
                renderer,
                "purrTTY-Offscreen",
                DefaultTextureSize,
                DefaultTextureSize,
                VkFormat.R8G8B8A8UNorm,
                renderer.DepthFormat);

            ModLog.Log.Debug(
                $"purrTTY in-world: off-screen target {DefaultTextureSize}x{DefaultTextureSize} created " +
                $"(colorView present={_target.ColorImageView.VkHandle != 0}, " +
                $"framebuffer present={_target.Framebuffer.VkHandle != 0})");

            // Secondary ImGui context (shares the main font atlas) + a second Vulkan
            // ImGui backend bound to the off-screen render pass. The backend's ctor
            // mutates the *current* context's IO + main viewport, so it must be
            // constructed with the secondary context current — hence the With(...).
            // MinImageCount/ImageCount = 2 matches MaxFramesInFlight and satisfies
            // the backend's MinImageCount >= 2 assertion; DescriptorPoolSize = 256
            // is KSA's hard floor.
            _ctx = new OffscreenImGuiContext(DefaultTextureSize, DefaultTextureSize);
            _ctx.With(() =>
            {
                _backend = new OffscreenImGuiBackend(
                    renderer,
                    _target.RenderPass,
                    minImageCount: 2,
                    imageCount: 2,
                    descriptorPoolSize: 256);
            });

            // Per-frame loop: a mod-owned command-buffer + fence ring records the
            // secondary-context UI into the off-screen target each frame.
            _perFrame = new PerFrameRenderer(renderer, _target, _ctx, _backend!, framesInFlight: 2);

            // Dedicated terminal session (its own shell) drawn into the off-screen
            // target via the shared FrameGridRenderer. Self-contained — no visible
            // 2D window is involved.
            _content = new InWorldTerminalRenderer(config, catalog);
            _perFrame.BuildUi = _content.BuildUi;

            // World-space quad sampling the off-screen texture, injected into the
            // scene pass by the RenderMainPass postfix. Part-anchored (auto-anchors
            // to the controlled vessel's first part until Phase 7 adds the picker).
            _quad = new InWorldQuad(renderer, _target);

            _initialized = true;
            // Publish to the render postfix only once everything is built: Instance
            // first, then Active (the postfix checks Active before touching Instance).
            Instance = this;
            Active = true;
            ModLog.Log.Debug("purrTTY in-world: ready (dedicated session + part-anchored quad)");
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: initialization failed ({ex.Message}); in-world terminal disabled");
            DisposeInternal();
        }
    }

    /// <summary>
    ///     Drives one off-screen terminal frame (which the world-space quad samples).
    ///     Call once per frame from the main thread (StarMap <c>OnAfterGui</c>). A
    ///     render failure disables the per-frame loop rather than spamming the log
    ///     every frame. There is no on-screen 2D window — the quad is the sole
    ///     presentation.
    /// </summary>
    public void OnAfterGui(double dt)
    {
        if (!_initialized || _perFrame == null)
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
            // frame and can corrupt Vulkan state. GPU resources are released at
            // Unload; here we just stop driving the loop.
            _initialized = false;
            ModLog.Log.Error($"purrTTY in-world: per-frame render failed, disabling ({ex.Message})");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeInternal();
    }

    private void DisposeInternal()
    {
        _initialized = false;

        // Stop the render postfix BEFORE freeing GPU resources: clear Active first
        // (the postfix early-outs on it), then drop the Instance it reaches the quad
        // through. Same thread as the postfix, so this can't interleave with an
        // in-flight RecordDraw.
        Active = false;
        Instance = null;

        // Dedicated terminal session: closes the shell + its native surface. Safe
        // here — Unload runs on the tick thread and the per-frame loop has already
        // stopped (_initialized = false above).
        _content?.Dispose();
        _content = null;

        // The quad (pipeline + descriptor set referencing the off-screen image)
        // before the target it samples. The postfix is already disabled (Active).
        _quad?.Dispose();
        _quad = null;

        // Per-frame renderer drains its fences (waiting out in-flight off-screen
        // work) then frees its command buffers + pool. Must precede the backend so
        // no in-flight command buffer references freed backend resources.
        _perFrame?.Dispose();
        _perFrame = null;

        // Reverse construction order. The ImGui backend's teardown mutates the
        // secondary context's IO, so it must run with that context current and
        // before the context itself is destroyed.
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
}
