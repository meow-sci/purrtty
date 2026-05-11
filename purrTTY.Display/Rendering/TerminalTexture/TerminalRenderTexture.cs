using System.Reflection;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;

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
            renderer.ColorFormat,
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
        var bindlessLibrary = bindlessLibraryField?.GetValue(_services.TextureSystem);
        var addTextureMethod = bindlessLibrary?.GetType().GetMethod("AddTexture", BindingFlags.Instance | BindingFlags.Public, [typeof(VkImageView)]);
        if (addTextureMethod == null)
        {
            return false;
        }

        _bindlessTextureHandle = (int)addTextureMethod.Invoke(bindlessLibrary, [_target.ColorImage.ImageView])!;
        return true;
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
    }
}