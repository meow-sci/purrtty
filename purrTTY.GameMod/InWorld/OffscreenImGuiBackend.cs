using System;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Thin wrapper around <see cref="ImGuiBackendVulkanImpl"/> bound to our
///     off-screen <see cref="VkRenderPass"/>. Owns the GPU-side ImGui renderer
///     for the secondary context.
///     <para>
///         Construction installs renderer state into the <b>currently-bound</b>
///         ImGui context (<c>ImGui.GetIO().BackendRendererUserData</c> and
///         <c>ImGui.GetMainViewport().RendererUserData</c>). The constructor
///         also asserts that no other backend is already attached. Therefore the
///         secondary context MUST be current when this is constructed —
///         callers do that with <see cref="OffscreenContext.With(System.Action)"/>.
///         The same rule applies to <see cref="Dispose"/>.
///     </para>
///     <para>
///         Phase 4: construct + dispose only. Per-frame <see cref="Render"/> is
///         wired in Phase 5.
///     </para>
/// </summary>
public sealed class OffscreenImGuiBackend : IDisposable
{
    /// <summary>
    ///     The underlying KSA Vulkan ImGui backend. Treated as a black box: we
    ///     do not mutate it post-construction in Phase 4.
    /// </summary>
    public ImGuiBackendVulkanImpl Impl { get; private set; } = null!;

    private bool _disposed;

    /// <summary>
    ///     Construct the backend. The secondary ImGui context MUST be current
    ///     when this constructor runs — caller is responsible (use
    ///     <see cref="OffscreenContext.With(System.Action)"/>).
    /// </summary>
    public OffscreenImGuiBackend(Renderer renderer, VkRenderPass renderPass,
                                 int minImageCount, int imageCount,
                                 int descriptorPoolSize = 1000)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));

        Impl = new ImGuiBackendVulkanImpl(new ImGuiBackendVulkanImpl.CreateInfo
        {
            Device             = renderer.Device,
            GraphicsQueue      = renderer.Graphics,
            RenderPass         = renderPass,
            SubPass            = 0,
            MinImageCount      = minImageCount,
            ImageCount         = imageCount,
            SampleCount        = VkSampleCountFlags._1Bit,
            DescriptorPoolSize = descriptorPoolSize,
            // MinAllocationSize: leave default; KSA's backend handles a zero default.
        });
    }

    /// <summary>
    ///     Record draw commands for <paramref name="drawData"/> into
    ///     <paramref name="cmd"/>. Not called in Phase 4; wired in Phase 5.
    /// </summary>
    public void Render(ImDrawDataPtr drawData, CommandBuffer cmd)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OffscreenImGuiBackend));
        Impl.RenderDrawData(drawData, cmd);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Impl?.Dispose();
        Impl = null!;
    }
}
