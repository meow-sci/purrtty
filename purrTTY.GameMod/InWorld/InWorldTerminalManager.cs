using System;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Top-level coordinator for the in-world (render-to-texture) terminal feature.
///     Phase 2: owns an <see cref="OffscreenRenderTarget"/> (render pass + framebuffer
///     + sampler) but still does not render anything into it.
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    private readonly InWorldSettings _settings;
    private OffscreenRenderTarget? _target;
    private OffscreenContext? _ctx;
    private OffscreenImGuiBackend? _backend;
    private PerFrameRenderer? _frame;
    private bool _initialized;
    private bool _disposed;

    public InWorldTerminalManager(InWorldSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    ///     Called once when the game is fully loaded and the renderer is live.
    /// </summary>
    public void Initialize()
    {
        try
        {
            var renderer = Program.GetRenderer();
            if (renderer == null)
            {
                ModLog.Log.Error("purrTTY in-world: Program.GetRenderer() returned null; disabling in-world terminal");
                _settings.Enabled = false;
                return;
            }

            _target = new OffscreenRenderTarget(
                renderer,
                "purrTTY-Offscreen",
                _settings.TextureWidth,
                _settings.TextureHeight,
                VkFormat.R8G8B8A8SRGB,
                renderer.DepthFormat);

            // Phase 3: secondary ImGui context shares the main font atlas so we
            // do not duplicate font upload memory. Constructed after the GPU
            // target so disposal can tear them down in reverse order.
            _ctx = new OffscreenContext(_settings.TextureWidth, _settings.TextureHeight);

            // Phase 4: secondary Vulkan ImGui backend bound to our off-screen
            // render pass. The backend's ctor mutates the *current* ImGui
            // context's IO + main viewport, so we must construct it under
            // _ctx.With(...). minImageCount/imageCount = 2 matches typical
            // MaxFramesInFlight and satisfies the backend's MinImageCount >= 2
            // assertion; descriptorPoolSize = 64 is plenty for a single window.
            _ctx.With(() =>
            {
                _backend = new OffscreenImGuiBackend(
                    renderer,
                    _target.RenderPass,
                    minImageCount: 2,
                    imageCount: 2,
                    descriptorPoolSize: 64);
            });

            // Phase 5: per-frame command buffer + fence ring. framesInFlight: 2
            // matches the backend's image count from Phase 4. The default
            // BuildUi is a no-op; install the Phase 5 sanity stub. Phase 6+
            // will swap in the real terminal UI.
            _frame = new PerFrameRenderer(renderer, _target, _ctx, _backend!, framesInFlight: 2);
            _frame.BuildUi = static () =>
            {
                ImGui.Begin("In-World Terminal");
                ImGui.Text("Hello, in-world terminal (Phase 5)");
                ImGui.End();
            };

            _initialized = true;
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: failed to create off-screen resources; disabling in-world terminal: {ex}");
            _settings.Enabled = false;
            // Tear down in reverse construction order. The frame ring owns
            // pure-Vulkan resources (no ImGui state) and disposes safely
            // outside the secondary context. The backend's Dispose touches
            // ImGui state on the secondary context, so it must run under
            // _ctx.With(...) — but only if _ctx is still alive (any earlier
            // step in construction can throw before later fields are
            // assigned, in which case there is nothing to dispose for them).
            try { _frame?.Dispose(); } catch { /* best-effort */ }
            _frame = null;
            if (_ctx != null && _backend != null)
            {
                try { _ctx.With(() => { _backend!.Dispose(); }); } catch { /* best-effort */ }
            }
            _backend = null;
            _ctx?.Dispose();
            _ctx = null;
            _target?.Dispose();
            _target = null;
        }
    }

    /// <summary>
    ///     Called every frame from <c>[StarMapAfterGui]</c>.
    ///     <paramref name="dt"/> is the same dt the game passes to its own AfterGui callbacks.
    /// </summary>
    public void OnAfterGui(double dt)
    {
        if (!_settings.Enabled || !_initialized || _disposed)
        {
            return;
        }
        if (_frame == null)
        {
            return;
        }
        try
        {
            _frame.Frame(dt);
        }
        catch (Exception ex)
        {
            // A render-loop exception is fatal for this feature: keep retrying
            // would just spam the log and may corrupt Vulkan state. Disable so
            // the user can re-toggle after addressing the underlying issue.
            ModLog.Log.Error($"purrTTY in-world: per-frame render failed; disabling in-world terminal: {ex}");
            _settings.Enabled = false;
        }
    }

    /// <summary>
    ///     Toggles the master enable flag and logs the new state.
    /// </summary>
    public void Toggle()
    {
        _settings.Enabled = !_settings.Enabled;
        ModLog.Log.Debug($"purrTTY in-world terminal {(_settings.Enabled ? "enabled" : "disabled")}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // Tear down in reverse construction order:
        //   _frame  → drains its fences then frees its cmd buffers + pool
        //             (pure Vulkan; no ImGui state, so no With() needed).
        //   _backend→ mutates ImGui state on the secondary context, so MUST
        //             run with that context current.
        //   _ctx    → destroys the secondary ImGui context.
        //   _target → destroys the off-screen render pass + framebuffer.
        try { _frame?.Dispose(); } catch { /* best-effort */ }
        _frame = null;
        if (_ctx != null && _backend != null)
        {
            _ctx.With(() => { _backend!.Dispose(); });
        }
        _backend = null;
        _ctx?.Dispose();
        _ctx = null;
        _target?.Dispose();
        _target = null;
    }
}
