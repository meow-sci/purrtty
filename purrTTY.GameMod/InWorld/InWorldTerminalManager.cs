using Brutal.ImGuiApi;
using Brutal.VulkanApi;
using KSA;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Theming;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.GameMod.InWorld.UI;
using purrTTY.Logging;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Coordinator for the in-world (render-to-texture) terminal feature: it owns N
///     <see cref="InWorldTerminalInstance"/>s (each a dedicated shell rendered into an
///     off-screen GPU texture and drawn on a world-space quad), arbitrates which one
///     holds input focus, drives every instance's per-frame render, and exposes the
///     static seams the render postfix + game-key gate read.
///     <para>
///         Instances are created/removed via the <see cref="InWorldManagerUI"/> and
///         are session-only (not persisted). A part-mode instance is focused by
///         clicking its quad (nearest hit wins); a billboard is focused from the
///         manager list. <see cref="Initialize"/> is cheap (no GPU); the dev gate
///         (PURRTTY_INWORLD) auto-creates one default instance on the first frame.
///     </para>
/// </summary>
public sealed class InWorldTerminalManager : IDisposable
{
    // Frames to keep a closed instance's GPU resources alive after it stops being
    // drawn: the game's scene command buffer may have recorded its quad this frame,
    // so freeing immediately is a use-after-free (VK_ERROR_DEVICE_LOST). Freed after
    // the recording frame has completed, gated by a device WaitIdle.
    private const int TeardownDelayFrames = 2;

    private readonly List<InWorldTerminalInstance> _instances = new();
    private readonly List<(InWorldTerminalInstance Instance, int FramesLeft)> _pendingTeardown = new();
    private ThemeConfiguration? _config;
    private ThemeCatalog? _catalog;
    private InWorldManagerUI? _ui;
    private SharedQuadResource? _sharedQuad;
    private InWorldTerminalInstance? _focused;
    private bool _pendingDefault;
    private bool _disposed;

    /// <summary>
    ///     Read by the <c>SuperMeshRenderSystem.RenderMainPass</c> postfix to decide
    ///     whether to draw any quad (true while ≥1 instance is live). Flipped on the
    ///     main thread (the same thread the postfix runs on), so no synchronization is
    ///     needed; cleared before GPU teardown so an in-flight postfix can't touch
    ///     freed handles.
    /// </summary>
    public static bool Active;

    /// <summary>The live coordinator the render postfix reaches the instances through.</summary>
    public static InWorldTerminalManager? Instance { get; private set; }

    /// <summary>
    ///     Whether an in-world terminal currently owns keyboard input. Read by the
    ///     game-key gate (Patch01) + the hotkey guard so typing at a quad never leaks
    ///     to vehicle/camera controls. Computed from the focused instance; main-thread
    ///     only.
    /// </summary>
    public static bool IsInputFocused => Instance?._focused != null;

    /// <summary>True while any in-world terminal is live.</summary>
    public bool IsActive => Active;

    /// <summary>The live instances (read by the manager UI + render postfix).</summary>
    public IReadOnlyList<InWorldTerminalInstance> Instances => _instances;

    /// <summary>Shared theme config (used by the manager UI's shell/theme pickers).</summary>
    public ThemeConfiguration? Config => _config;

    /// <summary>Shared theme catalog (used by the manager UI's theme picker).</summary>
    public ThemeCatalog? Catalog => _catalog;

    /// <summary>
    ///     Stores the shared config/catalog and builds the manager UI. Cheap: no GPU
    ///     work happens here. The dev gate defers creating one default instance to the
    ///     first <see cref="OnAfterGui"/> (building measures the font cell, which needs
    ///     an active ImGui frame). Call from <c>OnFullyLoaded</c>.
    /// </summary>
    public void Initialize(ThemeConfiguration config, ThemeCatalog catalog)
    {
        _config = config;
        _catalog = catalog;
        _ui = new InWorldManagerUI(this);
        Instance = this;

        if (IsDevGateEnabled())
        {
            _pendingDefault = true;
        }
    }

    /// <summary>Menu action: open the in-world terminal manager dialog.</summary>
    public void OpenManager() => _ui?.RequestOpen();

