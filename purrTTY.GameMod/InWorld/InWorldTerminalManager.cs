using System;
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
///     Owns the offscreen render target + ImGui context + per-frame renderer +
///     the quad that samples the target.
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    private readonly InWorldSettings _settings;
    private OffscreenRenderTarget? _target;
    private OffscreenContext? _ctx;
    private OffscreenImGuiBackend? _backend;
    private PerFrameRenderer? _frame;
    private QuadDisplay? _quad;
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

    /// <summary>
    ///     Returns true when the SubPart Id in <see cref="InWorldSettings.TargetPartName"/>
    ///     resolves to a real Part on the currently-controlled vessel. The UI
    ///     uses this to enable/disable the Toggle button, and <see cref="Toggle"/>
    ///     uses the same check to reject an enable when there is no valid anchor.
    /// </summary>
    public bool CanResolveAnchor()
    {
        if (string.IsNullOrEmpty(_settings.TargetPartName)) return false;
        Vehicle? vehicle = Program.ControlledVehicle;
        if (vehicle == null) return false;
        return FindPart(vehicle, _settings.TargetPartName) != null;
    }

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

            // R8G8B8A8Unorm (not SRGB): UnlitMesh.frag applies gammaToLinear() to
            // the sampled texel before writing, on the assumption the texture
            // holds gamma-encoded data. If we use an SRGB-format target, the GPU
            // auto-decodes on sample so the shader's gammaToLinear() runs on
            // already-linear values and the in-world colors come out
            // noticeably darker than the on-screen terminal. UNORM keeps the
            // ImGui-written bytes raw so the shader's gamma decode is the
            // single, correct one.
            _target = new OffscreenRenderTarget(
                renderer,
                "purrTTY-Offscreen",
                _settings.TextureWidth,
                _settings.TextureHeight,
                VkFormat.R8G8B8A8UNorm,
                renderer.DepthFormat);

            // Secondary ImGui context shares the main font atlas so we don't
            // duplicate font upload memory. Constructed after the GPU target
            // so disposal can tear them down in reverse order.
            _ctx = new OffscreenContext(_settings.TextureWidth, _settings.TextureHeight);

            // Secondary Vulkan ImGui backend bound to our off-screen render pass.
            // The backend's ctor mutates the *current* ImGui context's IO + main
            // viewport, so we must construct it under _ctx.With(...).
            // minImageCount/imageCount = 2 matches typical MaxFramesInFlight and
            // satisfies the backend's MinImageCount >= 2 assertion;
            // descriptorPoolSize = 256 is KSA's hard floor.
            _ctx.With(() =>
            {
                _backend = new OffscreenImGuiBackend(
                    renderer,
                    _target.RenderPass,
                    minImageCount: 2,
                    imageCount: 2,
                    descriptorPoolSize: 256);
            });

            _frame = new PerFrameRenderer(renderer, _target, _ctx, _backend!, framesInFlight: 2);

            // Pipeline + quad mesh that samples the off-screen target. The
            // quad's pose is composed per-frame from the user's anchor SubPart
            // + offset + rotation settings, so no separate "Anchor()" step is
            // needed here.
            _quad = new QuadDisplay(renderer, _target, _settings);

            // Per-frame ray-vs-quad pick that flips _focus on click.
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

        // Pick + Esc handling. Wrapped separately so a picking failure does NOT
        // disable the whole feature (unlike a render failure above).
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
    ///         When enabling, the SubPart anchor must resolve to a real Part on
    ///         the controlled vessel — if not, the toggle is rejected and the
    ///         feature stays disabled. The quad's pose is composed per-frame
    ///         from the live settings, so there is no separate "anchor" step.
    ///     </para>
    /// </summary>
    public void Toggle()
    {
        bool newState = !_settings.Enabled;

        if (newState)
        {
            if (string.IsNullOrEmpty(_settings.TargetPartName))
            {
                ModLog.Log.Debug("purrTTY in-world: cannot enable — no SubPart selected; toggle rejected");
                return;
            }
            Vehicle? vehicle = Program.ControlledVehicle;
            if (vehicle == null)
            {
                ModLog.Log.Debug("purrTTY in-world: cannot enable — no controlled vehicle; toggle rejected");
                return;
            }
            if (FindPart(vehicle, _settings.TargetPartName) == null)
            {
                ModLog.Log.Debug(
                    $"purrTTY in-world: cannot enable — SubPart '{_settings.TargetPartName}' not found on controlled vehicle; toggle rejected");
                return;
            }
        }
        else
        {
            // Detach the patches first so the postfix can't race with our
            // teardown. Drop focus so the GLFW forwarders + game-input prefix
            // stop firing immediately.
            FramePatches.IsActive = false;
            FramePatches.Focus = null;
            FramePatches.SecondaryContext = null;
        }

        _settings.Enabled = newState;
        FramePatches.Display  = (_settings.Enabled && _quad != null) ? _quad : null;
        FramePatches.IsActive = _settings.Enabled && _quad != null;

        // Always reset focus on either edge of the toggle so the in-world
        // terminal never comes back up already-focused, and so the screen-space
        // path is restored cleanly on disable.
        _focus.State = InWorldFocusState.NotFocused;
        FramePatches.Focus = _settings.Enabled ? _focus : null;
        FramePatches.SecondaryContext = _settings.Enabled ? _ctx : null;

        ModLog.Log.Debug($"purrTTY in-world terminal {(_settings.Enabled ? "enabled" : "disabled")}");
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
        // see a freed quad / context mid-shutdown.
        FramePatches.IsActive = false;
        FramePatches.Display  = null;
        FramePatches.Focus = null;
        FramePatches.SecondaryContext = null;
        _focus.State = InWorldFocusState.NotFocused;

        // Tear down in reverse construction order:
        //   _quad     → owns the in-world pipeline + buffers (pure Vulkan).
        //   _frame    → drains its fences then frees its cmd buffers + pool
        //               (pure Vulkan; no ImGui state, so no With() needed).
        //   _backend  → mutates ImGui state on the secondary context, so MUST
        //               run with that context current.
        //   _ctx      → destroys the secondary ImGui context.
        //   _target   → destroys the off-screen render pass + framebuffer.
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
