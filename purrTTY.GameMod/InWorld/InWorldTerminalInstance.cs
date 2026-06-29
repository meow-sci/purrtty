using Brutal.VulkanApi;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Settings;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     One in-world terminal: its own off-screen GPU target + secondary ImGui
///     context/backend + per-frame render loop + dedicated shell session
///     (<see cref="InWorldTerminalRenderer"/>) + world-space quad. Extracted from
///     <see cref="InWorldTerminalManager"/> so the coordinator can hold N of them
///     (today exactly one is created).
///     <para>
///         The constructor allocates GPU resources, so the renderer must be live
///         (built from a menu Enable / <c>OnFullyLoaded</c>). On any build failure it
///         tears down whatever was partially allocated and rethrows; the coordinator
///         catches and logs so a failed build never crashes the game. Main thread only.
///     </para>
/// </summary>
public sealed class InWorldTerminalInstance : IDisposable
{
    private readonly InWorldSettings _settings;

    private OffscreenRenderTarget? _target;
    private OffscreenImGuiContext? _ctx;
    private OffscreenImGuiBackend? _backend;
    private PerFrameRenderer? _perFrame;
    private InWorldTerminalRenderer? _content;
    private InWorldQuad? _quad;
    private bool _disposed;

    /// <summary>The dedicated terminal content renderer (its own shell session + fonts).</summary>
    public InWorldTerminalRenderer Content => _content!;

    /// <summary>True when this instance is a camera billboard (no ego-space raycast / click-to-focus).</summary>
    public bool IsBillboard => _settings.IsBillboard;

    public InWorldTerminalInstance(ThemeConfiguration config, ThemeCatalog catalog, InWorldSettings settings)
    {
        _settings = settings;

        try
        {
            var renderer = Program.GetRenderer()
                ?? throw new InvalidOperationException("Program.GetRenderer() returned null");

            int width = Math.Clamp(settings.TextureWidth, 256, 4096);
            int height = Math.Clamp(settings.TextureHeight, 256, 4096);

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
        }
        catch
        {
            Teardown();
            throw;
        }
    }

    /// <summary>Drives one off-screen terminal frame (which the world-space quad samples).</summary>
    public void Frame(double dt) => _perFrame!.Frame(dt);

    /// <summary>Appends this instance's quad draw to the scene-pass command buffer.</summary>
    public void RecordDraw(CommandBuffer commandBuffer) => _quad!.RecordDraw(commandBuffer);

    /// <summary>Ray-tests the quad in part mode; see <see cref="InWorldQuad.TryRaycast"/>.</summary>
    public bool TryRaycast(Ray ray, out double t, out float2 uv) => _quad!.TryRaycast(ray, out t, out uv);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Teardown();
    }

    private void Teardown()
    {
        // Dedicated terminal session: closes the shell + its native surface. Safe on
        // the tick thread; the per-frame loop has already stopped.
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
}
