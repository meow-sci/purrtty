using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Owns a sampleable off-screen render target for the in-world terminal:
///     a colour + depth attachment pair, a <see cref="VkRenderPass"/>, a
///     <see cref="VkFramebuffer"/>, and a linear-clamped <see cref="VkSampler"/>.
///     <para>
///         The render pass finalizes the colour attachment to
///         <c>ShaderReadOnlyOptimal</c>, so a later pass (the in-world quad) can
///         sample the texture with no manual layout transition.
///     </para>
///     <para>
///         <b>KSA 2026.8.5.5168 (rev 5154):</b> this used to delegate to KSA's
///         <c>RenderTarget</c>/<c>Framebuffer</c> helpers. That revision moved the game's own
///         offscreen rendering onto Vulkan dynamic rendering and <b>deleted</b>
///         <c>KSA.RenderTarget</c>, <c>KSA.Framebuffer</c> and <c>KSA.OffscreenTarget</c>. The
///         replacement <c>KSA.Rendering.RenderTarget</c> is an unrelated dynamic-rendering type with
///         no render pass or framebuffer, but <see cref="OffscreenImGuiBackend"/> builds its
///         pipelines against a real <see cref="VkRenderPass"/>. So the attachment/pass/framebuffer
///         construction the deleted helpers performed is reproduced here verbatim — purrTTY now owns
///         it outright and no longer tracks KSA's render architecture at all.
///     </para>
/// </summary>
public sealed class OffscreenRenderTarget : IDisposable
{
    // Attachment slot order — must match the clear-value order in PerFrameRenderer.
    private const int ColorAttachmentIndex = 0;
    private const int DepthAttachmentIndex = 1;
    private const int AttachmentCount = 2;

    private readonly Renderer _renderer;
    private bool _disposed;

    // Nullable, NOT bare ImageEx: the ctor's first Resize() calls DisposeGpu() on a
    // never-allocated target, and ImageEx is a readonly struct whose Dispose() is
    // `_allocator.FreeAllocation(...)` — on default(ImageEx) that allocator is null, so an
    // unguarded Dispose() NREs on every single build. `null` is the "no allocation" state.
    private ImageEx?    _colorAllocation;
    private ImageEx?    _depthAllocation;
    private VkImageView _depthImageView;

    public string       Name           { get; }
    public VkExtent2D   Extent         { get; private set; }
    public VkFormat     ColorFormat    { get; }
    public VkFormat     DepthFormat    { get; }
    public VkRenderPass RenderPass     { get; private set; }
    public VkFramebuffer Framebuffer   { get; private set; }
    public VkSampler    Sampler        { get; private set; }

    public VkImage     ColorImage     { get; private set; }
    public VkImageView ColorImageView { get; private set; }

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

        // Usage flags reproduce the deleted KSA RenderTarget.BuildAttachments: the colour slot is
        // transferable + storage + colour attachment, the depth slot transfer-dst + depth attachment,
        // and (since neither is transient) both additionally get Sampled | InputAttachment.
        const VkImageUsageFlags colorUsage =
            VkImageUsageFlags.TransferSrcBit | VkImageUsageFlags.TransferDstBit
            | VkImageUsageFlags.StorageBit | VkImageUsageFlags.ColorAttachmentBit
            | VkImageUsageFlags.SampledBit | VkImageUsageFlags.InputAttachmentBit;
        const VkImageUsageFlags depthUsage =
            VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.DepthStencilAttachmentBit
            | VkImageUsageFlags.SampledBit | VkImageUsageFlags.InputAttachmentBit;

        (var colorAllocation, ColorImageView) =
            CreateAttachment($"{Name} - Color Attachment", ColorFormat, colorUsage, VkImageAspectFlags.ColorBit);
        _colorAllocation = colorAllocation;
        ColorImage = colorAllocation.VkImage;

        (var depthAllocation, _depthImageView) =
            CreateAttachment($"{Name} - Depth Attachment", DepthFormat, depthUsage, DepthAspect(DepthFormat));
        _depthAllocation = depthAllocation;

        RenderPass  = CreateRenderPass();
        Framebuffer = CreateFramebuffer(RenderPass);

