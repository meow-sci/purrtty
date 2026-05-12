using System;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Phase 5 — drives the per-frame off-screen ImGui render loop using
///     <b>Strategy A</b>: a separate, mod-owned graphics command buffer submitted
///     with its own fence. We ring <see cref="_cmds"/> + <see cref="_fences"/> over
///     <see cref="MaxFramesInFlight"/> slots so the CPU can keep recording without
///     stalling the GPU.
///     <para>
///         The off-screen render pass (created by KSA's <c>RenderTarget.CreateRenderPass</c>)
///         already handles both image-layout transitions implicitly:
///         <list type="bullet">
///             <item>The first subpass dependency moves the color attachment
///                   <c>Undefined</c> → <c>ColorAttachmentOptimal</c>.</item>
///             <item>The render pass's <c>FinalLayout = ShaderReadOnlyOptimal</c>
///                   moves it back to a sampleable layout at <c>EndRenderPass</c>.</item>
///         </list>
///         Therefore <b>no explicit pre/post barriers are issued</b> from this class.
///     </para>
///     <para>
///         All ImGui mutations (NewFrame/Render) and the backend's RenderDrawData
///         must happen with the secondary <see cref="OffscreenContext"/> current,
///         so we always wrap them in <see cref="OffscreenContext.With"/>.
///     </para>
///     <para>
///         <see cref="Brutal.VulkanApi.Queue.Submit(System.Span{VkSubmitInfo}, VkFence)"/>
///         takes an internal lock, so it is safe to submit from <c>OnAfterGui</c>
///         even though the main render thread also submits later in the same frame.
///     </para>
/// </summary>
public sealed class PerFrameRenderer : IDisposable
{
    private readonly Renderer               _renderer;
    private readonly OffscreenRenderTarget  _target;
    private readonly OffscreenContext       _ctx;
    private readonly OffscreenImGuiBackend  _backend;

    private readonly VkCommandPool   _pool;
    private readonly CommandBuffer[] _cmds;
    private readonly VkFence[]       _fences;
    private          int             _currentIndex;

    /// <summary>
    ///     Number of GPU frames the ring buffer overlaps. Two matches the
    ///     backend's <c>MinImageCount</c>/<c>ImageCount</c> from Phase 4.
    /// </summary>
    public int MaxFramesInFlight => _cmds.Length;

    /// <summary>
    ///     Builds the secondary-context UI. Called every frame between
    ///     <c>NewFrame</c> and <c>Render</c> with the secondary context current.
    ///     Default is a no-op; the manager replaces it with the Phase 5 stub.
    /// </summary>
    public Action BuildUi { get; set; } = () => { };

    public PerFrameRenderer(Renderer renderer,
                            OffscreenRenderTarget target,
                            OffscreenContext ctx,
                            OffscreenImGuiBackend backend,
                            int framesInFlight = 2)
    {
        if (renderer == null) throw new ArgumentNullException(nameof(renderer));
        if (target   == null) throw new ArgumentNullException(nameof(target));
        if (ctx      == null) throw new ArgumentNullException(nameof(ctx));
        if (backend  == null) throw new ArgumentNullException(nameof(backend));
        if (framesInFlight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(framesInFlight),
                "framesInFlight must be >= 1.");
        }

        _renderer = renderer;
        _target   = target;
        _ctx      = ctx;
        _backend  = backend;

        // Transient: pool's buffers are short-lived (re-recorded each frame).
        // ResetCommandBufferBit: we reset individual buffers via
        // CommandBuffer.Reset() rather than resetting the whole pool.
        var poolCi = new VkCommandPoolCreateInfo
        {
            Flags            = VkCommandPoolCreateFlags.TransientBit |
                               VkCommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = renderer.Graphics.Index,
        };
        _pool = renderer.Device.CreateCommandPool(in poolCi, null);

        _cmds   = new CommandBuffer[framesInFlight];
        _fences = new VkFence[framesInFlight];

