using Brutal.VulkanApi;
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

        try
        {
            var renderer = Program.GetRenderer()
                ?? throw new InvalidOperationException("Program.GetRenderer() returned null");

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
    public void Frame(double dt) => _perFrame!.Frame(dt);

    /// <summary>Appends this instance's quad draw to the scene-pass command buffer.</summary>
    public void RecordDraw(CommandBuffer commandBuffer) => _quad!.RecordDraw(commandBuffer);

    /// <summary>Ray-tests the quad in part mode; see <see cref="InWorldQuad.TryRaycast"/>.</summary>
    public bool TryRaycast(Ray ray, out double t, out float2 uv) => _quad!.TryRaycast(ray, out t, out uv);

    /// <summary>Retires this instance after a GPU draw/frame failure; the coordinator prunes + disposes it.</summary>
    public void MarkFailed() => IsFailed = true;

    /// <summary>Applies a theme bundle live (colors + font + opacity + cursor).</summary>
    public void ApplyTheme(ThemeDefinition theme)
    {
        _record.ThemeName = theme.Name;
        _content?.ApplyTheme(theme);
    }

    /// <summary>
    ///     A live grid resize needs the off-screen texture rebuilt; not yet supported
    ///     (the size is fixed at creation). Returns false so callers fall back.
    /// </summary>
    public bool TrySetGridSize(int cols, int rows) => false;

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnregisterNow();
        Teardown();
    }

    private void Teardown()
    {
        // Dedicated terminal session: closes the shell + its native surface. Safe on
        // the tick thread; the per-frame loop has already stopped.
        _content?.Dispose();
        _content = null;

        // The quad (pipeline + descriptor set referencing the off-screen image)
        // before the target it samples.
        _quad?.Dispose();
        _quad = null;

        // Per-frame renderer drains its fences (waiting out in-flight off-screen
        // work) then frees its command buffers + pool. Must precede the backend so
        // no in-flight command buffer references freed backend resources.
        _perFrame?.Dispose();
        _perFrame = null;

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
