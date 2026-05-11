using Brutal.VulkanApi;

namespace purrTTY.Display.Rendering.TerminalTexture;

/// <summary>
///     Runtime KSA rendering services used by optional terminal texture rendering.
/// </summary>
public sealed class TerminalRenderServices
{
    public static TerminalRenderServices? Current { get; private set; }

    public required global::Core.Renderer Renderer { get; init; }
    public KSA.GpuTextureSystem? TextureSystem { get; init; }
    public KSA.GpuMaterialSystem? MaterialSystem { get; init; }
    public KSA.SuperMeshRenderSystem? MeshRenderSystem { get; init; }
    public required VkSampler ImGuiSampler { get; init; }

    public bool HasWorldQuadServices => TextureSystem != null && MaterialSystem != null && MeshRenderSystem != null;

    public static void Install(TerminalRenderServices services)
    {
        Current = services ?? throw new ArgumentNullException(nameof(services));
    }

    public static void Clear()
    {
        Current = null;
    }
}