    /// <summary>
    ///     Builds a new in-world terminal from <paramref name="record"/> (assigning a
    ///     registry-unique name first) and starts drawing it. Returns the instance, or
    ///     null on build failure (logged; never crashes the game). Call with an ImGui
    ///     frame active.
    /// </summary>
    public InWorldTerminalInstance? Create(InWorldTerminalRecord record)
    {
        if (_disposed || _config == null || _catalog == null)
        {
            return null;
        }

        // Build the shared quad pipeline/geometry once (identical across instances);
        // kept for the coordinator's lifetime and reused by every instance.
        if (_sharedQuad == null)
        {
            var renderer = Program.GetRenderer();
            if (renderer == null)
            {
                ModLog.Log.Error("purrTTY in-world: Program.GetRenderer() returned null; cannot create");
                return null;
            }

            try
            {
                _sharedQuad = new SharedQuadResource(renderer);
            }
            catch (Exception ex)
            {
                ModLog.Log.Error($"purrTTY in-world: shared quad build failed ({ex.Message})");
                return null;
            }
        }

        record.Name = TerminalTargetRegistry.SuggestUniqueName(
            string.IsNullOrWhiteSpace(record.Name) ? "In-World" : record.Name);

        try
        {
            var instance = new InWorldTerminalInstance(_config, _catalog, record, _sharedQuad);
            _instances.Add(instance);
            Active = true;
            ModLog.Log.Debug($"purrTTY in-world: created '{record.Name}' ({record.Mode}, {record.Cols}x{record.Rows})");
            return instance;
        }
        catch (Exception ex)
        {
            ModLog.Log.Error($"purrTTY in-world: create failed ({ex.Message})");
            return null;
        }
    }

    /// <summary>
    ///     Closes one in-world terminal: stops drawing it and drops it from the
    ///     registry immediately, but DEFERS freeing its GPU graph (the game's scene
    ///     command buffer may still reference its quad this frame — freeing now would
    ///     be a use-after-free / VK_ERROR_DEVICE_LOST). The free happens in
    ///     <see cref="ProcessPendingTeardown"/> once the recording frame has completed.
    /// </summary>
    public void Remove(InWorldTerminalInstance instance)
    {
        if (!_instances.Remove(instance))
        {
            return; // already detached
        }

        if (ReferenceEquals(_focused, instance))
        {
            _focused = null;
        }

        instance.HasFocus = false;
        instance.UnregisterNow();
        _pendingTeardown.Add((instance, TeardownDelayFrames));

        if (_instances.Count == 0)
        {
            Active = false;
        }
    }

    /// <summary>Gives input focus to a specific instance (the manager list's billboard focus path).</summary>
    public void Focus(InWorldTerminalInstance? instance) => _focused = instance;

    /// <summary>
    ///     Appends every live instance's quad draw to the scene-pass command buffer.
    ///     A per-instance draw failure retires only that instance (the coordinator
    ///     prunes it next frame) rather than disabling all. Called from the render
    ///     postfix (which guards on <see cref="Active"/>).
    /// </summary>
    public void RecordDrawAll(CommandBuffer commandBuffer)
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            var instance = _instances[i];
            if (instance.IsFailed)
            {
                continue;
            }