        try
        {
            // Allocate all primary command buffers up front in a single call.
            var allocCi = new VkCommandBufferAllocateInfo
            {
                CommandPool = _pool,
                Level       = VkCommandBufferLevel.Primary,
            };
            renderer.Device.AllocateCommandBuffers(allocCi, _cmds);

            // Fences are created SIGNALED so the very first WaitForFences in
            // Frame() returns immediately — no special "first frame" branch.
            for (int i = 0; i < framesInFlight; i++)
            {
                _fences[i] = renderer.Device.CreateFence(VkFenceCreateFlags.SignaledBit, null);
            }
        }
        catch
        {
            // If anything fails after the pool exists, undo what we did so the
            // caller doesn't leak GPU resources.
            for (int i = 0; i < framesInFlight; i++)
            {
                if (_fences[i].VkHandle != 0)
                {
                    try { renderer.Device.DestroyFence(_fences[i], null); } catch { /* best-effort */ }
                    _fences[i] = default;
                }
            }
            try { renderer.Device.DestroyCommandPool(_pool, null); } catch { /* best-effort */ }
            throw;
        }
    }

    /// <summary>
    ///     Records and submits one off-screen frame. Must be called on the main
    ///     thread (the only thread that owns the Vulkan device + ImGui contexts).
    /// </summary>
    public void Frame(double dt)
    {
        var device = _renderer.Device;
        var cmd    = _cmds[_currentIndex];
        var fence  = _fences[_currentIndex];

        // 1. Wait for the previous use of THIS slot to finish on the GPU before
        //    we reuse its command buffer / fence. Initially signaled, so the
        //    first frame returns immediately.
        Span<VkFence> fenceSpan = stackalloc VkFence[1] { fence };
        device.WaitForFences(fenceSpan, true, unchecked((nint)(-1L)));
        device.ResetFences(fenceSpan);

        // 2. Build the secondary-context UI and bake the draw lists. Every
        //    ImGui call here MUST happen with the secondary context current.
        ImDrawDataPtr drawData = default;
        _ctx.With(() =>
        {
            var io = ImGui.GetIO();
            io.DeltaTime   = (float)dt;
            io.DisplaySize = new float2(_target.Extent.Width, _target.Extent.Height);

            ImGui.NewFrame();
            try
            {
                BuildUi?.Invoke();
            }
            finally
            {
                // ImGui requires a matching Render() for every NewFrame(); skipping
                // it on a BuildUi exception would leave the context in a bad state.
                ImGui.Render();
            }
            drawData = ImGui.GetDrawData();
        });

        // 3. Reset & re-record the slot's command buffer.
        cmd.Reset(VkCommandBufferResetFlags.None);
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);

        RecordRenderPass(cmd, drawData);

        cmd.End();

        // 4. Submit on the same graphics queue the main render uses. Queue.Submit
        //    is internally locked, so this is safe vs. KSA's own submissions.
        Span<CommandBuffer>      cmds       = stackalloc CommandBuffer[1] { cmd };
        Span<VkSemaphore>        none       = default;
        Span<VkPipelineStageFlags> noStages = default;
        _renderer.Graphics.Submit(none, noStages, cmds, none, fence);

        // 5. Advance the ring.
        _currentIndex = (_currentIndex + 1) % _cmds.Length;
    }

    private unsafe void RecordRenderPass(CommandBuffer cmd, ImDrawDataPtr drawData)
    {
        // Two clear values: color (slot 0) and depth/stencil (slot 1). Order must
        // match the attachment order in RenderTarget.CreateRenderPass.
        VkClearValue* clearValues = stackalloc VkClearValue[2];
        clearValues[0] = new VkClearValue
        {
            Color = new VkClearColorValue { Float32 = new Brutal.Numerics.float4(0f, 0f, 0f, 1f) },
        };
        clearValues[1] = new VkClearValue
        {
            DepthStencil = new VkClearDepthStencilValue { Depth = 1f, Stencil = 0 },
        };

        var beginInfo = new VkRenderPassBeginInfo
        {
            RenderPass      = _target.RenderPass,
            Framebuffer     = _target.Framebuffer,
            RenderArea      = new VkRect2D(_target.Extent),
            ClearValueCount = 2,
            ClearValues     = clearValues,
        };

        cmd.BeginRenderPass(in beginInfo, VkSubpassContents.Inline);

        // The KSA Vulkan ImGui backend installs its draw state into the
        // currently-bound ImGui context's IO/viewport user-data, so we MUST
        // make the secondary context current while it records.
        //
        // Texture-lifecycle suppression: the secondary context shares the main
        // atlas, so drawData.Textures is the same ImFontAtlas.TexList that the
        // main backend manages. Letting the secondary backend's UpdateTexture
        // loop fire would let it call DestroyTexture on a descriptor set whose
        // BackendUserData was set by the main backend (its descriptor set was
        // allocated from main's pool, not ours) — that throws
        // DescriptorPoolInvalidOperationException. Null out the texture list
        // for the duration of this Render call so the secondary backend skips
        // the loop entirely; main remains the sole owner of the atlas
        // textures' lifecycle. Vulkan binding doesn't care which pool a
        // descriptor set was allocated from, so the secondary's draw commands
        // bind tex.TexID (set by main) and render correctly.
        var savedTextures = drawData.Textures;
        drawData.Textures = default;
        try
        {
            try
            {
                _ctx.With(() => _backend.Render(drawData, cmd));
            }
            catch (ImException ex)
            {
                // Skipping the texture-lifecycle loop (above) means the secondary
                // backend never assigns TexIDs of its own, but our render runs
                // BEFORE main's RenderDrawData each frame. So when an atlas glyph
                // is added this frame (status WantCreate, TexID Invalid) and the
                // terminal references it on the same frame, the assert in
                // ImDrawCmd.GetTexID fires here. Main's RenderDrawData later in
                // the same frame will assign the TexID, so next frame the same
                // texture is OK. Skip this frame's draws (the render pass is
                // still cleanly bracketed by Begin/EndRenderPass below) and let
                // the next frame succeed — much better than disabling the whole
                // feature for what is a one-frame transient.
                ModLog.Log.Debug($"purrTTY in-world: skipping frame (atlas texture not yet uploaded by main backend): {ex.Message}");
            }
        }
        finally
        {
            drawData.Textures = savedTextures;
        }

        cmd.EndRenderPass();
    }

    public void Dispose()
    {
        // Drain any outstanding GPU work that references our cmd buffers /
        // fences before we destroy them.
        if (_fences != null && _fences.Length > 0)
        {
            try
            {
                _renderer.Device.WaitForFences(_fences, true, unchecked((nint)(-1L)));
            }
            catch { /* best-effort: device may already be lost on shutdown */ }

            for (int i = 0; i < _fences.Length; i++)
            {
                if (_fences[i].VkHandle != 0)
                {
                    try { _renderer.Device.DestroyFence(_fences[i], null); } catch { /* best-effort */ }
                    _fences[i] = default;
                }
            }
        }

        if (_cmds != null && _cmds.Length > 0)
        {
            try { _renderer.Device.FreeCommandBuffers(_pool, _cmds); } catch { /* best-effort */ }
        }

        if (_pool.VkHandle != 0)
        {
            try { _renderer.Device.DestroyCommandPool(_pool, null); } catch { /* best-effort */ }
        }
    }
}
