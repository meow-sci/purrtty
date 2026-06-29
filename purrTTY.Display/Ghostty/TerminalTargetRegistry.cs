using purrTTY.Display.Theming;

namespace purrTTY.Display.Ghostty;

/// <summary>Which rendering world a <see cref="INamedTerminal"/> belongs to.</summary>
public enum TerminalKind
{
    /// <summary>A 2D ImGui terminal window (sizes its grid to its pane).</summary>
    Window,

    /// <summary>An in-world render-to-texture terminal (fixed grid, drawn on a 3D quad).</summary>
    InWorld,
}

/// <summary>
///     A uniquely-named terminal that a theme — and, where supported, a fixed grid
///     size — can be applied to, regardless of whether it is a 2D window or an
///     in-world quad. This is the addressing seam the theme picker and the in-world
///     manager select targets through, so the two rendering worlds stay independent
///     at the GPU level but share one naming/addressing layer.
/// </summary>
public interface INamedTerminal
{
    /// <summary>Unique (case-insensitive), user-facing identity within the registry.</summary>
    string Name { get; }

    /// <summary>Which rendering world this terminal belongs to.</summary>
    TerminalKind Kind { get; }

    /// <summary>True while this terminal currently owns input focus.</summary>
    bool HasFocus { get; }

    /// <summary>Applies a complete theme bundle (colors + font + opacity + cursor/border/lock) to this terminal.</summary>
    void ApplyTheme(ThemeDefinition theme);

    /// <summary>
    ///     Sets a fixed grid size where the terminal supports one (in-world). 2D
    ///     windows size their grid to the ImGui pane, so they ignore this and return
    ///     false.
    /// </summary>
    bool TrySetGridSize(int cols, int rows);

    /// <summary>
    ///     Renames this terminal if <paramref name="newName"/> is non-blank and not
    ///     taken by another registered terminal. Returns false (keeping the current
    ///     name) otherwise.
    /// </summary>
    bool TryRename(string newName);
}

/// <summary>
///     The process-wide set of live <see cref="INamedTerminal"/>s — 2D windows and
///     in-world instances alike — that the theme picker and management dialogs
///     address by name. Terminals self-register on creation and unregister on
///     disposal.
///     <para>
///         Main-thread only: registration, pruning, focus reads, and enumeration all
///         run on the ImGui/tick thread (window open/close and the menu draw), so it
///         is intentionally lock-free, mirroring the other main-thread statics in
///         this layer.
///     </para>
/// </summary>
public static class TerminalTargetRegistry
{
    private static readonly List<INamedTerminal> Terminals = new();

    /// <summary>All currently-registered terminals, in registration order.</summary>
    public static IReadOnlyList<INamedTerminal> All => Terminals;

    /// <summary>The single focused terminal across all worlds, or null if none has focus.</summary>
    public static INamedTerminal? Focused
    {
        get
        {
            // Indexed loop: read on the draw path; avoid the LINQ enumerator alloc.
            for (int i = 0; i < Terminals.Count; i++)
            {
                if (Terminals[i].HasFocus)
                {
                    return Terminals[i];
                }
            }

            return null;
        }
    }

    /// <summary>
    ///     Registers a terminal. Returns false (and does not add it) when its
    ///     <see cref="INamedTerminal.Name"/> is blank or already taken — callers
    ///     should assign a <see cref="SuggestUniqueName"/> first.
    /// </summary>
    public static bool Register(INamedTerminal terminal)
    {
        if (!IsNameAvailable(terminal.Name, terminal))
        {
            return false;
        }

        if (!Terminals.Contains(terminal))
        {
            Terminals.Add(terminal);
        }

        return true;
    }

    /// <summary>Removes a terminal from the registry; a no-op if it was not registered.</summary>
    public static void Unregister(INamedTerminal terminal) => Terminals.Remove(terminal);

    /// <summary>Finds a registered terminal by name (case-insensitive), or null.</summary>
    public static INamedTerminal? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        for (int i = 0; i < Terminals.Count; i++)
        {
            if (Terminals[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return Terminals[i];
            }
        }

        return null;
    }

    /// <summary>
    ///     True when <paramref name="name"/> is non-blank and not used by any
    ///     registered terminal other than <paramref name="excluding"/> (pass the
    ///     terminal being renamed so it does not clash with itself).
    /// </summary>
    public static bool IsNameAvailable(string? name, INamedTerminal? excluding = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        for (int i = 0; i < Terminals.Count; i++)
        {
            if (!ReferenceEquals(Terminals[i], excluding)
                && Terminals[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Returns <paramref name="baseName"/> if free, else the first available
    ///     "<c>baseName N</c>" (N = 2, 3, …). Used to auto-name 2D windows.
    /// </summary>
    public static string SuggestUniqueName(string baseName)
    {
        if (IsNameAvailable(baseName))
        {
            return baseName;
        }

        for (int i = 2; ; i++)
        {
            string candidate = $"{baseName} {i}";
            if (IsNameAvailable(candidate))
            {
                return candidate;
            }
        }
    }
}