        var samplerCi = Presets.Sampler.SamplerLinearClamped;
        Sampler = _renderer.Device.CreateSampler(in samplerCi, null);
    }

    /// <summary>Depth-only formats carry no stencil aspect; every other depth format does.</summary>
    private static VkImageAspectFlags DepthAspect(VkFormat format)
        => format is VkFormat.D32SFloat or VkFormat.D16UNorm
            ? VkImageAspectFlags.DepthBit
            : VkImageAspectFlags.DepthBit | VkImageAspectFlags.StencilBit;

    private (ImageEx Allocation, VkImageView View) CreateAttachment(
        string name, VkFormat format, VkImageUsageFlags usage, VkImageAspectFlags aspect)
    {
        var allocation = _renderer.Allocator.CreateImage(new ImageEx.CreateInfo
        {
            Name                    = name,
            ImageType               = VkImageType._2D,
            ImageFormat             = format,
            ImageExtent             = new VkExtent3D(Extent.Width, Extent.Height, 1),
            ImageMipLevels          = 1,
            ImageArrayLayers        = 1,
            ImageSamples            = VkSampleCountFlags._1Bit,
            ImageTiling             = VkImageTiling.Optimal,
            ImageUsage              = usage,
            ImageInitialLayout      = VkImageLayout.Undefined,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });

        var view = _renderer.Device.CreateImageView(new VkImageViewCreateInfo
        {
            Image           = allocation.VkImage,
            ViewType        = VkImageViewType._2D,
            Format          = format,
            SubresourceRange = new VkImageSubresourceRange
            {
                AspectMask     = aspect,
                BaseMipLevel   = 0,
                LevelCount     = 1,
                BaseArrayLayer = 0,
                LayerCount     = 1,
            },
        }, null);

        return (allocation, view);
    }

    /// <summary>
    ///     One graphics subpass writing colour + depth. The colour attachment finalizes to
    ///     <c>ShaderReadOnlyOptimal</c> so the in-world quad can sample it directly; the external
    ///     subpass dependency orders this pass after the previous frame's attachment writes.
    /// </summary>
    private unsafe VkRenderPass CreateRenderPass()
    {
        var attachments = stackalloc VkAttachmentDescription[AttachmentCount];
        attachments[ColorAttachmentIndex] = new VkAttachmentDescription
        {
            Format         = ColorFormat,
            Samples        = VkSampleCountFlags._1Bit,
            LoadOp         = VkAttachmentLoadOp.Clear,
            StoreOp        = VkAttachmentStoreOp.Store,
            StencilLoadOp  = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout  = VkImageLayout.Undefined,
            FinalLayout    = VkImageLayout.ShaderReadOnlyOptimal,
        };
        attachments[DepthAttachmentIndex] = new VkAttachmentDescription
        {
            Format         = DepthFormat,
            Samples        = VkSampleCountFlags._1Bit,
            LoadOp         = VkAttachmentLoadOp.Clear,
            StoreOp        = VkAttachmentStoreOp.Store,
            StencilLoadOp  = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = VkAttachmentStoreOp.DontCare,
            InitialLayout  = VkImageLayout.Undefined,
            FinalLayout    = VkImageLayout.DepthStencilReadOnlyOptimal,
        };

        var refs = stackalloc VkAttachmentReference[AttachmentCount];
        refs[ColorAttachmentIndex] = new VkAttachmentReference
        {
            Attachment = ColorAttachmentIndex,
            Layout     = VkImageLayout.ColorAttachmentOptimal,
        };
        refs[DepthAttachmentIndex] = new VkAttachmentReference
        {
            Attachment = DepthAttachmentIndex,
            Layout     = VkImageLayout.DepthStencilAttachmentOptimal,
        };

        var subpass = new VkSubpassDescription
        {
            PipelineBindPoint       = VkPipelineBindPoint.Graphics,
            ColorAttachmentCount    = 1,
            ColorAttachments        = refs + ColorAttachmentIndex,
            DepthStencilAttachment  = refs + DepthAttachmentIndex,
        };

        var dependency = new VkSubpassDependency
        {
            SrcSubpass      = -1, // VK_SUBPASS_EXTERNAL
            DstSubpass      = 0,
            SrcStageMask    = VkPipelineStageFlags.LateFragmentTestsBit | VkPipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask    = VkPipelineStageFlags.EarlyFragmentTestsBit | VkPipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask   = VkAccessFlags.DepthStencilAttachmentWriteBit,
            DstAccessMask   = VkAccessFlags.ColorAttachmentWriteBit | VkAccessFlags.DepthStencilAttachmentWriteBit,
            DependencyFlags = VkDependencyFlags.None,
        };

        var createInfo = new VkRenderPassCreateInfo
        {
            AttachmentCount = AttachmentCount,
            Attachments     = attachments,
            SubpassCount    = 1,
            Subpasses       = &subpass,
            DependencyCount = 1,
            Dependencies    = &dependency,
        };
        return _renderer.Device.CreateRenderPass(in createInfo, null);
    }

    private unsafe VkFramebuffer CreateFramebuffer(VkRenderPass renderPass)
    {
        var views = stackalloc VkImageView[AttachmentCount];
        views[ColorAttachmentIndex] = ColorImageView;
        views[DepthAttachmentIndex] = _depthImageView;

        var createInfo = new VkFramebufferCreateInfo
        {
            RenderPass      = renderPass,
            Width           = Extent.Width,
            Height          = Extent.Height,
            Layers          = 1,
            Flags           = VkFramebufferCreateFlags.None,
            Attachments     = views,
            AttachmentCount = AttachmentCount,
        };
        return _renderer.Device.CreateFramebuffer(in createInfo, null);
    }

    private void DisposeGpu()
    {
        var device = _renderer.Device;

        if (Sampler.VkHandle != 0)
        {
            device.DestroySampler(Sampler, null);
            Sampler = default;
        }

        if (Framebuffer.VkHandle != 0)
        {
            device.DestroyFramebuffer(Framebuffer, null);
            Framebuffer = default;
        }

        if (ColorImageView.VkHandle != 0)
        {
            device.DestroyImageView(ColorImageView, null);
            ColorImageView = default;
        }

        if (_depthImageView.VkHandle != 0)
        {
            device.DestroyImageView(_depthImageView, null);
            _depthImageView = default;
        }

        _colorAllocation?.Dispose();
        _colorAllocation = null;
        _depthAllocation?.Dispose();
        _depthAllocation = null;
        ColorImage = default;

        if (RenderPass.VkHandle != 0)
        {
            device.DestroyRenderPass(RenderPass, null);
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
