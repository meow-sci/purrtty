using Brutal.ImGuiApi;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace purrTTY.GameMod.UI;

/// <summary>
///     Reusable ImGui widgets + layout helpers shared across purrTTY's in-game
///     dialogs, following the KSA mod layout conventions (label/widget tables with
///     <c>NoPadOuterX</c> + uniform cell padding, fixed-width modals).
/// </summary>
public static class ImGuiWidgets
{
    /// <summary>Status text color for errors.</summary>
    public static readonly float4 ErrorColor = new(1f, 0.3f, 0.3f, 1f);

    /// <summary>Status text color for success.</summary>
    public static readonly float4 SuccessColor = new(0.4f, 1f, 0.4f, 1f);

    /// <summary>Status text color for warnings.</summary>
    public static readonly float4 WarningColor = new(1f, 0.8f, 0.2f, 1f);

    /// <summary>
    ///     Pins a modal's WIDTH every frame (height auto-fits) so it never shrinks to
    ///     its content — the fixed-size idiom from the KSA mods. Call immediately
    ///     before <c>BeginPopupModal(..., AlwaysAutoResize)</c>.
    /// </summary>
    public static void SetNextModalWidth(float width)
        => ImGui.SetNextWindowSize(new float2(width, 0f), ImGuiCond.Always);

    /// <summary>
    ///     Begins a 2-column label/widget form table (label gets ¼ width, the widget
    ///     ¾, matching the KSA convention). Returns false if the table could not open
    ///     (then do NOT emit rows or call <see cref="EndFormTable"/>). Pair each
    ///     <see cref="FormRow"/> with exactly one full-width widget.
    /// </summary>
    public static bool BeginFormTable(string id)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        bool open = ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
        if (open)
        {
            ImGui.TableSetupColumn($"{id}_l", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn($"{id}_w", ImGuiTableColumnFlags.WidthStretch, 3f);
        }
        else
        {
            ImGui.PopStyleVar();
        }

        return open;
    }

    /// <summary>Ends a <see cref="BeginFormTable"/>.</summary>
    public static void EndFormTable()
    {
        ImGui.EndTable();
        ImGui.PopStyleVar();
    }

    /// <summary>
    ///     Emits a label cell (frame-aligned) and advances to the full-width widget
    ///     cell. Draw exactly one widget immediately after.
    /// </summary>
    public static void FormRow(string label)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
    }

    /// <summary>
    ///     A combo box whose first row is a live text filter: type to narrow the list
    ///     by case-insensitive substring of each item's label. Returns <c>true</c> on
    ///     the frame an item is chosen (with <paramref name="selectedKey"/> set to that
    ///     item's key); otherwise <c>false</c> and <paramref name="selectedKey"/> is
    ///     null.
    ///     <para>
    ///         The caller owns <paramref name="filter"/> — one persistent
    ///         <see cref="ImInputString"/> per combo — so the typed text survives across
    ///         frames. Two deliberate choices guard the BRUTAL InputText length quirk
    ///         (it only refreshes <see cref="ImInputString.Length"/> on a <i>true</i>
    ///         return): we do <b>not</b> pass <c>EnterReturnsTrue</c>, and we call
    ///         <see cref="ImInputString.EvaluateLength"/> every frame before reading the
    ///         buffer — otherwise the filter would read empty while the user types and
    ///         never narrow. (Same note as TerminalMod.RenderSaveThemeModal.)
    ///     </para>
    /// </summary>
    /// <param name="id">An ImGui id for the combo (conventionally starting with <c>##</c>).</param>
    /// <param name="preview">The collapsed-combo preview text (typically the current selection).</param>
    /// <param name="filter">Caller-owned filter buffer, persisted across frames.</param>
    /// <param name="items">Selectable items as (stable key, display label) pairs.</param>
    /// <param name="selectedKey">The chosen item's key when this returns true; else null.</param>
    public static bool FilterCombo(
        string id,
        string preview,
        ImInputString filter,
        IReadOnlyList<(string Key, string Label)> items,
        out string? selectedKey)
    {
        selectedKey = null;

        if (!ImGui.BeginCombo(id, preview))
        {
            return false;
        }

        // Focus the filter box the frame the popup opens so the user can type at once.
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText($"{id}_filter", filter, ImGuiInputTextFlags.None);
        filter.EvaluateLength();
        string needle = filter.ToString().Trim();

        ImGui.Separator();

        bool chose = false;
        for (int i = 0; i < items.Count; i++)
        {
            (string key, string label) = items[i];
            if (needle.Length > 0 && label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // Disambiguate the ImGui id with the row index so duplicate labels (e.g.
            // parts sharing a template id) never collide.
            if (ImGui.Selectable($"{label}##{id}_{i}", false))
            {
                selectedKey = key;
                chose = true;
            }
        }

        ImGui.EndCombo();

        // Reset the filter once a choice is made so the next open starts from the full
        // list rather than a stale needle.
        if (chose)
        {
            filter.Clear();
        }

        return chose;
    }
}
