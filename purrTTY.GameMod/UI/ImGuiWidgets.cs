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

    /// <summary>
    ///     Like <see cref="BeginFormTable"/> but the label column is a <b>fixed</b> width
    ///     (<paramref name="labelWidth"/>, typically from <see cref="MeasureLabelWidth"/>)
    ///     and the widget column stretches to fill the rest — so several form sections in
    ///     the same dialog line up to one uniform label gutter regardless of the widest
    ///     label in each. Pair each <see cref="FormRow"/> with one full-width widget, and
    ///     close with <see cref="EndFormTable"/>.
    /// </summary>
    public static bool BeginFormTableFixed(string id, float labelWidth)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        bool open = ImGui.BeginTable(id, 2, ImGuiTableFlags.NoPadOuterX);
        if (open)
        {
            ImGui.TableSetupColumn($"{id}_l", ImGuiTableColumnFlags.WidthFixed, labelWidth);
            ImGui.TableSetupColumn($"{id}_w", ImGuiTableColumnFlags.WidthStretch, 1f);
        }
        else
        {
            ImGui.PopStyleVar();
        }

        return open;
    }

    /// <summary>Ends a <see cref="BeginFormTable"/> or <see cref="BeginFormTableFixed"/>.</summary>
    public static void EndFormTable()
    {
        ImGui.EndTable();
        ImGui.PopStyleVar();
    }

    /// <summary>
    ///     Measures the pixel width a fixed label column needs to fit the widest of
    ///     <paramref name="labels"/> in the current font, plus a small gutter. Feed the
    ///     result to <see cref="BeginFormTableFixed"/> to give every section of a dialog
    ///     the same content-fitted label column. Call within an active ImGui frame.
    /// </summary>
    public static float MeasureLabelWidth(IReadOnlyList<string> labels)
    {
        float max = 0f;
        for (int i = 0; i < labels.Count; i++)
        {
            float w = ImGui.CalcTextSize(labels[i]).X;
            if (w > max)
            {
                max = w;
            }
        }

        // Pad past the widest label so it never clips and leaves a gap before the widget
        // column (covers the cell's left/right padding plus a little breathing room).
        return max + 16f;
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
    ///     A red, clearly-destructive button (KSA "Destroy"-style). Returns true when
    ///     clicked. Pass <paramref name="size"/> to size it (e.g. a half-width footer
    ///     button); omit for auto-fit. Must run with an active ImGui frame (uses the
    ///     style alpha).
    /// </summary>
    public static bool DestructiveButton(string label, float2? size = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(new float4(0.70f, 0.18f, 0.18f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetColorU32(new float4(0.85f, 0.25f, 0.25f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.GetColorU32(new float4(0.60f, 0.12f, 0.12f, 1f)));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(new float4(0.97f, 0.97f, 0.97f, 1f)));
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        return clicked;
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
