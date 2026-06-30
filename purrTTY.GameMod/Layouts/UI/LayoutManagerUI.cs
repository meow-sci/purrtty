using Brutal.ImGuiApi;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Layouts;
using purrTTY.GameMod.UI;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.Layouts.UI;

/// <summary>
///     The "Layouts…" dialog: a non-modal, movable window listing saved layouts
///     (Load / Edit / Tear down / Delete), a "Save current as…" form that captures the
///     live terminals, and an editor for a saved layout (rename it, and per terminal:
///     rename, retheme, set the startup command, grid/anchor/geometry, or remove). All
///     actions are user-initiated; nothing is applied automatically. Mirrors the
///     conventions of <see cref="InWorld.UI.InWorldManagerUI"/>.
/// </summary>
public sealed class LayoutManagerUI
{
    private const string WindowId = "Layouts##purrtty_layout_manager";
    private const float DialogWidth = 580f;

    private readonly LayoutManager _manager;

    private bool _visible;
    private bool _requestFocus;

    // "Save current as…" form.
    private readonly ImInputString _saveName = new(64);
    private readonly ImInputString _saveDescription = new(256);

    // Result banner after a load/save.
    private string? _banner;
    private bool _bannerWarn;

    // Edit mode: a loaded layout draft + which entry's form is open.
    private TerminalLayout? _editLayout;
    private string _editOriginalName = "";
    private string? _editError;
    private int _editingEntryIndex = -1;

    private readonly ImInputString _editLayoutName = new(64);
    private readonly ImInputString _editLayoutDesc = new(256);
    private readonly ImInputString _entryName = new(64);
    private readonly ImInputString _entryStartup = new(512);
    private readonly ImInputString _entryVehicle = new(64);
    private readonly ImInputString _entryPart = new(64);
    private readonly ImInputString _entrySubPart = new(64);
    private readonly ImInputString _themeFilter = new(64);

    public LayoutManagerUI(LayoutManager manager) => _manager = manager;

    /// <summary>Shows (and brings to front) the dialog from the menu.</summary>
    public void RequestOpen()
    {
        _visible = true;
        _requestFocus = true;
        _editLayout = null;
        _editingEntryIndex = -1;
        _editError = null;
    }

    /// <summary>Draws the dialog every frame from the main context; a no-op while hidden.</summary>
    public void Draw()
    {
        if (!_visible)
        {
            return;
        }

        if (_requestFocus)
        {
            _requestFocus = false;
            ImGui.SetNextWindowFocus();
        }

        ImGui.SetNextWindowSize(new float2(DialogWidth, 560f), ImGuiCond.FirstUseEver);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(16f, 16f));
        bool open = ImGui.Begin(WindowId, ref _visible, ImGuiWindowFlags.None);
        ImGui.PopStyleVar();

        if (!open)
        {
            ImGui.End();
            return;
        }

        if (_editLayout != null)
        {
            DrawEditView();
        }
        else
        {
            DrawListView();
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button(" Close ##layout_close", new float2(-1f, 0f)))
        {
            _visible = false;
        }

