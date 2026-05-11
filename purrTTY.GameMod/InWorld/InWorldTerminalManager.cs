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

            _initialized = true;
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: failed to create off-screen resources; disabling in-world terminal: {ex}");
            _settings.Enabled = false;
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
        // Tear down ImGui state first, then GPU resources.
        _ctx?.Dispose();
        _ctx = null;
        _target?.Dispose();
        _target = null;
    }
}
