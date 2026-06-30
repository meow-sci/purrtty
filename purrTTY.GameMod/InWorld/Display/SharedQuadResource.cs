using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace purrTTY.GameMod.InWorld.Display;

/// <summary>
///     The in-world quad GPU resources that are <b>identical for every instance</b>:
///     the two graphics pipelines (reverse-Z depth-write / no-depth), the pipeline
///     layout (one combined-image-sampler + a mat4 vertex push constant), the
///     descriptor-set layout, the vertex input, and the unit-quad vertex/index
///     buffers. One copy is built by the coordinator and shared by all
///     <see cref="InWorldQuad"/>s — which then own only their per-instance descriptor
///     set (the off-screen texture binding) and per-frame MVP push constant. This
///     hoist turns N× (2 pipelines + layout + geometry) into 1×.
///     <para>
///         Built after the renderer is live (does not need an active ImGui frame).
///         The KSA <c>UnlitMesh.{vert,frag}</c> shader modules are owned by ModLibrary
///         and must NOT be destroyed here. Main thread only.
///     </para>
/// </summary>
public sealed class SharedQuadResource : IDisposable
{
    private readonly Renderer    _renderer;
    private readonly VertexInput _vertexInput;
    private bool _disposed;

    /// <summary>One combined-image-sampler at binding 0 (fragment); each instance's set conforms to this.</summary>
    public DescriptorSetLayoutEx DescriptorSetLayout { get; }

    /// <summary>Descriptor-set layout + a mat4 vertex push constant.</summary>
    public VkPipelineLayout PipelineLayout { get; }

    /// <summary>Reverse-Z depth-test+write pipeline (part mode / occluding billboard).</summary>
    public VkPipeline PipelineDepthWrite { get; }

    /// <summary>No-depth pipeline (always-on-top HUD billboard).</summary>
    public VkPipeline PipelineNoDepth { get; }

    /// <summary>Shared centered unit-quad vertex buffer.</summary>
    public BufferEx VertexBuffer { get; }

