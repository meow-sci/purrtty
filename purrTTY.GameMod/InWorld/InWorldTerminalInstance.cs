using Brutal.VulkanApi;
using Core;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Settings;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     One in-world terminal: its own off-screen GPU target + secondary ImGui
///     context/backend + per-frame render loop + dedicated shell session
///     (<see cref="InWorldTerminalRenderer"/>) + world-space quad. Implements
///     <see cref="INamedTerminal"/> so it appears in the shared terminal-target
///     registry (the theme picker) alongside 2D windows.
///     <para>
///         The constructor allocates GPU resources (renderer must be live; built from
///         <c>OnAfterGui</c> where an ImGui frame is active, since sizing measures the
///         font cell). On any build failure it tears down the partial allocation and
///         rethrows; the coordinator catches and logs. It self-registers in the target
///         registry on success and unregisters on <see cref="Dispose"/>. Main thread only.
///     </para>
/// </summary>
public sealed class InWorldTerminalInstance : INamedTerminal, IDisposable
{
    private readonly InWorldTerminalRecord _record;
    private readonly ThemeConfiguration _config;
    private readonly ThemeCatalog _catalog;
    private Renderer _renderer = null!;

    // Live grid resize: the new texture extent, applied only after the frames that
    // may have recorded the current quad have been submitted (same window as the
    // coordinator's deferred teardown), then under a device drain. The quad is not
    // drawn while a resize is pending.
    private (int Width, int Height)? _pendingResize;
    private int _resizeFramesLeft;

    private OffscreenRenderTarget? _target;
    private OffscreenImGuiContext? _ctx;
    private OffscreenImGuiBackend? _backend;
    private PerFrameRenderer? _perFrame;
    private InWorldTerminalRenderer? _content;
    private InWorldQuad? _quad;
    private bool _hasFocus;
    private bool _registered;
    private bool _disposed;

    /// <summary>This terminal's configuration (placement read live by the quad).</summary>
    public InWorldTerminalRecord Record => _record;

    /// <summary>The dedicated terminal content renderer (its own shell session + fonts).</summary>
    public InWorldTerminalRenderer Content => _content!;

    /// <summary>True when this instance is a camera billboard (no ego-space raycast / click-to-focus).</summary>
    public bool IsBillboard => _record.IsBillboard;

    /// <summary>True once a GPU draw/frame failure has retired this instance; the coordinator prunes it.</summary>
    public bool IsFailed { get; private set; }

    /// <inheritdoc/>
    public string Name => _record.Name;

    /// <inheritdoc/>
    public TerminalKind Kind => TerminalKind.InWorld;

    /// <inheritdoc/>
    public bool HasFocus
    {
        get => _hasFocus;
        internal set
        {
            _hasFocus = value;
            // Drive the content cursor (solid when focused, hollow when not).
            if (_content != null)
            {
                _content.HasFocus = value;
            }
        }
    }

    public InWorldTerminalInstance(
        ThemeConfiguration config, ThemeCatalog catalog, InWorldTerminalRecord record, SharedQuadResource sharedQuad)
    {
        _record = record;
        _config = config;
        _catalog = catalog;

        try
        {
            var renderer = Program.GetRenderer()
                ?? throw new InvalidOperationException("Program.GetRenderer() returned null");
            _renderer = renderer;

            // Grid-driven texture: derive the off-screen extent from the fixed
            // cols×rows and this terminal's font cell (measured on the live ImGui
            // frame's shared atlas), clamped to the GPU texture range.
            var (cellWidth, cellHeight) = InWorldTerminalRenderer.MeasureCell(config, catalog, record.ThemeName);
            int width = Math.Clamp((int)MathF.Ceiling(record.Cols * cellWidth), 256, 4096);
            int height = Math.Clamp((int)MathF.Ceiling(record.Rows * cellHeight), 256, 4096);

            // R8G8B8A8Unorm (not SRGB): UnlitMesh.frag applies gammaToLinear() to the
            // sampled texel, expecting gamma-encoded bytes. An SRGB target would
            // double-decode and render the in-world terminal noticeably dark.
            _target = new OffscreenRenderTarget(
                renderer, "purrTTY-Offscreen", width, height, VkFormat.R8G8B8A8UNorm, renderer.DepthFormat);

            // Secondary ImGui context (shares the main font atlas) + a second Vulkan
            // ImGui backend bound to the off-screen pass. The backend ctor mutates
            // the current context's IO, so build it under With(...).
            _ctx = new OffscreenImGuiContext(width, height);
            _ctx.With(() =>
            {
                _backend = new OffscreenImGuiBackend(renderer, _target.RenderPass,
                    minImageCount: 2, imageCount: 2, descriptorPoolSize: 256);
            });

            _perFrame = new PerFrameRenderer(renderer, _target, _ctx, _backend!, framesInFlight: 2);

            // Dedicated terminal session (its own shell + theme) drawn into the
            // off-screen target via the shared FrameGridRenderer.
            _content = new InWorldTerminalRenderer(config, catalog, record.Launch, record.ThemeName);
            _perFrame.BuildUi = _content.BuildUi;

            // World-space quad sampling the texture; reads the record live (so launch-
            // UI placement edits update it instantly). Draws through the shared
            // pipeline/geometry, owning only its descriptor set.
            _quad = new InWorldQuad(renderer, _target, _record, sharedQuad);
        }
        catch
        {
            Teardown();
            throw;
        }

        // Register only after a successful build (the coordinator assigns a unique
        // name first, so this always succeeds).
        _registered = TerminalTargetRegistry.Register(this);
    }

