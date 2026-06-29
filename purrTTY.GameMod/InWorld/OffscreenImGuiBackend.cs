using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Core;
using KSA;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Thin wrapper around <see cref="ImGuiBackendVulkanImpl"/> bound to our
///     off-screen <see cref="VkRenderPass"/>. Owns the GPU-side ImGui renderer for
///     the secondary context.
///     <para>
///         Construction installs renderer state into the <b>currently-bound</b>
///         ImGui context (<c>ImGui.GetIO().BackendRendererUserData</c> and
///         <c>ImGui.GetMainViewport().RendererUserData</c>) and asserts that no
///         other backend is already attached. Therefore the secondary context MUST
///         be current when this is constructed — callers do that with
///         <see cref="OffscreenImGuiContext.With(System.Action)"/>. The same rule
///         applies to <see cref="Dispose"/>.
///     </para>
///     <para>
///         Phase 2: construct + dispose, plus an optional trivial frame. Per-frame
///         <see cref="Render"/> against the off-screen target is wired in Phase 3.
///     </para>
/// </summary>
public sealed class OffscreenImGuiBackend : IDisposable
{
    /// <summary>
    ///     The underlying KSA Vulkan ImGui backend. Treated as a black box.
    /// </summary>
    public ImGuiBackendVulkanImpl Impl { get; private set; } = null!;

    private bool _disposed;

    /// <summary>
    ///     Construct the backend. The secondary ImGui context MUST be current when
    ///     this constructor runs — the caller is responsible (use
    ///     <see cref="OffscreenImGuiContext.With(System.Action)"/>).
    /// </summary>
    public OffscreenImGuiBackend(Renderer renderer, VkRenderPass renderPass,
                                 int minImageCount, int imageCount,
                                 int descriptorPoolSize = 256)
    {
        ArgumentNullException.ThrowIfNull(renderer);

        // The off-screen terminal target is single-sample (no MSAA) — ImGui text
        // needs none. (The world-space quad pipeline, by contrast, matches the
        // scene pass's MSAA in a later phase.) The backend asserts
        // MinImageCount >= 2, ImageCount >= MinImageCount, DescriptorPoolSize > 0.
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
    ///     <paramref name="cmd"/>. Must be called with the secondary context current
    ///     and inside a begun render pass on the off-screen target (Phase 3).
    /// </summary>
    public void Render(ImDrawDataPtr drawData, CommandBuffer cmd)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
