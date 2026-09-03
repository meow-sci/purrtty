using Brutal.VulkanApi;
using HarmonyLib;
using KSA;
using KSA.Rendering;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld.Patches;

/// <summary>
///     Harmony postfix that injects the in-world quad draw <b>after</b> KSA's
///     atmosphere/cloud compositing and ocean rendering, not right after the opaque
///     vehicle-mesh pass. <c>SuperMeshRenderSystem.RenderTranslucencyPass</c> is the
///     game's own slot for translucent scene geometry (glass, particles, orbit lines):
///     it runs — and opens its own dynamic-rendering scope over the shared offscreen
///     color+depth images — strictly after <c>PlanetTransparenciesRenderer.Render</c>
///     (atmosphere/cloud, a <b>compute</b> pass that overwrites color directly from the
///     depth buffer) and <c>OceanRenderer.Render</c> (its own <c>VkRenderPass</c> over
///     the same images), both of which run right after the opaque pass and have no idea
///     a translucent, depth-test-no-write quad already drew there.
///     <para>
///         Rationale for hooking here instead of <c>SuperMeshRenderSystem.RenderMainPass</c>
///         (the original approach): a quad drawn during the opaque pass is correctly
///         depth-tested against vehicle parts and the planet's solid body (all of which
///         draw before it), but atmosphere/cloud/ocean draw <b>after</b> it, read the
///         still-unwritten-by-us depth buffer, and unconditionally repaint every pixel
///         they decide belongs to the planet — including the quad's — producing a hard
///         cutout that exactly follows the planet's screen-space silhouette. Postfixing
///         the translucency pass instead means the quad is the last thing to touch color
///         for the frame (besides gizmos/orbit lines/UI, which are 2D overlays that don't
///         re-derive color from depth), so it survives.
///     </para>
///     <para>
///         Unlike <c>RenderMainPass</c>, <c>RenderTranslucencyPass</c> closes its own
///         dynamic-rendering scope (<c>BeginRendering</c>/<c>EndRendering</c>) before
///         returning, so a postfix can't simply append draws into it — it must reopen a
///         second dynamic-rendering scope of its own (<c>LoadOp.Load</c> for color+depth,
///         matching KSA's own <c>PartModelGlass.WriteCommandsColor</c>, which draws in
///         this exact spot). <see cref="SharedQuadResource"/>'s pipelines are built with
///         <c>VkPipelineRenderingCreateInfo</c> (no <c>VkRenderPass</c> handle) to be
///         valid inside that scope, mirroring the same KSA convention.
///     </para>
///     <para>
///         Registered as an <b>optional</b> patch in <c>Patcher</c>: a drifted target
///         must never block terminal init. <see cref="InWorldTerminalManager.Active"/>
///         and <see cref="InWorldTerminalManager.Instance"/> are plain statics flipped
///         on the main thread; the postfix runs on that same render thread, so no
///         synchronization is needed.
///     </para>
/// </summary>
[HarmonyPatch(typeof(SuperMeshRenderSystem), nameof(SuperMeshRenderSystem.RenderTranslucencyPass))]
internal static class RenderTranslucencyPassPatch
{
    [HarmonyPostfix]
    public static unsafe void Postfix(CommandBuffer commandBuffer, bool useCustomRenderPass, IViewport viewport)
    {
        if (!useCustomRenderPass || viewport == null)
        {
            return;
        }

        if (!InWorldTerminalManager.Active)
        {
            return;
        }

        try
        {
            if (viewport.OffscreenTarget is not { } offscreenTarget)
            {
                return;
            }

            // KSA 2026.8.5.5168 (rev 5154) moved offscreen rendering off VkRenderPass/framebuffers
            // onto Vulkan dynamic rendering and deleted KSA.OffscreenTarget (and its
            // MultisampleColorImage/MultisampleDepthImage pair). The replacement,
            // KSA.Rendering.RenderTarget, exposes ColorAttachment/DepthAttachment, which already
            // resolve to the MSAA image when multisampled and the single-sampled output otherwise —
            // exactly the choice the old `msaa ? Multisample... : ...` ternaries made, so the sample
            // count no longer has to be probed separately. Both are nullable (a target may carry only
            // colour or only depth); this pass needs both.
            if (offscreenTarget.ColorAttachment is not { } colorImage
                || offscreenTarget.DepthAttachment is not { } depthImage)
            {
                return;
            }

            // Grant blend-read access to whatever atmosphere/ocean/RenderTranslucencyPass
            // just wrote (dynamic rendering does not insert this barrier automatically
            // between separate BeginRendering/EndRendering scopes) — mirrors the entry
            // transition KSA's own PartModelGlass.WriteCommandsColor performs at this
            // same point in the frame.
            //
            // Rev 5154 gave every RenderImage a tracked layout/access state, so the barrier now names
            // only the state this pass needs; the source state comes from the image itself instead of
            // being hard-coded here, which removes a whole class of silent drift.
            commandBuffer.PipelineBarrier2(colorImage, ImageBarrierInfo.Presets.ColorAttachmentRead);

            var colorAttachment = new VkRenderingAttachmentInfo
            {
                ImageLayout = VkImageLayout.ColorAttachmentOptimal,
                ImageView   = colorImage.ImageView,
                ResolveMode = VkResolveModeFlags.None,
                LoadOp      = VkAttachmentLoadOp.Load,
                StoreOp     = VkAttachmentStoreOp.Store,
            };
            var depthAttachment = new VkRenderingAttachmentInfo
            {
                ImageLayout = VkImageLayout.DepthStencilAttachmentOptimal,
                ImageView   = depthImage.ImageView,
                ResolveMode = VkResolveModeFlags.None,
                LoadOp      = VkAttachmentLoadOp.Load,
                StoreOp     = VkAttachmentStoreOp.Store,
            };
            var renderingInfo = new VkRenderingInfo
            {
                RenderArea          = new VkRect2D(viewport.Size.X, viewport.Size.Y),
                LayerCount          = 1,
                ColorAttachmentCount = 1,
                ColorAttachments    = &colorAttachment,
                DepthAttachment     = &depthAttachment,
            };

            commandBuffer.BeginRendering(in renderingInfo);
            try
            {
                InWorldTerminalManager.Instance?.RecordDrawAll(commandBuffer);
            }
            finally
            {
                commandBuffer.EndRendering();
            }
        }
        catch (Exception ex)
        {
            // Never crash the render loop on a transient draw failure; disable so it
            // doesn't spam every frame (re-enabled on the next toggle/reload).
            ModLog.Log.Error($"purrTTY in-world quad draw failed: {ex}");
            InWorldTerminalManager.Active = false;
        }
    }
}
