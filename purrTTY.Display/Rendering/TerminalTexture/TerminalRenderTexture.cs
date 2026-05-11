using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using purrTTY.Logging;

namespace purrTTY.Display.Rendering.TerminalTexture;

/// <summary>
///     Owns a KSA render target that can be sampled by ImGui and, optionally, KSA materials.
/// </summary>
internal sealed class TerminalRenderTexture : IDisposable
{
    private readonly TerminalRenderServices _services;
    private RenderTarget? _target;
    private VkRenderPass _renderPass;
    private ImTextureRef? _imguiTexture;
    private int? _bindlessTextureHandle;
    private bool _hasRenderPass;
    private bool _hasUploadedPixels;
    private int _width;
    private int _height;

    public TerminalRenderTexture(TerminalRenderServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public int Width => _width;

    public int Height => _height;

    public bool IsReady => _target != null && _imguiTexture.HasValue;

    public ImTextureRef ImGuiTexture => _imguiTexture ?? throw new InvalidOperationException("Terminal texture is not allocated");

    public int? BindlessTextureHandle => _bindlessTextureHandle;

    public RenderTarget Target => _target ?? throw new InvalidOperationException("Terminal texture is not allocated");

    public VkRenderPass RenderPass => _hasRenderPass ? _renderPass : throw new InvalidOperationException("Terminal render pass is not allocated");

    public void EnsureSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (_target != null && _width == width && _height == height)
        {
            return;
        }

        DisposeGpuResources(waitIdle: true);

        var renderer = _services.Renderer;
        _target = new RenderTarget(
            renderer,
            "purrTTY Terminal Texture",
            new VkExtent2D(width, height),
            VkFormat.R8G8B8A8UNorm,
            VkFormat.Undefined);

        _renderPass = _target.CreateRenderPassWithOptions(VkImageLayout.ShaderReadOnlyOptimal);
        _hasRenderPass = true;
        _target.BuildFramebuffer(_renderPass);

        _imguiTexture = ImGuiBackend.Vulkan.AddTexture(
            _services.ImGuiSampler,
            _target.ColorImage.ImageView,
            VkImageLayout.ShaderReadOnlyOptimal);

        _width = width;
        _height = height;
        _hasUploadedPixels = false;
    }

    public void UploadRgba(ReadOnlySpan<byte> rgba)
    {
        if (_target == null)
        {
            throw new InvalidOperationException("Terminal texture is not allocated");
        }

        int expectedByteCount = _width * _height * 4;
        if (rgba.Length < expectedByteCount)
        {
            throw new ArgumentException($"Expected at least {expectedByteCount} RGBA bytes, got {rgba.Length}", nameof(rgba));
        }

        byte[] uploadBytes = rgba[..expectedByteCount].ToArray();
        var renderer = _services.Renderer;
        using var stagingPool = renderer.Allocator.CreateStagingPool(renderer.GraphicsAndCompute, 1);
        var commandBuffer = stagingPool.NextCommandBuffer();
        commandBuffer.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);

        var stagingBuffer = stagingPool.AddStagingBuffer(uploadBytes.AsSpan());
        var colorImage = _target.ColorImage;
        var image = colorImage.Image;
        var subresourceRange = new VkImageSubresourceRange
        {
            AspectMask = VkImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1
        };

        var uploadBarrier = new VkImageMemoryBarrier
        {
            SrcAccessMask = _hasUploadedPixels ? VkAccessFlags.ShaderReadBit : VkAccessFlags.None,
            DstAccessMask = VkAccessFlags.TransferWriteBit,
            OldLayout = _hasUploadedPixels ? VkImageLayout.ShaderReadOnlyOptimal : VkImageLayout.Undefined,
            NewLayout = VkImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = -1,
            DstQueueFamilyIndex = -1,
            Image = image,
            SubresourceRange = subresourceRange
        };
        commandBuffer.PipelineBarrier(
            _hasUploadedPixels ? VkPipelineStageFlags.FragmentShaderBit : VkPipelineStageFlags.TopOfPipeBit,
            VkPipelineStageFlags.TransferBit,
            VkDependencyFlags.None,
            null,
            null,
            new ReadOnlySpan<VkImageMemoryBarrier>(ref uploadBarrier));

