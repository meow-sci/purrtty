using Brutal.ImGuiApi;

namespace purrTTY.GameMod.UI;

/// <summary>
///     Reusable ImGui widgets shared across purrTTY's in-game dialogs (theme picker,
///     terminal-target picker, in-world shell picker).
/// </summary>
public static class ImGuiWidgets
{
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