    /// <summary>Drives one off-screen terminal frame (which the world-space quad samples).</summary>
    public void Frame(double dt)
    {
        if (_pendingResize is { } extent && --_resizeFramesLeft <= 0)
        {
            ApplyResize(extent.Width, extent.Height);
            _pendingResize = null;
        }

        _pendingFrameDt += dt;

        // Tick every game frame (the surface inbox must drain — gotcha 18), but re-record +
        // submit the off-screen pass only when something visible changed; the world quad keeps
        // sampling the previous texture otherwise (gatOS PERF_IMPROVEMENT_PLAN.md P5 — this
        // pass used to render every game frame unconditionally, per in-world terminal).
        if (_content is { } content && _target is { } target
            && !content.TickAndCheckDirty(target.Extent.Width, target.Extent.Height))
        {
            return;
        }

        _perFrame!.Frame(_pendingFrameDt);
        _pendingFrameDt = 0;
    }

    // Accumulates skipped-frame time so ImGui's DeltaTime stays truthful when a render runs.
    private double _pendingFrameDt;

    /// <summary>Appends this instance's quad draw to the scene-pass command buffer.</summary>
    public void RecordDraw(CommandBuffer commandBuffer)
    {
        // A pending resize is draining scene-pass references to the current texture;
        // recording the quad now would re-arm the use-after-free the drain prevents.
        if (_pendingResize != null)
        {
            return;
        }

        _quad!.RecordDraw(commandBuffer);
    }

    /// <summary>Ego-space ray-tests the quad (part mode, or a click-to-focus billboard); see <see cref="InWorldQuad.TryRaycast"/>.</summary>
    public bool TryRaycast(Ray ray, out double t, out float2 uv) => _quad!.TryRaycast(ray, out t, out uv);

    /// <summary>Retires this instance after a GPU draw/frame failure; the coordinator prunes + disposes it.</summary>
    public void MarkFailed() => IsFailed = true;