        ImGui.End();
    }

    // ---- List view: saved layouts + save-current ----

    private void DrawListView()
    {
        var names = _manager.Catalog.All();
        ImGui.SeparatorText($"Saved Layouts ( {names.Count} )");

        if (names.Count == 0)
        {
            ImGui.TextDisabled("None yet — set up some terminals and use \"Save current as…\" below.");
        }

        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            bool loaded = _manager.IsLoaded(name);

            ImGui.Text(loaded ? $"{name}   [loaded]" : name);

            if (ImGui.Button($" Load ##layout_load_{i}"))
            {
                SetBanner(_manager.Apply(name));
            }

            ImGui.SameLine(0, 8);
            if (ImGui.Button($" Edit ##layout_editbtn_{i}"))
            {
                BeginEdit(name);
                return; // switched to edit view
            }

            ImGui.SameLine(0, 8);
            if (loaded)
            {
                if (ImGuiWidgets.DestructiveButton($" Tear down ##layout_td_{i}"))
                {
                    int removed = _manager.TeardownSet(name);
                    _banner = $"Tore down {removed} terminal(s) from '{name}'.";
                    _bannerWarn = false;
                }
            }
            else
            {
                ImGui.BeginDisabled();
                ImGui.Button($" Tear down ##layout_td_{i}");
                ImGui.EndDisabled();
            }

            ImGui.SameLine(0, 8);
            if (ImGuiWidgets.DestructiveButton($" Delete ##layout_del_{i}"))
            {
                _manager.Catalog.Delete(name);
                _banner = $"Deleted layout '{name}'." + (loaded ? " (its live terminals were left running.)" : string.Empty);
                _bannerWarn = false;
                break; // names list changed
            }

            ImGui.Spacing();
        }

        if (_banner != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(_bannerWarn ? ImGuiWidgets.WarningColor : ImGuiWidgets.SuccessColor, _banner);
        }

        ImGui.Spacing();
        ImGui.SeparatorText("Save current as…");

        if (ImGuiWidgets.BeginFormTable("##layout_save"))
        {
            ImGuiWidgets.FormRow("Name");
            ImGui.InputText("##layout_save_name", _saveName, ImGuiInputTextFlags.None);
            _saveName.EvaluateLength();

            ImGuiWidgets.FormRow("Description");
            ImGui.InputText("##layout_save_desc", _saveDescription, ImGuiInputTextFlags.None);

            ImGuiWidgets.EndFormTable();
        }

        string saveName = _saveName.ToString().Trim();
        bool canSave = saveName.Length > 0;
        if (!canSave)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Save current terminals ##layout_savebtn", new float2(-1f, 0f)) && canSave)
        {
            bool overwrite = _manager.Catalog.Exists(saveName);
            _manager.CaptureCurrentAs(saveName, _saveDescription.ToString());
            _banner = overwrite ? $"Updated layout '{saveName}'." : $"Saved layout '{saveName}'.";
            _bannerWarn = false;
            _saveName.Clear();
            _saveDescription.Clear();
        }

        if (!canSave)
        {
            ImGui.EndDisabled();
        }

        if (canSave && _manager.Catalog.Exists(saveName))
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiWidgets.WarningColor, $"A layout named '{saveName}' exists — saving overwrites it.");
        }
    }

    private void SetBanner(LayoutApplyResult result)
    {
        _bannerWarn = result.Skipped.Count > 0;
        _banner = result.Skipped.Count > 0
            ? $"Loaded {result.Created}, skipped {result.Skipped.Count} (name collisions / failures) — see log."
            : $"Loaded {result.Created} terminal(s).";
    }

    // ---- Edit view: a saved layout's header + entries ----

    private void BeginEdit(string name)
    {
        var layout = _manager.Catalog.Load(name);
        if (layout is null)
        {
            _banner = $"Could not load layout '{name}'.";
            _bannerWarn = true;
            return;
        }

        _editLayout = layout;
        _editOriginalName = name;
        _editLayoutName.SetValue(layout.Header.Name);
        _editLayoutDesc.SetValue(layout.Header.Description ?? string.Empty);
        _editingEntryIndex = -1;
        _editError = null;
    }

    private void DrawEditView()
    {
        var layout = _editLayout!;
        ImGui.SeparatorText($"Editing layout: {_editOriginalName}");
        ImGui.TextDisabled("Edits the saved file only; live terminals are untouched until the layout is loaded again.");

        if (ImGuiWidgets.BeginFormTable("##layout_edit_hdr"))
        {
            ImGuiWidgets.FormRow("Layout name");
            ImGui.InputText("##layout_edit_name", _editLayoutName, ImGuiInputTextFlags.None);
            _editLayoutName.EvaluateLength();

            ImGuiWidgets.FormRow("Description");
            ImGui.InputText("##layout_edit_desc", _editLayoutDesc, ImGuiInputTextFlags.None);

            ImGuiWidgets.EndFormTable();
        }

        ImGui.Spacing();
        ImGui.SeparatorText($"Terminals ( {layout.Terminals.Count} )");

        for (int i = 0; i < layout.Terminals.Count; i++)
        {
            var entry = layout.Terminals[i];
            string kind = entry.Kind == TerminalKind.InWorld ? "in-world" : "window";
            ImGui.Text($"{entry.Name}  ({kind})");

            if (ImGui.Button($" Edit ##entry_edit_{i}"))
            {
                BeginEntryEdit(i);
            }

            ImGui.SameLine(0, 8);
            if (ImGuiWidgets.DestructiveButton($" Remove ##entry_rm_{i}"))
            {
                layout.Terminals.RemoveAt(i);
                _editingEntryIndex = -1;
                _editError = null;
                break; // list changed
            }

            if (_editingEntryIndex == i)
            {
                DrawEntryForm(entry);
            }

            ImGui.Spacing();
        }

        if (_editError != null)
        {
            ImGui.TextColored(ImGuiWidgets.ErrorColor, _editError);
            ImGui.Spacing();
        }

        float avail = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (avail - gap) / 2f;

        if (ImGui.Button(" Save ##layout_edit_save", new float2(buttonWidth, 0f)))
        {
            SaveEdit();
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button(" Cancel ##layout_edit_cancel", new float2(buttonWidth, 0f)))
        {
            _editLayout = null;
            _editingEntryIndex = -1;
            _editError = null;
        }
    }

    private void BeginEntryEdit(int index)
    {
        _editingEntryIndex = index;
        var e = _editLayout!.Terminals[index];
        _entryName.SetValue(e.Name);
        _entryStartup.SetValue(e.Shell.StartupCommand ?? string.Empty);
        _entryVehicle.SetValue(e.VehicleId ?? string.Empty);
        _entryPart.SetValue(e.PartId ?? string.Empty);
        _entrySubPart.SetValue(e.SubPartId ?? string.Empty);
        _editError = null;
    }

    private void DrawEntryForm(TerminalEntry e)
    {
        ImGui.Indent();
        if (ImGuiWidgets.BeginFormTable($"##entry_form_{_editingEntryIndex}"))
        {
            ImGuiWidgets.FormRow("Name");
            ImGui.InputText("##entry_name", _entryName, ImGuiInputTextFlags.None);
            _entryName.EvaluateLength();

            ImGuiWidgets.FormRow("Theme");
            string? theme = e.Theme;
            if (DrawThemePicker("##entry_theme", ref theme))
            {
                e.Theme = theme;
            }

            ImGuiWidgets.FormRow("Startup command");
            ImGui.InputText("##entry_startup", _entryStartup, ImGuiInputTextFlags.None);

            if (e.Kind == TerminalKind.InWorld)
            {
                int cols = e.Cols ?? 100;
                ImGuiWidgets.FormRow("Columns");
                if (ImGui.DragInt("##entry_cols", ref cols, 0.5f, 8, 400, "%d"))
                {
                    e.Cols = cols;
                }

                int rows = e.Rows ?? 30;
                ImGuiWidgets.FormRow("Rows");
                if (ImGui.DragInt("##entry_rows", ref rows, 0.5f, 4, 200, "%d"))
                {
                    e.Rows = rows;
                }

                ImGuiWidgets.FormRow("Vehicle id");
                ImGui.InputText("##entry_veh", _entryVehicle, ImGuiInputTextFlags.None);
                ImGuiWidgets.FormRow("Part id");
                ImGui.InputText("##entry_part", _entryPart, ImGuiInputTextFlags.None);
                ImGuiWidgets.FormRow("Sub-part id");
                ImGui.InputText("##entry_subpart", _entrySubPart, ImGuiInputTextFlags.None);
            }
            else
            {
                DragRow("Pos X (px)", "##entry_px", e.PosX ?? 0f, v => e.PosX = v, 1f, 0f, 8000f, "%.0f");
                DragRow("Pos Y (px)", "##entry_py", e.PosY ?? 0f, v => e.PosY = v, 1f, 0f, 8000f, "%.0f");
                DragRow("Width (px)", "##entry_w", e.Width ?? 880f, v => e.Width = v, 1f, 80f, 8000f, "%.0f");
                DragRow("Height (px)", "##entry_h", e.Height ?? 520f, v => e.Height = v, 1f, 60f, 8000f, "%.0f");
            }

            ImGuiWidgets.EndFormTable();
        }

        if (e.Kind == TerminalKind.InWorld)
        {
            ImGui.TextDisabled("Tip: tune offset/rotation/size live in \"In-World Terminals…\", then re-save.");
        }

        if (ImGui.Button(" Done ##entry_done", new float2(-1f, 0f)))
        {
            ApplyEntryEdit(e);
        }

        ImGui.Unindent();
    }

    private void ApplyEntryEdit(TerminalEntry e)
    {
        string newName = _entryName.ToString().Trim();
        if (newName.Length == 0)
        {
            _editError = "Terminal name cannot be blank.";
            return;
        }

        // Enforce uniqueness within the layout (a duplicate would collide-skip on load).
        for (int i = 0; i < _editLayout!.Terminals.Count; i++)
        {
            if (i != _editingEntryIndex
                && _editLayout.Terminals[i].Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
            {
                _editError = $"Another terminal in this layout is named '{newName}'.";
                return;
            }
        }

        e.Name = newName;
        e.Shell.StartupCommand = NullIfEmpty(_entryStartup.ToString());
        if (e.Kind == TerminalKind.InWorld)
        {
            e.VehicleId = _entryVehicle.ToString().Trim();
            e.PartId = _entryPart.ToString().Trim();
            e.SubPartId = _entrySubPart.ToString().Trim();
        }

        _editError = null;
        _editingEntryIndex = -1;
    }

    private void SaveEdit()
    {
        var layout = _editLayout!;
        string newName = _editLayoutName.ToString().Trim();
        if (newName.Length == 0)
        {
            _editError = "Layout name cannot be blank.";
            return;
        }

        // Renaming onto a different existing layout would clobber it.
        if (!newName.Equals(_editOriginalName, StringComparison.OrdinalIgnoreCase) && _manager.Catalog.Exists(newName))
        {
            _editError = $"A layout named '{newName}' already exists.";
            return;
        }

        layout.Header.Name = newName;
        layout.Header.Description = NullIfEmpty(_editLayoutDesc.ToString());
        _manager.Catalog.Save(layout);

        if (!newName.Equals(_editOriginalName, StringComparison.OrdinalIgnoreCase))
        {
            _manager.Catalog.Delete(_editOriginalName);
        }

        _banner = $"Saved layout '{newName}'.";
        _bannerWarn = false;
        _editLayout = null;
        _editingEntryIndex = -1;
        _editError = null;
    }

    private bool DrawThemePicker(string id, ref string? selected)
    {
        var items = new List<(string Key, string Label)>();
        foreach (var theme in _manager.Themes.BuiltInThemes)
        {
            items.Add((theme.Name, theme.Name));
        }

        foreach (var theme in _manager.Themes.UserThemes)
        {
            items.Add((theme.Name, $"{theme.Name}  (saved)"));
        }

        if (ImGuiWidgets.FilterCombo(id, selected ?? "(default)", _themeFilter, items, out string? picked) && picked != null)
        {
            selected = picked;
            return true;
        }

        return false;
    }

    private static void DragRow(string label, string id, float value, Action<float> set, float speed, float min, float max, string format)
    {
        ImGuiWidgets.FormRow(label);
        float v = value;
        if (ImGui.DragFloat(id, ref v, speed, min, max, format))
        {
            set(v);
        }
    }

    private static string? NullIfEmpty(string value)
    {
        value = value.Trim();
        return value.Length == 0 ? null : value;
    }
}
