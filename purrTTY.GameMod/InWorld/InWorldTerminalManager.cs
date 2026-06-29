using Brutal.VulkanApi;
using KSA;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Top-level coordinator for the in-world (render-to-texture) terminal feature:
///     the terminal is rendered into an off-screen GPU texture and that texture is
///     drawn on a quad in 3D game space.
///     <para>
///         Phase 1 owns only the <see cref="OffscreenRenderTarget"/>. Later phases
///         add the secondary ImGui context + backend, the per-frame render loop, the
///         dedicated terminal session, the quad pipeline, and input/focus (see
///         <c>plans/GAME_SPACE_QUAD_PLAN.md</c>). The implementation is built and
///         verified incrementally, so this class grows phase by phase.
///     </para>
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    // Fixed off-screen texture resolution for the prototype. Phase 7 (§5.10)
    // replaces this constant with a persisted InWorldSettings knob.
    private const int DefaultTextureSize = 1024;

    private OffscreenRenderTarget? _target;
    private bool _disposed;

    /// <summary>The off-screen color/depth target the terminal renders into and the quad samples.</summary>
    public OffscreenRenderTarget? Target => _target;

    /// <summary>
    ///     Builds the GPU resources. Call once after the renderer is live
    ///     (StarMap <c>OnFullyLoaded</c>); it is unsafe earlier. Any failure is
    ///     logged, the partially-built resources are torn down, and the feature
    ///     is left disabled — it must never crash the game.
    /// </summary>
    public void Initialize()
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
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: initialization failed ({ex.Message}); in-world terminal disabled");
            DisposeInternal();
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
        _target?.Dispose();
        _target = null;
    }
}