        var copyRegion = new VkBufferImageCopy
        {
            ImageSubresource = new VkImageSubresourceLayers
            {
                AspectMask = VkImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1
            },
            ImageExtent = new VkExtent3D(_width, _height, 1)
        };
        commandBuffer.CopyBufferToImage(
            stagingBuffer.VkBuffer,
            image,
            VkImageLayout.TransferDstOptimal,
            new ReadOnlySpan<VkBufferImageCopy>(ref copyRegion));

        var sampleBarrier = new VkImageMemoryBarrier
        {
            SrcAccessMask = VkAccessFlags.TransferWriteBit,
            DstAccessMask = VkAccessFlags.ShaderReadBit,
            OldLayout = VkImageLayout.TransferDstOptimal,
            NewLayout = VkImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = -1,
            DstQueueFamilyIndex = -1,
            Image = image,
            SubresourceRange = subresourceRange
        };
        commandBuffer.PipelineBarrier(
            VkPipelineStageFlags.TransferBit,
            VkPipelineStageFlags.FragmentShaderBit,
            VkDependencyFlags.None,
            null,
            null,
            new ReadOnlySpan<VkImageMemoryBarrier>(ref sampleBarrier));

        commandBuffer.End();
        _hasUploadedPixels = true;
    }

    public bool EnsureBindlessTexture()
    {
        if (_bindlessTextureHandle.HasValue)
        {
            return true;
        }

        if (_target == null)
        {
            return false;
        }

        var bindlessLibraryField = typeof(GpuTextureSystem).GetField("_bindlessTextureLib", BindingFlags.Instance | BindingFlags.NonPublic);
        if (bindlessLibraryField == null)
        {
            ModLog.Log.Debug("purrTTY texture bindless registration failed: _bindlessTextureLib field was not found");
            return false;
        }

        var bindlessLibrary = bindlessLibraryField.GetValue(_services.TextureSystem);
        if (bindlessLibrary == null)
        {
            ModLog.Log.Debug("purrTTY texture bindless registration failed: _bindlessTextureLib is null");
            return false;
        }

        var addTextureMethod = bindlessLibrary.GetType().GetMethod("AddTexture", BindingFlags.Instance | BindingFlags.Public, [typeof(VkImageView)]);
        if (addTextureMethod == null)
        {
            ModLog.Log.Debug("purrTTY texture bindless registration failed: AddTexture(VkImageView) was not found");
            return false;
        }

        try
        {
            var result = addTextureMethod.Invoke(bindlessLibrary, [_target.ColorImage.ImageView]);
            if (result is int handle && handle >= 0)
            {
                _bindlessTextureHandle = handle;
                return true;
            }

            ModLog.Log.Debug($"purrTTY texture bindless registration failed: AddTexture returned {result ?? "null"}");
            return false;
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY texture bindless registration failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        DisposeGpuResources(waitIdle: true);
    }

    private void DisposeGpuResources(bool waitIdle)
    {
        if (_target == null && !_hasRenderPass && !_imguiTexture.HasValue && !_bindlessTextureHandle.HasValue)
        {
            return;
        }

        if (waitIdle)
        {
            _services.Renderer.Device.WaitIdle();
        }

        if (_imguiTexture.HasValue)
        {
            ImGuiBackend.Vulkan.RemoveTexture(_imguiTexture.Value);
            _imguiTexture = null;
        }

        if (_bindlessTextureHandle.HasValue)
        {
            _services.TextureSystem.Free(_bindlessTextureHandle.Value);
            _bindlessTextureHandle = null;
        }

        if (_hasRenderPass)
        {
            _services.Renderer.Device.DestroyRenderPass(_renderPass, null);
            _renderPass = default;
            _hasRenderPass = false;
        }

        _target?.Dispose();
        _target = null;
        _width = 0;
        _height = 0;
        _hasUploadedPixels = false;
    }
}