using System;
using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Input;
using purrTTY.GameMod.InWorld.Patches;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.Logging;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Top-level coordinator for the in-world (render-to-texture) terminal feature.
///     Phase 2: owns an <see cref="OffscreenRenderTarget"/> (render pass + framebuffer
///     + sampler) but still does not render anything into it.
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    private readonly InWorldSettings _settings;
    private OffscreenRenderTarget? _target;
    private OffscreenContext? _ctx;
    private OffscreenImGuiBackend? _backend;
    private PerFrameRenderer? _frame;
    private QuadDisplay? _quad;
    private SubPartOverrideBase? _override;
    private readonly InWorldFocus _focus = new();
    private QuadPicker? _picker;
    private bool _initialized;
    private bool _disposed;

    public InWorldTerminalManager(InWorldSettings settings)
    {
        _settings = settings;
    }

    public InWorldSettings Settings => _settings;
    public bool IsInitialized => _initialized;
    public bool IsQuadAnchored => _quad?.IsAnchored ?? false;
    public bool IsFocused => _focus.IsFocused;
    public bool HasSubPartOverride => _override != null;

    /// <summary>
    ///     Replaces the per-frame UI builder used inside the secondary ImGui
    ///     context. Set after <see cref="Initialize"/> to mirror real terminal
    ///     content into the off-screen target. A null value resets to a no-op.
    /// </summary>
    public void SetBuildUi(Action? build)
    {
        if (_frame == null) return;
        _frame.BuildUi = build ?? (() => { });
    }

    /// <summary>
    ///     Re-runs the quad anchor logic against the current main camera.
    ///     Useful for the user when they have moved/rotated and want the in-world
    ///     terminal to re-attach in front of the new view. Safe no-op when the
    ///     feature is not initialized, when there is no active camera, or when
    ///     the quad is missing.
    /// </summary>
    public void ReanchorQuad()
    {
        if (_disposed || !_initialized || _quad == null) return;
        var camera = Program.GetMainCamera();
        if (camera == null) return;
        _quad.Anchor(camera);
    }

    /// <summary>
    ///     Rebinds the SubPart material override to whichever part is currently
    ///     named in <see cref="InWorldSettings.TargetPartName"/>. Called from
    ///     the UI when the user picks a different part. No-op when the feature
    ///     is disabled (the new name will take effect on the next toggle-on).
    /// </summary>
    public void RebindSubPart()
    {
        if (_disposed) return;

        // Detach the patch first so a postfix never sees a freed override.
        FramePatches.Override = null;
        try { _override?.Dispose(); } catch { /* best-effort */ }
        _override = null;

        if (!_settings.Enabled || !_initialized)
        {
            // Picker still updated TargetPartName; next Toggle-on will pick it up.
            return;
        }

        TrySetupSubPartOverride();
        FramePatches.Override = _override;
    }

    /// <summary>
    ///     Called once when the game is fully loaded and the renderer is live.
    /// </summary>
    public void Initialize()
    {
        try
        {
            var renderer = Program.GetRenderer();
            if (renderer == null)
            {
                ModLog.Log.Error("purrTTY in-world: Program.GetRenderer() returned null; disabling in-world terminal");
                _settings.Enabled = false;
                return;
            }

            _target = new OffscreenRenderTarget(
                renderer,
                "purrTTY-Offscreen",
                _settings.TextureWidth,
                _settings.TextureHeight,
                VkFormat.R8G8B8A8SRGB,
                renderer.DepthFormat);

            // Phase 3: secondary ImGui context shares the main font atlas so we
            // do not duplicate font upload memory. Constructed after the GPU
            // target so disposal can tear them down in reverse order.
            _ctx = new OffscreenContext(_settings.TextureWidth, _settings.TextureHeight);

            // Phase 4: secondary Vulkan ImGui backend bound to our off-screen
            // render pass. The backend's ctor mutates the *current* ImGui
            // context's IO + main viewport, so we must construct it under
            // _ctx.With(...). minImageCount/imageCount = 2 matches typical
            // MaxFramesInFlight and satisfies the backend's MinImageCount >= 2
            // assertion; descriptorPoolSize = 256 is KSA's hard floor (it asserts
            // >= 256 in CreateDescriptorPool — anything lower throws at ctor time).
            _ctx.With(() =>
            {
                _backend = new OffscreenImGuiBackend(
                    renderer,
                    _target.RenderPass,
                    minImageCount: 2,
                    imageCount: 2,
                    descriptorPoolSize: 256);
            });

            // Phase 5: per-frame command buffer + fence ring. framesInFlight: 2
            // matches the backend's image count from Phase 4. The default
            // BuildUi is a no-op; install the Phase 5 sanity stub. Phase 6+
            // will swap in the real terminal UI.
            _frame = new PerFrameRenderer(renderer, _target, _ctx, _backend!, framesInFlight: 2);
            _frame.BuildUi = static () =>
            {
                ImGui.Begin("In-World Terminal");
                ImGui.Text("Hello, in-world terminal (Phase 5)");
                ImGui.End();
            };

            // Phase 6A: pipeline + quad mesh that samples the off-screen target.
            // Anchoring (model-matrix compute) happens later in Toggle() when an
            // active camera exists.
            _quad = new QuadDisplay(renderer, _target, _settings);

            // Phase 7A: per-frame ray-vs-quad pick that flips _focus on click.
            _picker = new QuadPicker(_quad);

            _initialized = true;
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: failed to create off-screen resources; disabling in-world terminal: {ex}");
            _settings.Enabled = false;
            // Tear down in reverse construction order. The frame ring owns
            // pure-Vulkan resources (no ImGui state) and disposes safely
            // outside the secondary context. The backend's Dispose touches
            // ImGui state on the secondary context, so it must run under
            // _ctx.With(...) — but only if _ctx is still alive (any earlier
            // step in construction can throw before later fields are
            // assigned, in which case there is nothing to dispose for them).
            try { _quad?.Dispose(); } catch { /* best-effort */ }
            _quad = null;
            try { _frame?.Dispose(); } catch { /* best-effort */ }
            _frame = null;
            if (_ctx != null && _backend != null)
            {
                try { _ctx.With(() => { _backend!.Dispose(); }); } catch { /* best-effort */ }
            }
            _backend = null;
            _ctx?.Dispose();
            _ctx = null;
            _target?.Dispose();
            _target = null;
        }
    }

    /// <summary>
    ///     Called every frame from <c>[StarMapAfterGui]</c>.
    ///     <paramref name="dt"/> is the same dt the game passes to its own AfterGui callbacks.
    /// </summary>
    public void OnAfterGui(double dt)
    {
        if (!_settings.Enabled || !_initialized || _disposed)
        {
            return;
        }
        if (_frame == null)
        {
            return;
        }
        try
        {
            _frame.Frame(dt);
        }
        catch (Exception ex)
        {
            // A render-loop exception is fatal for this feature: keep retrying
            // would just spam the log and may corrupt Vulkan state. Disable so
            // the user can re-toggle after addressing the underlying issue.
            ModLog.Log.Error($"purrTTY in-world: per-frame render failed; disabling in-world terminal: {ex}");
            _settings.Enabled = false;
            return;
        }

        // Phase 7A: pick + Esc handling. Wrapped separately so a picking failure
        // does NOT disable the whole feature (unlike a render failure above).
        try
        {
            _picker?.Tick(_focus);
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world picker tick failed: {ex}");
        }
    }

    /// <summary>
    ///     Toggles the master enable flag and logs the new state.
    ///     <para>
    ///         Phase 6A: when enabling, anchor the quad to the active camera's
    ///         current forward/up. Anchoring requires a live camera (the player
    ///         must be in flight, not on a loading screen); if none exists we
    ///         revert the toggle so the patch never fires against an un-anchored
    ///         quad.
    ///     </para>
    ///     <para>
    ///         Phase 6B: when enabling and <see cref="InWorldSettings.TargetPartName"/>
    ///         is set, also try to construct a <see cref="SubPartOverrideBase"/>
    ///         (per-template or per-instance overlay, depending on
    ///         <see cref="InWorldSettings.TargetOverrideMode"/>) that swaps the chosen
    ///         part's diffuse + emissive bindless handles for our off-screen texture.
    ///         Failure here is non-fatal: the quad still renders. When the target name
    ///         is empty we log all available part Ids on the active vessel as a one-shot
    ///         hint to the user.
    ///     </para>
    ///     <para>
    ///         The patch enable bits (<see cref="FramePatches.IsActive"/>,
    ///         <see cref="FramePatches.Display"/>, <see cref="FramePatches.Override"/>)
    ///         are updated last so the postfixes never see half-initialized state.
    ///     </para>
    /// </summary>
    public void Toggle()
    {
        bool newState = !_settings.Enabled;

        if (newState && _quad != null)
        {
            var camera = Program.GetMainCamera();
            if (camera == null)
            {
                ModLog.Log.Debug("purrTTY in-world: cannot enable quad — no active camera; toggle rejected");
                return;
            }
            _quad.Anchor(camera);

            TrySetupSubPartOverride();
        }

        if (!newState)
        {
            // Detach the patches first so the override teardown can't race with
            // a postfix that still holds the old reference. Also drop focus so
            // the GLFW forwarders + game-input prefix stop firing immediately.
            FramePatches.IsActive = false;
            FramePatches.Override = null;
            FramePatches.Focus = null;
            FramePatches.SecondaryContext = null;
            try { _override?.Dispose(); } catch { /* best-effort */ }
            _override = null;
        }

        _settings.Enabled = newState;
        FramePatches.Display  = (_settings.Enabled && _quad != null) ? _quad : null;
        FramePatches.Override = _settings.Enabled ? _override : null;
        FramePatches.IsActive = _settings.Enabled && _quad != null;

        // Always reset focus on either edge of the toggle so the in-world
        // terminal never comes back up already-focused, and so the screen-space
        // path is restored cleanly on disable.
        _focus.State = InWorldFocusState.NotFocused;
        FramePatches.Focus = _settings.Enabled ? _focus : null;
        FramePatches.SecondaryContext = _settings.Enabled ? _ctx : null;

        ModLog.Log.Debug($"purrTTY in-world terminal {(_settings.Enabled ? "enabled" : "disabled")}");
    }

    private void TrySetupSubPartOverride()
    {
        if (_target == null) return;

        var renderer = Program.GetRenderer();
        if (renderer == null) return;

        Vehicle? vehicle = Program.ControlledVehicle;
        if (vehicle == null)
        {
            ModLog.Log.Debug("purrTTY in-world: no controlled vehicle; subpart override skipped (quad-only).");
            return;
        }

        // Empty target name: log every visible part Id on the vessel so the user
        // can pick one. This is a one-shot diagnostic emitted at toggle-on.
        if (string.IsNullOrEmpty(_settings.TargetPartName))
        {
            var ids = new System.Text.StringBuilder();
            foreach (Part p in vehicle.Parts.Parts)
            {
                if (ids.Length > 0) ids.Append(", ");
                ids.Append(p.Id);
                foreach (Part sub in p.SubParts)
                {
                    ids.Append(", ");
                    ids.Append(sub.Id);
                }
            }
            ModLog.Log.Debug($"purrTTY in-world: available parts: {ids}");
            return;
        }

        Part? hit = FindPart(vehicle, _settings.TargetPartName);
        if (hit == null)
        {
            ModLog.Log.Error(
                $"purrTTY in-world: TargetPartName '{_settings.TargetPartName}' not found on active vessel; "
                + "falling back to quad-only.");
            return;
        }

        try
        {
            // Mode dispatch: per-template rewrites the shared PerDrawData (and so
            // affects every instance of the same template); per-instance overlay
            // appends a second draw for the chosen instance only.
            _override = _settings.TargetOverrideMode switch
            {
                OverrideMode.PerInstanceOverlay
                    => new SubPartOverlayOverride(renderer, _target, hit),
                _   => new SubPartTemplateOverride(renderer, _target, hit),
            };
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: subpart override construction failed; quad-only: {ex}");
            _override = null;
        }
    }

    private static Part? FindPart(Vehicle vehicle, string id)
    {
        foreach (Part p in vehicle.Parts.Parts)
        {
            if (p.Id == id) return p;
            foreach (Part sub in p.SubParts)
            {
                if (sub.Id == id) return sub;
            }
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        // Detach the patches before we tear anything down so the postfixes can't
        // see a freed quad/override/context mid-shutdown.
        FramePatches.IsActive = false;
        FramePatches.Display  = null;
        FramePatches.Override = null;
        FramePatches.Focus = null;
        FramePatches.SecondaryContext = null;
        _focus.State = InWorldFocusState.NotFocused;

        // Tear down in reverse construction order:
        //   _override → frees its bindless texture handle in the shared lib.
        //   _quad     → owns the in-world pipeline + buffers (pure Vulkan).
        //   _frame    → drains its fences then frees its cmd buffers + pool
        //               (pure Vulkan; no ImGui state, so no With() needed).
        //   _backend  → mutates ImGui state on the secondary context, so MUST
        //               run with that context current.
        //   _ctx      → destroys the secondary ImGui context.
        //   _target   → destroys the off-screen render pass + framebuffer.
        try { _override?.Dispose(); } catch { /* best-effort */ }
        _override = null;
        try { _quad?.Dispose(); } catch { /* best-effort */ }
        _quad = null;
        try { _frame?.Dispose(); } catch { /* best-effort */ }
        _frame = null;
        if (_ctx != null && _backend != null)
        {
            _ctx.With(() => { _backend!.Dispose(); });
        }
        _backend = null;
        _ctx?.Dispose();
        _ctx = null;
        _target?.Dispose();
        _target = null;
    }
}
