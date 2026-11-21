using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Owns a sampleable off-screen render target for the in-world terminal:
///     a single colour attachment and a linear-clamped <see cref="VkSampler"/>.
///     <para>
///         There is no render pass or framebuffer: <see cref="PerFrameRenderer"/> renders into
///         <see cref="ColorImageView"/> with Vulkan dynamic rendering and issues the layout
///         transitions explicitly (<c>Undefined</c> → <c>ColorAttachmentOptimal</c> before the
///         scope, → <c>ShaderReadOnlyOptimal</c> after it), so the in-world quad samples the
///         texture in the layout its descriptor set declares.
///     </para>
///     <para>
///         <b>KSA 2026.8.5.5168 (rev 5154):</b> this used to delegate to KSA's
///         <c>RenderTarget</c>/<c>Framebuffer</c> helpers. That revision moved the game's own
///         offscreen rendering onto Vulkan dynamic rendering and <b>deleted</b>
///         <c>KSA.RenderTarget</c>, <c>KSA.Framebuffer</c> and <c>KSA.OffscreenTarget</c>, so purrTTY
///         reproduced the attachment/pass/framebuffer construction itself.
///     </para>
///     <para>
///         <b>KSA 2026.11.16.5901 (rev 5816):</b> <see cref="OffscreenImGuiBackend"/>'s underlying
///         <c>ImGuiBackendVulkanImpl</c> now builds its pipeline for dynamic rendering against one
///         colour format with <b>no depth format</b>. Its pipeline can neither be bound inside a
///         <see cref="VkRenderPass"/> nor in a rendering scope that carries a depth attachment, so the
///         render pass, framebuffer and the never-sampled depth attachment were removed. ImGui does
///         not depth-test; nothing else read the depth image.
///     </para>
/// </summary>
public sealed class OffscreenRenderTarget : IDisposable
{
    private readonly Renderer _renderer;
    private bool _disposed;

    // Nullable, NOT bare ImageEx: the ctor's first Resize() calls DisposeGpu() on a
    // never-allocated target, and ImageEx is a readonly struct whose Dispose() is
    // `_allocator.FreeAllocation(...)` — on default(ImageEx) that allocator is null, so an
    // unguarded Dispose() NREs on every single build. `null` is the "no allocation" state.
    private ImageEx?    _colorAllocation;

    public string       Name           { get; }
    public VkExtent2D   Extent         { get; private set; }
    public VkFormat     ColorFormat    { get; }
    public VkSampler    Sampler        { get; private set; }

    public VkImage     ColorImage     { get; private set; }
    public VkImageView ColorImageView { get; private set; }

    public OffscreenRenderTarget(Renderer renderer, string name, int width, int height, VkFormat colorFormat)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name cannot be null or empty.", nameof(name));

        _renderer   = renderer;
        Name        = name;
        ColorFormat = colorFormat;

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

        // Usage flags reproduce the colour slot of the deleted KSA RenderTarget.BuildAttachments:
        // transferable + storage + colour attachment, plus (non-transient) Sampled | InputAttachment.
        const VkImageUsageFlags colorUsage =
            VkImageUsageFlags.TransferSrcBit | VkImageUsageFlags.TransferDstBit
            | VkImageUsageFlags.StorageBit | VkImageUsageFlags.ColorAttachmentBit
            | VkImageUsageFlags.SampledBit | VkImageUsageFlags.InputAttachmentBit;

        (var colorAllocation, ColorImageView) =
            CreateAttachment($"{Name} - Color Attachment", ColorFormat, colorUsage, VkImageAspectFlags.ColorBit);
        _colorAllocation = colorAllocation;
        ColorImage = colorAllocation.VkImage;

        var samplerCi = Presets.Sampler.SamplerLinearClamped;
        Sampler = _renderer.Device.CreateSampler(in samplerCi, null);
    }

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

    private void DisposeGpu()
    {
        var device = _renderer.Device;

        if (Sampler.VkHandle != 0)
        {
            device.DestroySampler(Sampler, null);
            Sampler = default;
        }

        if (ColorImageView.VkHandle != 0)
        {
            device.DestroyImageView(ColorImageView, null);
            ColorImageView = default;
        }

        _colorAllocation?.Dispose();
        _colorAllocation = null;
        ColorImage = default;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeGpu();
    }
}
