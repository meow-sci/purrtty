using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using KSA.Rendering;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Phase 3 — drives the per-frame off-screen ImGui render loop on a separate,
///     mod-owned graphics command buffer submitted with its own fence. We ring
///     <see cref="_cmds"/> + <see cref="_fences"/> over <see cref="MaxFramesInFlight"/>
///     slots so the CPU can keep recording without stalling the GPU.
///     <para>
///         The frame is a colour-only Vulkan <b>dynamic-rendering</b> scope over the
///         off-screen target (KSA 2026.11.16.5901, rev 5816, moved
///         <c>ImGuiBackendVulkanImpl</c> to dynamic rendering, so its pipeline no longer
///         binds inside a <c>VkRenderPass</c>). Dynamic rendering performs no implicit
///         layout transitions, so this class issues the two the old render pass did:
///         <list type="bullet">
///             <item>Before the scope: <c>Undefined</c> → <c>ColorAttachmentOptimal</c>
///                   (contents discarded — the scope clears), waiting out the previous
///                   frame's quad sampling and our own previous writes.</item>
///             <item>After the scope: → <c>ShaderReadOnlyOptimal</c>, making the writes
///                   visible to the in-world quad's fragment-shader sampling.</item>
///         </list>
///     </para>
///     <para>
///         All ImGui mutations (NewFrame/Render) and the backend's RenderDrawData
///         must happen with the secondary <see cref="OffscreenImGuiContext"/> current,
///         so we always wrap them in <see cref="OffscreenImGuiContext.With"/>.
///     </para>
///     <para>
///         <see cref="QueueExtensions.Submit"/> takes an internal lock, so it is
///         safe to submit from <c>OnAfterGui</c> even though the main render thread
///         also submits later in the same frame.
///     </para>
/// </summary>
public sealed class PerFrameRenderer : IDisposable
{
    private readonly Renderer               _renderer;
    private readonly OffscreenRenderTarget  _target;
    private readonly OffscreenImGuiContext  _ctx;
    private readonly OffscreenImGuiBackend  _backend;

    private readonly VkCommandPool   _pool;
    private readonly CommandBuffer[] _cmds;
    private readonly VkFence[]       _fences;
    private          int             _currentIndex;

    /// <summary>
    ///     Number of GPU frames the ring buffer overlaps. Two matches the backend's
    ///     <c>MinImageCount</c>/<c>ImageCount</c> and KSA's frames-in-flight.
    /// </summary>
    public int MaxFramesInFlight => _cmds.Length;

    /// <summary>
    ///     Builds the secondary-context UI. Called every frame between
    ///     <c>NewFrame</c> and <c>Render</c> with the secondary context current.
    ///     Default is a no-op; the manager replaces it with a placeholder in
    ///     Phase 3 and with the real terminal draw in Phase 4.
    /// </summary>
    public Action BuildUi { get; set; } = () => { };