    /// <summary>Shared unit-quad index buffer (6 indices).</summary>
    public BufferEx IndexBuffer { get; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct QuadVertex
    {
        public float3 Pos;
        public float2 Uv;
    }

    public unsafe SharedQuadResource(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;

        // The unlit mesh shader pair KSA already ships. Get<T> throws if either id is
        // missing, which the coordinator catches to skip the feature cleanly. The
        // modules are owned by ModLibrary — we must NOT destroy them.
        ShaderReference vertRef = ModLibrary.Get<ShaderReference>("UnlitMeshVert");
        ShaderReference fragRef = ModLibrary.Get<ShaderReference>("UnlitMeshFrag");
        VkShaderModule  vertModule = vertRef;  // implicit operator
        VkShaderModule  fragModule = fragRef;

        var device = renderer.Device;

        // ---- Descriptor set layout: one combined-image-sampler at binding 0 ----
        var bindingDesc = new VkDescriptorSetLayoutBinding
        {
            Binding         = 0,
            DescriptorType  = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags      = VkShaderStageFlags.FragmentBit,
        };
        var dslCreateInfo = new DescriptorSetLayoutEx.CreateInfo
        {
            Bindings = new Span<VkDescriptorSetLayoutBinding>(ref bindingDesc),
        };
        DescriptorSetLayout = device.CreateDescriptorSetLayout(dslCreateInfo, null);

        // ---- Pipeline layout: one descriptor set layout + mat4 push constant (vertex) ----
        VkPushConstantRange pushRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit,
            Offset     = ByteSize.Zero,
            Size       = ByteSize.Of<float4x4>(),
        };
        VkDescriptorSetLayout dslHandle = DescriptorSetLayout;
        PipelineLayout = device.CreatePipelineLayout(
            new ReadOnlySpan<VkDescriptorSetLayout>(ref dslHandle),
            new ReadOnlySpan<VkPushConstantRange>(ref pushRange),
            null);

        // ---- Vertex input: single binding stride 20, locations 0=pos (vec3), 1=uv (vec2) ----
        _vertexInput = new VertexInput(1, 2)
            .AddBinding(0, ByteSize.Of<QuadVertex>(), VkVertexInputRate.Vertex)
            .AddAttribute(0, 0, VkFormat.R32G32B32SFloat, ByteSize.Zero)
            .AddAttribute(1, 0, VkFormat.R32G32SFloat,   ByteSize.Of<float3>())
            .Check();

        // ---- Shader stages ----
        var stagesArr = stackalloc VkPipelineShaderStageCreateInfo[2];
        stagesArr[0] = new VkPipelineShaderStageCreateInfo
        {
            Stage  = VkShaderStageFlags.VertexBit,
            Module = vertModule,
            Name   = Presets.Entrypoint.Main,
        };
        stagesArr[1] = new VkPipelineShaderStageCreateInfo
        {
            Stage  = VkShaderStageFlags.FragmentBit,
            Module = fragModule,
            Name   = Presets.Entrypoint.Main,
        };

        // ---- Pipeline state — the load-bearing bits (z-order fix from commit 5be1aad) ----
        // Bind to Program.OffScreenPass, NOT Program.MainPass: the scene (parts + our
        // postfix) runs inside the offscreen MSAA pass; MainPass is the 1-bit
        // swapchain pass. RasterizationSamples must match the scene framebuffer's MSAA.
        // Cull none: a user-orientable single quad should never be invisible from its
        // back face. Two pipelines differ only in depth state.
        var multisample = new VkPipelineMultisampleStateCreateInfo
        {
            RasterizationSamples = Program.OffScreenPass.SampleCount,
        };

        var pipelineInfo = new VkGraphicsPipelineCreateInfo
        {
            Layout             = PipelineLayout,
            RenderPass         = Program.OffScreenPass.Pass,
            Subpass            = 0,
            StageCount         = 2,
            Stages             = stagesArr,
            DynamicState       = renderer.DynamicStateInfo,
            ViewportState      = renderer.ViewportState,
            VertexInputState   = _vertexInput,
            InputAssemblyState = Presets.InputAssembly.TriangleList,
            RasterizationState = Presets.Rasterization.Fill.CullNone,
            DepthStencilState  = RenderingPresets.ReverseZDepthStencil.DepthTestWrite,
            ColorBlendState    = Presets.BlendState.BlendNone,
            MultisampleState   = &multisample,
        };

        try
        {
            PipelineDepthWrite = device.CreateGraphicsPipeline(default(VkPipelineCache), pipelineInfo, null);

            pipelineInfo.DepthStencilState = RenderingPresets.ReverseZDepthStencil.NoDepthTest;
            PipelineNoDepth = device.CreateGraphicsPipeline(default(VkPipelineCache), pipelineInfo, null);

            // ---- Vertex / index buffers ----
            // Centered unit quad in the XY plane, +Z normal. V is flipped so the
            // texture's (0,0) top-left aligns with the ImGui-written content.
            Span<QuadVertex> verts = stackalloc QuadVertex[4]
            {
                new QuadVertex { Pos = new float3(-0.5f, -0.5f, 0f), Uv = new float2(0f, 1f) },
                new QuadVertex { Pos = new float3( 0.5f, -0.5f, 0f), Uv = new float2(1f, 1f) },
                new QuadVertex { Pos = new float3( 0.5f,  0.5f, 0f), Uv = new float2(1f, 0f) },
                new QuadVertex { Pos = new float3(-0.5f,  0.5f, 0f), Uv = new float2(0f, 0f) },
            };
            Span<ushort> indices = stackalloc ushort[6] { 0, 1, 2, 0, 2, 3 };

            VertexBuffer = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
            {
                Name                    = "purrTTY-Quad-VB",
                BufferUsage             = VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit,
                BufferSize              = ByteSize.Of(verts),
                AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
                BufferSharingMode       = VkSharingMode.Exclusive,
            });
            IndexBuffer = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
            {
                Name                    = "purrTTY-Quad-IB",
                BufferUsage             = VkBufferUsageFlags.IndexBufferBit | VkBufferUsageFlags.TransferDstBit,
                BufferSize              = ByteSize.Of(indices),
                AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
                BufferSharingMode       = VkSharingMode.Exclusive,
            });

            // Mirror SimpleVkMesh's upload idiom: one staging cmd buffer, both copies,
            // Submit (which fences) + Wait. Synchronous is fine — runs once.
            using var staging = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
            var stagingCmd = staging.NextCommandBuffer();
            stagingCmd.Begin();
            VkUtils.StageAndUploadToBuffer(staging, VertexBuffer.VkBuffer, VertexBuffer.BindOffset, verts, stagingCmd);
            VkUtils.StageAndUploadToBuffer(staging, IndexBuffer.VkBuffer,  IndexBuffer.BindOffset,  indices, stagingCmd);
            stagingCmd.End();
            staging.Submit().Wait();
        }
        catch
        {
            DestroyGpu();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyGpu();
    }

    private void DestroyGpu()
    {
        var device = _renderer.Device;
        // Reverse construction order. VkShaderModules are owned by ModLibrary — do
        // NOT destroy them here.
        if (PipelineNoDepth.VkHandle != 0)
        {
            try { device.DestroyPipeline(PipelineNoDepth, null); } catch { /* best-effort */ }
        }
        if (PipelineDepthWrite.VkHandle != 0)
        {
            try { device.DestroyPipeline(PipelineDepthWrite, null); } catch { /* best-effort */ }
        }
        if (PipelineLayout.VkHandle != 0)
        {
            try { device.DestroyPipelineLayout(PipelineLayout, null); } catch { /* best-effort */ }
        }
        try { VertexBuffer.Dispose(); } catch { /* best-effort */ }
        try { IndexBuffer.Dispose();  } catch { /* best-effort */ }
        if (DescriptorSetLayout != null)
        {
            try { device.DestroyDescriptorSetLayout(DescriptorSetLayout, null); } catch { /* best-effort */ }
        }
        try { _vertexInput?.Bindings.Dispose();   } catch { /* best-effort */ }
        try { _vertexInput?.Attributes.Dispose(); } catch { /* best-effort */ }
    }
}