            try
            {
                instance.RecordDraw(commandBuffer);
            }
            catch (Exception ex)
            {
                instance.MarkFailed();
                ModLog.Log.Error($"purrTTY in-world: quad draw failed for '{instance.Name}', retiring it ({ex.Message})");
            }
        }
    }

    /// <summary>
    ///     Per frame on the main thread (StarMap <c>OnAfterGui</c>): draws the manager
    ///     UI, processes a deferred dev-gate create, prunes failed instances, arbitrates
    ///     focus + input forwarding, and drives each instance's off-screen render.
    /// </summary>
    public void OnAfterGui(double dt)
    {
        // Free any instances whose deferred teardown has come due (closed/retired in
        // a prior frame) before the UI can queue more this frame.
        ProcessPendingTeardown();

        // Manager UI renders in the main context — available even with no instances.
        _ui?.Draw();

        // Deferred dev-gate default (needs an active ImGui frame to measure the cell).
        if (_pendingDefault)
        {
            _pendingDefault = false;
            Create(new InWorldTerminalRecord { Name = "In-World" });
        }

        PruneFailed();

        if (!Active || _instances.Count == 0)
        {
            return;
        }

        // Mutually exclusive with 2D-window focus: if a 2D terminal owns the keyboard
        // this frame, no quad does (prevents double-input).
        if (GhosttyTerminalController.IsAnyTerminalActive)
        {
            _focused = null;
        }

        // Click-to-focus: nearest part-mode quad under the cursor.
        TickPicker();

        var focused = _focused;
        var io = ImGui.GetIO();

        if (focused != null && !io.WantTextInput && focused.Content.ActiveSession is { } session)
        {
            // Shared encoder; no suppression / blink-reset needed for the quad.
            TerminalInputEncoder.ProcessKeyboard(session, io);
        }

        // App-mouse: map the cursor's quad hit to a cell and forward press/drag/wheel
        // so in-world TUIs (vim, htop) respond to clicks. Part mode only.
        if (focused != null && !focused.IsBillboard)
        {
            float2? hitUv = null;
            if (focused.TryRaycast(Cursor.InputRay, out _, out float2 uv))
            {
                hitUv = uv;
            }

            focused.Content.ProcessMouse(hitUv, io);
        }

        // Only the focused instance shows a focused (solid) cursor.
        for (int i = 0; i < _instances.Count; i++)
        {
            _instances[i].HasFocus = ReferenceEquals(_instances[i], focused);
        }

        for (int i = 0; i < _instances.Count; i++)
        {
            var instance = _instances[i];
            if (instance.IsFailed)
            {
                continue;
            }

            try
            {
                instance.Frame(dt);
            }
            catch (Exception ex)
            {
                instance.MarkFailed();
                ModLog.Log.Error($"purrTTY in-world: per-frame render failed for '{instance.Name}', retiring it ({ex.Message})");
            }
        }
    }

    /// <summary>
    ///     Click-to-focus over all part-mode quads: ray-tests each, focuses the nearest
    ///     hit; a click in empty world space (not over an ImGui widget) clears focus.
    ///     Billboard instances have no ego-space ray and are focused from the manager
    ///     list. Escape is deliberately NOT a release key — it must reach the shell.
    /// </summary>
    private void TickPicker()
    {
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            return;
        }

        InWorldTerminalInstance? nearest = null;
        double nearestT = double.MaxValue;
        for (int i = 0; i < _instances.Count; i++)
        {
            var instance = _instances[i];
            if (instance.IsBillboard || instance.IsFailed)
            {
                continue;
            }

            if (instance.TryRaycast(Cursor.InputRay, out double t, out _) && t < nearestT)
            {
                nearestT = t;
                nearest = instance;
            }
        }

        if (nearest != null)
        {
            _focused = nearest;
        }
        else if (!ImGui.GetIO().WantCaptureMouse)
        {
            _focused = null;
        }
    }

    private void PruneFailed()
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            if (_instances[i].IsFailed)
            {
                Remove(_instances[i]);
            }
        }
    }

    /// <summary>
    ///     Frees instances whose deferred-teardown delay has elapsed: by now the frame
    ///     that recorded their quad has been submitted, so a device WaitIdle guarantees
    ///     the GPU is done with them before the resources are released.
    /// </summary>
    private void ProcessPendingTeardown()
    {
        if (_pendingTeardown.Count == 0)
        {
            return;
        }

        for (int i = _pendingTeardown.Count - 1; i >= 0; i--)
        {
            var (instance, framesLeft) = _pendingTeardown[i];
            framesLeft--;
            if (framesLeft > 0)
            {
                _pendingTeardown[i] = (instance, framesLeft);
                continue;
            }

            WaitDeviceIdle();
            instance.Dispose();
            _pendingTeardown.RemoveAt(i);
        }
    }

    // Blocks until the GPU has finished all in-flight work (incl. the game's scene
    // pass that recorded our quad), so freeing an instance's GPU graph can't race a
    // command buffer that still references it.
    private static void WaitDeviceIdle()
    {
        try
        {
            Program.GetRenderer()?.Device.WaitIdle();
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY in-world: device WaitIdle before teardown failed ({ex.Message})");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Stop the postfix BEFORE freeing GPU resources.
        Active = false;
        _focused = null;
        Instance = null;

        // Wait the device idle so freeing can't race any final in-flight frame, then
        // free both live and pending-teardown instances (each drains its fences and
        // frees its GPU graph + shell, and unregisters from the target registry).
        WaitDeviceIdle();

        for (int i = 0; i < _instances.Count; i++)
        {
            _instances[i].Dispose();
        }

        _instances.Clear();

        for (int i = 0; i < _pendingTeardown.Count; i++)
        {
            _pendingTeardown[i].Instance.Dispose();
        }

        _pendingTeardown.Clear();

        // The shared quad pipeline/geometry outlives every instance (their RecordDraw
        // referenced it); free it last, after all instances are gone.
        _sharedQuad?.Dispose();
        _sharedQuad = null;
    }

    // Dev convenience: auto-create one default in-world terminal on load when
    // PURRTTY_INWORLD is set. The manager dialog is the user-facing path.
    private static bool IsDevGateEnabled()
    {
        var v = Environment.GetEnvironmentVariable("PURRTTY_INWORLD");
        return !string.IsNullOrEmpty(v) &&
               (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase));
    }
}