    public PerFrameRenderer(Renderer renderer,
                            OffscreenRenderTarget target,
                            OffscreenImGuiContext ctx,
                            OffscreenImGuiBackend backend,
                            int framesInFlight = 2)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(backend);
        if (framesInFlight < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(framesInFlight), "framesInFlight must be >= 1.");
        }

        _renderer = renderer;
        _target   = target;
        _ctx      = ctx;
        _backend  = backend;

        // Transient: the pool's buffers are short-lived (re-recorded each frame).
        // ResetCommandBufferBit: we reset individual buffers via CommandBuffer.Reset()
        // rather than resetting the whole pool.
        //
        // QueueFamilyIndex must be the graphics queue's FAMILY index (not its index
        // within the family) — command buffers from this pool are submitted to
        // renderer.Graphics, so the pool must belong to that queue's family.
        var poolCi = new VkCommandPoolCreateInfo
        {
            Flags            = VkCommandPoolCreateFlags.TransientBit |
                               VkCommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = renderer.Graphics.Family,
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

            // Fences are created SIGNALED so the very first WaitForFences in Frame()
            // returns immediately — no special "first frame" branch.
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

        // 1. Wait for the previous use of THIS slot to finish on the GPU before we
        //    reuse its command buffer / fence. Initially signaled, so the first
        //    frame returns immediately.
        Span<VkFence> fenceSpan = stackalloc VkFence[1] { fence };
        device.WaitForFences(fenceSpan, true, unchecked((nint)(-1L)));
        device.ResetFences(fenceSpan);

        // 2. Build the secondary-context UI and bake the draw lists. Every ImGui
        //    call here MUST happen with the secondary context current.
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

        RecordRendering(cmd, drawData);

        cmd.End();

        // 4. Submit on the same graphics queue the main render uses. Submit is
        //    internally locked, so this is safe vs. KSA's own submissions.
        Span<CommandBuffer>        cmds     = stackalloc CommandBuffer[1] { cmd };
        Span<VkSemaphore>          none     = default;
        Span<VkPipelineStageFlags> noStages = default;
        _renderer.Graphics.Submit(none, noStages, cmds, none, fence);

        // 5. Advance the ring.
        _currentIndex = (_currentIndex + 1) % _cmds.Length;
    }

    /// <summary>
    ///     Source state of the pre-scope transition: the texture's previous use. Its layout is
    ///     <c>Undefined</c> so the old contents are discarded (the scope clears them), and its
    ///     stages cover both earlier consumers — our previous frame's colour writes and the
    ///     scene pass's fragment-shader sampling of the quad (a write-after-read hazard across
    ///     submissions on the same queue). The old render pass's external subpass dependency
    ///     provided this implicitly.
    /// </summary>
    private static readonly ImageBarrierInfo PreviousUse = new(
        VkImageLayout.Undefined,
        VkAccessFlags2.ColorAttachmentWriteBit,
        VkPipelineStageFlags2.ColorAttachmentOutputBit | VkPipelineStageFlags2.FragmentShaderBit);

    private unsafe void RecordRendering(CommandBuffer cmd, ImDrawDataPtr drawData)
    {
        // ImGui blends, so the attachment is read as well as written inside the scope.
        ReadOnlySpan<ImageTransition> toAttachment =
            [new ImageTransition(_target.ColorImage, PreviousUse, ImageBarrierInfo.Presets.ColorAttachmentReadWrite)];
        cmd.TransitionImages2(toAttachment);

        // Colour only: ImGuiBackendVulkanImpl's pipeline is created with one colour format and
        // no depth format, so a depth attachment here would not match it.
        var colorAttachment = new VkRenderingAttachmentInfo
        {
            ImageView   = _target.ColorImageView,
            ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            ResolveMode = VkResolveModeFlags.None,
            LoadOp      = VkAttachmentLoadOp.Clear,
            StoreOp     = VkAttachmentStoreOp.Store,
            // Fully transparent (alpha 0). The terminal background rect is drawn at the
            // theme's BackgroundOpacity, so untouched/translucent regions keep a sub-1
            // alpha in the R8G8B8A8 texture — the world-space quad composites that
            // per-pixel coverage over the 3D scene (premultiplied-alpha).
            ClearValue  = new VkClearValue
            {
                Color = new VkClearColorValue { Float32 = new Brutal.Numerics.float4(0f, 0f, 0f, 0f) },
            },
        };
        var renderingInfo = new VkRenderingInfo
        {
            RenderArea           = new VkRect2D(_target.Extent),
            LayerCount           = 1,
            ColorAttachmentCount = 1,
            ColorAttachments     = &colorAttachment,
        };

        cmd.BeginRendering(in renderingInfo);

        // The KSA Vulkan ImGui backend installs its draw state into the
        // currently-bound ImGui context's IO/viewport user-data, so we MUST make
        // the secondary context current while it records.
        //
        // Texture-lifecycle suppression: the secondary context shares the main
        // atlas, so drawData.Textures is the same ImFontAtlas texture list the main
        // backend manages. Letting the secondary backend's UpdateTexture loop fire
        // would let it call DestroyTexture on a descriptor set allocated from main's
        // pool (not ours) — that throws DescriptorPoolInvalidOperationException.
        // Null out the texture list for the duration of this Render call so the
        // secondary backend skips the loop entirely; main remains the sole owner of
        // the atlas textures' lifecycle. Vulkan binding doesn't care which pool a
        // descriptor set came from, so the secondary's draws bind tex.TexID (set by
        // main) and render correctly.
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
                // Skipping the texture-lifecycle loop means the secondary backend
                // never assigns TexIDs of its own, but our render runs BEFORE main's
                // RenderDrawData each frame. So when an atlas glyph is added this
                // frame (status WantCreate, TexID Invalid) and the terminal
                // references it on the same frame, the assert in ImDrawCmd.GetTexID
                // fires here. Main's RenderDrawData later this same frame assigns the
                // TexID, so next frame the same texture is OK. Skip this frame's
                // draws (the scope is still cleanly bracketed by
                // Begin/EndRendering and the post transition) and let the next frame
                // succeed — far better than disabling the whole feature for a
                // one-frame transient.
                ModLog.Log.Debug($"purrTTY in-world: skipping frame (atlas texture not yet uploaded by main backend): {ex.Message}");
            }
        }
        finally
        {
            drawData.Textures = savedTextures;
        }

        cmd.EndRendering();

        // Back to the layout the quad's descriptor set declares, with the colour writes made
        // visible to its fragment-shader sampling (the old render pass's FinalLayout).
        ReadOnlySpan<ImageTransition> toSampled =
            [new ImageTransition(_target.ColorImage, ImageBarrierInfo.Presets.ColorAttachmentWrite, ImageBarrierInfo.Presets.SampledReadF)];
        cmd.TransitionImages2(toSampled);
    }

    public void Dispose()
    {
        // Drain any outstanding GPU work that references our cmd buffers / fences
        // before we destroy them.
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
