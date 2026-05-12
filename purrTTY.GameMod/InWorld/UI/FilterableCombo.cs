using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;

namespace purrTTY.GameMod.InWorld.UI;

/// <summary>
///     Reusable filterable combobox helper. Wraps <see cref="ImGui.BeginCombo"/>
///     with an in-popup <see cref="ImGui.InputTextWithHint"/> filter that the
///     popup auto-focuses on first appearance, plus selectable rows filtered by
///     case-insensitive substring match. The filter buffer is owned by the
///     caller so each call site has its own state.
/// </summary>
internal static class FilterableCombo
{
    /// <summary>
    ///     Renders the combo and writes the new selection back through
    ///     <paramref name="selected"/>. <paramref name="selected"/> may be -1 to
    ///     mean "no selection". Returns true exactly on the frame the user
    ///     clicked a row, so callers can react to commit.
    /// </summary>
    public static bool Render<T>(
        string label,
        IReadOnlyList<T> items,
        Func<T, string> displayOf,
        ref int selected,
        ImInputString filter)
    {
        // Preview text: show the currently selected item, or a placeholder.
        // Pre-format to a plain string so the implicit ImString conversion
        // does not bind through the InterpolatedStringHandler path.
        string preview;
        if (selected >= 0 && selected < items.Count)
        {
            preview = displayOf(items[selected]);
        }
        else
        {
            preview = items.Count == 0 ? "(empty)" : "(none)";
        }

        bool changed = false;

        if (!ImGui.BeginCombo(label, preview))
        {
            return false;
        }

        try
        {
            // Auto-focus the filter on first appearance and reset stale text
            // from a previous open so the user always types into a clean box.
            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere(0);
                filter.Clear();
            }

            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##filter", "filter...", filter);
            ImGui.Separator();

            if (items.Count == 0)
            {
                ImGui.BeginDisabled();
                ImGui.Selectable("(empty)", false);
                ImGui.EndDisabled();
                return false;
            }

            string filterStr = filter.Value.ToString();
            bool hasFilter = !string.IsNullOrEmpty(filterStr);

            for (int i = 0; i < items.Count; i++)
            {
                string display = displayOf(items[i]);
                if (hasFilter && display.IndexOf(filterStr, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                bool isSelected = (i == selected);
                // Suffix the item index so identical display strings still
                // produce unique ImGui IDs within the combo body.
                string rowLabel = display + "##fc_row_" + i;
                if (ImGui.Selectable(rowLabel, isSelected))
                {
                    selected = i;
                    changed = true;
                }
            }
        }
        finally
        {
            ImGui.EndCombo();
        }

        return changed;
    }
}
