using System;
using Brutal.ImGuiApi;
using Brutal.Pointers;
using float2 = Brutal.Numerics.float2;
using int2 = Brutal.Numerics.int2;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Owns a secondary <see cref="ImGuiContextPtr"/> that runs the in-world
///     terminal UI in isolation from the game's main ImGui context.
///     <para>
///         Phase 3: the context is constructed and torn down here, but no frame
///         is yet driven on it. Per-frame work happens in Phase 5 via
///         <see cref="With(System.Action)"/>.
///     </para>
///     <para>
///         The font atlas is shared with the main context so we do not duplicate
///         font upload memory and so the same <c>ImFontPtr</c> handles continue
///         to work across both contexts. The atlas has already been built by
///         <c>PurrTTYFontManager.LoadFonts()</c> on the main context; we do not
///         rebuild it here.
///     </para>
///     <para>
///         Threading: every method assumes it is called on the main thread (the
///         same thread that owns the game's ImGui context) and may be invoked
///         while another context is current. All mutations save the prior
///         context, switch, mutate, and restore in a <c>try/finally</c>.
///     </para>
/// </summary>
public sealed class OffscreenContext : IDisposable
{
    /// <summary>
    ///     The native ImGui context handle. Becomes <see cref="ImGuiContextPtr.IsNull"/>
    ///     after <see cref="Dispose"/>.
    /// </summary>
    public ImGuiContextPtr Native { get; private set; }

    /// <summary>
    ///     Logical display size of the off-screen viewport in pixels.
    /// </summary>
    public int2 Size { get; private set; }

    private bool _disposed;

    public OffscreenContext(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Invalid OffscreenContext size: {width}x{height}");
        }

        // CreateContext(ImFontAtlasPtr) is the only overload in this binding
        // (Brutal.ImGuiApi.ImGui.cs:5401). The default-valued parameter means
        // we can pass either the main context's atlas (to share) or `default`
        // (to let ImGui allocate a fresh one). We share to avoid a second font
        // upload and to keep ImFontPtr handles valid across both contexts.
        var prevCtx = ImGui.GetCurrentContext();
        try
        {
            ImFontAtlasPtr sharedAtlas = default;
            if (!prevCtx.IsNull())
            {
                sharedAtlas = ImGui.GetIO().Fonts;
            }

            Native = ImGui.CreateContext(sharedAtlas);
            ImGui.SetCurrentContext(Native);

            var io = ImGui.GetIO();
            io.DisplaySize    = new float2(width, height);
            io.DeltaTime      = 1f / 60f;
            io.IniFilename    = default;                       // Ptr<byte>: null = no .ini persistence
            io.MouseDrawCursor = false;
            // Keep keyboard nav available in the secondary context even before
            // we wire up real focus signals in Phase 7.
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;

            Size = new int2(width, height);
        }
        finally
        {
            // Always restore the prior context, even if CreateContext or any IO
            // mutation throws.
            ImGui.SetCurrentContext(prevCtx);
        }
    }

    /// <summary>
    ///     Runs <paramref name="action"/> with this context current and restores
    ///     whatever context was current on entry — even if <paramref name="action"/>
    ///     throws. This is the only sanctioned way for callers to interact with
    ///     the secondary context's ImGui state.
    /// </summary>
    public void With(Action action)
    {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (_disposed) throw new ObjectDisposedException(nameof(OffscreenContext));

        var prev = ImGui.GetCurrentContext();
        ImGui.SetCurrentContext(Native);
        try
        {
            action();
        }
        finally
        {
            ImGui.SetCurrentContext(prev);
        }
    }

    /// <summary>
    ///     Updates the secondary context's <c>DisplaySize</c>. Safe to call from
    ///     any context; saves and restores the prior current context.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException($"Invalid OffscreenContext size: {width}x{height}");
        }

        With(() =>
        {
            ImGui.GetIO().DisplaySize = new float2(width, height);
            Size = new int2(width, height);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Native.IsNull()) return;

        // Never destroy a context that is currently bound. Detach to "no
        // current context" first, then destroy. Callers must not be inside a
        // With() scope when Dispose runs (documented contract).
        var ours = Native;
        Native = default;

        ImGui.SetCurrentContext(default);
        ImGui.DestroyContext(ours);
    }
}
