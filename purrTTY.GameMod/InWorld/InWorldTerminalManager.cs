using System;
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

            _initialized = true;
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: failed to create off-screen resources; disabling in-world terminal: {ex}");
            _settings.Enabled = false;
            // Tear down in reverse construction order. The backend's Dispose
            // touches ImGui state on the secondary context, so it must run
            // under _ctx.With(...) — but only if _ctx is still alive (backend
            // construction itself can throw before _backend is assigned, in
            // which case there is nothing to dispose).
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
        // phase 5 populates this
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
        // Tear down in reverse construction order. The backend's Dispose
        // mutates ImGui state on the secondary context, so it must run with
        // that context current. Then the context itself, then the GPU target.
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
