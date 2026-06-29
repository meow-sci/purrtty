using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Owns a sampleable off-screen render target for the in-world terminal:
///     a <see cref="VkRenderPass"/>, a KSA <see cref="RenderTarget"/> (color + depth
///     attachments + framebuffer), and a linear-clamped <see cref="VkSampler"/>.
///     <para>
///         Phase 1: the asset exists in GPU memory but nothing renders into it yet.
///         The render pass is built via <see cref="RenderTarget.CreateRenderPass"/>,
///         which finalizes the color attachment to <c>ShaderReadOnlyOptimal</c>, so a
///         later pass can sample the texture with no manual layout transition.
///     </para>
/// </summary>
public sealed class OffscreenRenderTarget : IDisposable
{
    private readonly Renderer _renderer;
    private bool _disposed;

    public string       Name           { get; }
    public VkExtent2D   Extent         { get; private set; }
    public VkFormat     ColorFormat    { get; }
    public VkFormat     DepthFormat    { get; }
    public VkRenderPass RenderPass     { get; private set; }
    public RenderTarget Target         { get; private set; } = null!;
    public VkSampler    Sampler        { get; private set; }

    // Framebuffer.Attachments is protected, but RenderTarget exposes the color slot
    // through the public ColorImage property (a FramebufferAttachment value).
    public VkImage       ColorImage     => Target.ColorImage.Image;
    public VkImageView   ColorImageView => Target.ColorImage.ImageView;
    public VkFramebuffer Framebuffer    => Target.FrameBuffer;

    public OffscreenRenderTarget(Renderer renderer, string name, int width, int height,
                                 VkFormat colorFormat, VkFormat depthFormat)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name cannot be null or empty.", nameof(name));

        _renderer   = renderer;
        Name        = name;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;

        Resize(width, height);
    }

    /// <summary>
    ///     Idempotent: dispose existing GPU resources (if any) and recreate them
    ///     at the requested size. Must run on the main thread (the only thread
    ///     that owns the Vulkan device).
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Invalid OffscreenRenderTarget size: {width}x{height}");
        }

        DisposeGpu();

        Extent = new VkExtent2D { Width = width, Height = height };

        Target = new RenderTarget(_renderer, Name, Extent,
                                  ColorFormat, DepthFormat,
                                  depthSlices: 1, inMipLevels: 1);

        // Use KSA's built-in render pass: it finalizes the color attachment to
        // ShaderReadOnlyOptimal, so we can sample from the texture in a later pass
        // without a manual layout transition.
        RenderPass = Target.CreateRenderPass();

        Target.BuildFramebuffer(RenderPass);

        var samplerCi = Presets.Sampler.SamplerLinearClamped;
        Sampler = _renderer.Device.CreateSampler(in samplerCi, null);
    }

    private void DisposeGpu()
    {
        if (Sampler.VkHandle != 0)
        {
            _renderer.Device.DestroySampler(Sampler, null);
            Sampler = default;
        }

        if (Target != null)
        {
            // Disposes the framebuffer + all attachment images & views.
            Target.Dispose();
            Target = null!;
        }

        if (RenderPass.VkHandle != 0)
        {
            _renderer.Device.DestroyRenderPass(RenderPass, null);
            RenderPass = default;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeGpu();
    }
}