    /// <summary>Applies a theme bundle live (colors + font + opacity + cursor).</summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        _record.ThemeName = theme.Name;
        _content?.ApplyTheme(theme);
    }

    /// <summary>Live in-world background opacity (0..1); drives the quad's transparency.</summary>
    public float BackgroundOpacity => _content?.BackgroundOpacity ?? 1f;

    /// <summary>Live in-world foreground (text) opacity (0..1).</summary>
    public float ForegroundOpacity => _content?.ForegroundOpacity ?? 1f;

    /// <summary>Live in-world cell-background opacity (0..1).</summary>
    public float CellBackgroundOpacity => _content?.CellBackgroundOpacity ?? 1f;

    /// <summary>Sets the three live opacities (0..1), forwarded to the content renderer. Session-only.</summary>
    public void SetOpacities(float background, float foreground, float cellBackground)
        => _content?.SetOpacities(background, foreground, cellBackground);

    /// <summary>
    ///     Live in-place grid resize: commits the new cols×rows to the record and
    ///     schedules the off-screen texture rebuild. The quad stops drawing at once;
    ///     after the deferred-teardown window (so every scene command buffer that
    ///     recorded the current texture has been submitted) the target is rebuilt
    ///     under a device drain and the quad's descriptor set rewritten. The shell
    ///     session survives — the next off-screen frame sees the new extent and
    ///     resizes the engine grid + PTY itself, so the running app just reflows
    ///     (SIGWINCH-style), exactly like dragging a 2D window. Needs an active ImGui
    ///     frame (measures the font cell). Main thread only.
    /// </summary>
    public bool TrySetGridSize(int cols, int rows)
    {
        if (_disposed || IsFailed || _target == null)
        {
            return false;
        }

        cols = Math.Clamp(cols, 8, 400);
        rows = Math.Clamp(rows, 4, 200);

        // Same extent derivation as the constructor (cell measured on the live
        // ImGui frame's shared atlas, clamped to the GPU texture range).
        var (cellWidth, cellHeight) = InWorldTerminalRenderer.MeasureCell(_config, _catalog, _record.ThemeName);
        int width = Math.Clamp((int)MathF.Ceiling(cols * cellWidth), 256, 4096);
        int height = Math.Clamp((int)MathF.Ceiling(rows * cellHeight), 256, 4096);

        _record.Cols = cols;
        _record.Rows = rows;

        if (width == (int)_target.Extent.Width && height == (int)_target.Extent.Height)
        {
            // The clamped extent is unchanged, and the grid is derived from the
            // texture — nothing to rebuild. Also cancels a superseded pending
            // resize that pointed back at the current extent.
            _pendingResize = null;
            return true;
        }

        // +1: the countdown decrements in Frame() later this same OnAfterGui pass,
        // whereas the teardown queue's first decrement is a frame after Remove —
        // this keeps both drains the same length.
        _pendingResize = (width, height);
        _resizeFramesLeft = InWorldTerminalManager.TeardownDelayFrames + 1;
        return true;
    }

    // Applies a deferred live resize. By now every scene command buffer that could
    // reference the current quad texture has been submitted (RecordDraw skipped while
    // the resize was pending) and our own off-screen submits are synchronous, so a
    // device drain leaves zero live references — the target can be rebuilt and the
    // descriptor set rewritten in place. The ImGui backend is NOT rebuilt: its
    // pipeline, created against the original render pass, remains valid with the
    // recreated (identically-formatted, therefore render-pass-compatible) one.
    private void ApplyResize(int width, int height)
    {
        _renderer.Device.WaitIdle();
        _target!.Resize(width, height);
        _ctx!.Resize(width, height);
        _quad!.RebindTarget();
    }

    /// <inheritdoc/>
    public bool TryRename(string newName)
    {
        string trimmed = newName.Trim();
        if (!TerminalTargetRegistry.IsNameAvailable(trimmed, this))
        {
            return false;
        }

        _record.Name = trimmed;
        return true;
    }

    /// <summary>
    ///     Drops this instance from the target registry immediately (so it leaves the
    ///     theme picker / manager list at once) without freeing its GPU resources —
    ///     the coordinator frees those later via deferred teardown. Idempotent.
    /// </summary>
    public void UnregisterNow()
    {
        if (_registered)
        {
            TerminalTargetRegistry.Unregister(this);
            _registered = false;
        }
    }

    public void Dispose() => Dispose(freeGpu: true);

    /// <summary>
    ///     Disposes the instance. <paramref name="freeGpu"/> = false skips the Vulkan
    ///     resource frees — used only at game shutdown, where the game has already
    ///     destroyed the device and touching it faults (an uncatchable
    ///     AccessViolationException); the process exit reclaims the VRAM. The shell
    ///     session (device-free) is still closed either way.
    /// </summary>
    public void Dispose(bool freeGpu)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterNow();

        if (freeGpu)
        {
            Teardown();
            return;
        }

        // Shutdown: close the shell (no device involved) but leave the GPU graph for
        // the OS to reclaim — the device is gone. freeGpu:false also keeps the
        // renderer's kitty-image textures untouched for the same reason.
        try { _content?.Dispose(freeGpu: false); } catch { /* best-effort */ }
        _content = null;
    }

    private void Teardown()
    {
        // The quad (pipeline + descriptor set referencing the off-screen image)
        // before the target it samples.
        _quad?.Dispose();
        _quad = null;

        // Per-frame renderer drains its fences (waiting out in-flight off-screen
        // work) then frees its command buffers + pool. Must precede the backend so
        // no in-flight command buffer references freed backend resources.
        _perFrame?.Dispose();
        _perFrame = null;

        // Dedicated terminal session + the renderer's kitty-image textures. Closes
        // the shell (safe on the tick thread; the per-frame loop has stopped) and
        // frees the textures — AFTER the fence drain above, so no in-flight
        // off-screen command buffer still samples them.
        _content?.Dispose();
        _content = null;

        // The ImGui backend's teardown mutates the secondary context's IO, so it
        // must run with that context current and before the context is destroyed.
        if (_backend != null)
        {
            var backend = _backend;
            _backend = null;
            if (_ctx != null)
            {
                _ctx.With(backend.Dispose);
            }
            else
            {
                backend.Dispose();
            }
        }

        _ctx?.Dispose();
        _ctx = null;

        _target?.Dispose();
        _target = null;
    }
}
