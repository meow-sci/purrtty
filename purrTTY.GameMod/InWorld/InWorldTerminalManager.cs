using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Top-level coordinator for the in-world (render-to-texture) terminal feature:
///     the terminal is rendered into an off-screen GPU texture and that texture is
///     drawn on a quad in 3D game space.
///     <para>
///         Built incrementally per <c>plans/GAME_SPACE_QUAD_PLAN.md</c>: the
///         off-screen target, the secondary ImGui context + Vulkan backend, and the
///         per-frame render loop (with a placeholder UI) are in place. The dedicated
///         terminal session, the quad pipeline, and input/focus arrive in later
///         phases, so this class grows phase by phase.
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
    private bool _initialized;
    private bool _disposed;

    // Temporary Phase 3 verification: surface the off-screen texture in a 2D ImGui
    // window so the render-to-texture path can be confirmed before the world-space
    // quad exists. Removed in Phase 6 once the quad shows the texture in 3D.
    private ImTextureRef _debugTexRef;
    private bool _debugTexAdded;

    /// <summary>The off-screen color/depth target the terminal renders into and the quad samples.</summary>
    public OffscreenRenderTarget? Target => _target;

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

            _initialized = true;
            ModLog.Log.Debug("purrTTY in-world: per-frame off-screen renderer ready (dedicated session)");
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: initialization failed ({ex.Message}); in-world terminal disabled");
            DisposeInternal();
        }
    }

    /// <summary>
    ///     Drives one off-screen frame and (temporarily, in Phase 3) draws a 2D
    ///     debug window showing the resulting texture. Call once per frame from the
    ///     main thread (StarMap <c>OnAfterGui</c>). A render failure disables the
    ///     per-frame loop rather than spamming the log every frame.
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
            return;
        }

        DrawDebugWindow();
    }

    // Temporary Phase 3 verification (removed in Phase 6): show the off-screen color
    // texture inside a normal 2D ImGui window in the MAIN context, to confirm it has
    // real content before the world-space quad exists.
    private void DrawDebugWindow()
    {
        if (_target == null) return;

        if (!_debugTexAdded)
        {
            // Register the off-screen image with the MAIN backend so ImGui.Image can
            // sample it. Done lazily on the first frame (after content exists).
            _debugTexRef = ImGuiBackend.Vulkan.AddTexture(_target.Sampler, _target.ColorImageView);
            _debugTexAdded = true;
        }

        ImGui.SetNextWindowSize(new float2(548f, 600f), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("purrTTY in-world (debug)"))
        {
            ImGui.Text("Off-screen terminal texture:");
            ImGui.Image(_debugTexRef, new float2(512f, 512f));
        }
        ImGui.End();
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

        // Remove the temporary debug texture from the MAIN backend first — it
        // references the off-screen image view we are about to destroy.
        if (_debugTexAdded)
        {
            try { ImGuiBackend.Vulkan.RemoveTexture(_debugTexRef); } catch { /* best-effort */ }
            _debugTexAdded = false;
            _debugTexRef = default;
        }

        // Dedicated terminal session: closes the shell + its native surface. Safe
        // here — Unload runs on the tick thread and the per-frame loop has already
        // stopped (_initialized = false above).
        _content?.Dispose();
        _content = null;

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
