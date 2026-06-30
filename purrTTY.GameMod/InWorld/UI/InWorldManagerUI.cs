using Brutal.ImGuiApi;
using KSA;
using purrTTY.Core.Terminal;
using purrTTY.CustomShells;
using purrTTY.Display.Ghostty;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.GameMod.UI;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld.UI;

/// <summary>
///     The in-world terminal manager: a fixed-width modal that lists the live in-world
///     terminals (per-instance focus / configure / close), a create form (name, shell,
///     grid size, anchor mode, theme), and a configure form for an existing instance
///     (live theme + placement, plus a recreate-based grid resize). Renders in the
///     <b>main</b> ImGui context; placement and theme edits are live, a size change
///     rebuilds the off-screen texture (restarting the shell). Table-based label/widget
///     layout per the KSA mod conventions.
/// </summary>
public sealed class InWorldManagerUI
{
    private const string PopupId = "In-World Terminals##purrtty_inworld_manager";
    private const float DialogWidth = 600f;

    private readonly InWorldTerminalManager _manager;

    private readonly ImInputString _nameInput = new(64);
    private readonly ImInputString _startupCommandInput = new(512);
    private readonly ImInputString _shellFilter = new(64);
    private readonly ImInputString _themeFilter = new(64);
    private readonly ImInputString _vehicleFilter = new(64);
    private readonly ImInputString _partFilter = new(64);
    private readonly ImInputString _subPartFilter = new(64);

    private bool _visible;
    private bool _requestFocus;
    private int _draftCols = 100;
    private int _draftRows = 30;
    private string _draftMode = InWorldTerminalRecord.ModePart;
    private string _draftVehicleId = "";
    private string _draftPartId = "";
    private string _draftSubPartId = "";
    private (string Label, ProcessLaunchOptions Options)? _draftShell;
    private string? _draftThemeName;
    private string? _createError;

    // Configure-an-existing-instance state.
    private InWorldTerminalInstance? _configuring;
    private int _resizeCols;
    private int _resizeRows;

    public InWorldManagerUI(InWorldTerminalManager manager) => _manager = manager;

    /// <summary>Shows (and brings to front) the manager window (from the menu).</summary>
    public void RequestOpen()
    {
        _visible = true;
        _requestFocus = true;
        _configuring = null;
        _createError = null;
    }

    /// <summary>
    ///     Draws the manager as a non-modal, movable window (the game stays interactive
    ///     behind it). Call every frame from the main context; a no-op while hidden.
    /// </summary>
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
        bool visible = ImGui.Begin(PopupId, ref _visible, ImGuiWindowFlags.None);
        ImGui.PopStyleVar();

        if (!visible)
        {
            ImGui.End();
            return;
        }

        DrawInstanceList();

        // Drop a stale placement reference if that instance was closed or retired.
        if (_configuring != null && (_configuring.IsFailed || !InstanceIsLive(_configuring)))
        {
            _configuring = null;
        }

        if (_configuring != null)
        {
            DrawConfigure(_configuring);
        }
        else
        {
            DrawCreateForm();
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button(" Close ##iw_close", new float2(-1f, 0f)))
        {
            _visible = false;
        }

        ImGui.End();
    }

    private void DrawInstanceList()
    {
        var instances = _manager.Instances;
        ImGui.SeparatorText($"In-World Terminals ( {instances.Count} )");

        if (instances.Count == 0)
        {
            ImGui.TextDisabled("None yet — create one below.");
            return;
        }

        for (int i = 0; i < instances.Count; i++)
        {
            var instance = instances[i];
            var r = instance.Record;
            string focus = instance.HasFocus ? "  [focused]" : string.Empty;
            ImGui.Text($"{instance.Name}  ({(r.IsBillboard ? "billboard" : "part")}, {r.Cols}x{r.Rows}){focus}");

            if (ImGui.Button($" Focus ##iw_focus_{i}"))
            {
                _manager.Focus(instance);
            }

            ImGui.SameLine(0, 8);
            if (ImGui.Button($" Configure ##iw_cfg_{i}"))
            {
                BeginConfigure(instance);
            }

            ImGui.SameLine(0, 8);
            if (ImGuiWidgets.DestructiveButton($" Destroy ##iw_remove_{i}"))
            {
                _manager.Remove(instance);
                return; // the list changed under us; resume next frame
            }

            ImGui.Spacing();
        }
    }

    private void DrawCreateForm()
    {
        ImGui.SeparatorText("New terminal");

        var shells = BuildShellList();
        var shellItems = new List<(string Key, string Label)>(shells.Count);
        for (int i = 0; i < shells.Count; i++)
        {
            shellItems.Add((i.ToString(), shells[i].Label));
        }

        string name = _nameInput.ToString();

        if (ImGuiWidgets.BeginFormTable("##iw_create"))
        {
            ImGuiWidgets.FormRow("Name");
            ImGui.InputText("##iw_name", _nameInput, ImGuiInputTextFlags.None);
            _nameInput.EvaluateLength();
            name = _nameInput.ToString().Trim();

            ImGuiWidgets.FormRow("Shell");
            if (ImGuiWidgets.FilterCombo("##iw_shell", _draftShell?.Label ?? "Default Shell", _shellFilter, shellItems, out string? shellKey)
                && shellKey != null && int.TryParse(shellKey, out int shellIdx) && shellIdx < shells.Count)
            {
                _draftShell = shells[shellIdx];
            }

            // Optional command auto-run as stdin once the shell starts (e.g. a gatOS flight-computer TUI).
            ImGuiWidgets.FormRow("Startup command");
            ImGui.InputText("##iw_startup", _startupCommandInput, ImGuiInputTextFlags.None);

            ImGuiWidgets.FormRow("Columns");
            ImGui.DragInt("##iw_cols", ref _draftCols, 0.5f, 8, 400, "%d");

            ImGuiWidgets.FormRow("Rows");
            ImGui.DragInt("##iw_rows", ref _draftRows, 0.5f, 4, 200, "%d");

            ImGuiWidgets.FormRow("Theme");
            DrawThemePicker("##iw_create_theme", ref _draftThemeName);

            ImGuiWidgets.EndFormTable();
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text("Anchor");
        ImGui.SameLine(0, 12);
        if (ImGui.RadioButton("Vehicle Part##iw_mode_part", _draftMode == InWorldTerminalRecord.ModePart))
        {
            _draftMode = InWorldTerminalRecord.ModePart;
        }

        ImGui.SameLine(0, 16);
        if (ImGui.RadioButton("Camera Billboard##iw_mode_bb", _draftMode == InWorldTerminalRecord.ModeBillboard))
        {
            _draftMode = InWorldTerminalRecord.ModeBillboard;
        }

        // Part mode: pick the anchor vehicle + part at creation time.
        if (_draftMode == InWorldTerminalRecord.ModePart && ImGuiWidgets.BeginFormTable("##iw_create_anchor"))
        {
            DrawVehiclePartPicker(_draftVehicleId, _draftPartId, _draftSubPartId,
                v => _draftVehicleId = v, p => _draftPartId = p, sp => _draftSubPartId = sp);
            ImGuiWidgets.EndFormTable();
        }

        ImGui.Spacing();

        bool canCreate = name.Length > 0 && TerminalTargetRegistry.IsNameAvailable(name);
        if (!canCreate)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Create terminal ##iw_createbtn", new float2(-1f, 0f)) && canCreate)
        {
            CreateFromDraft(name);
        }

        if (!canCreate)
        {
            ImGui.EndDisabled();
        }

        if (name.Length > 0 && !TerminalTargetRegistry.IsNameAvailable(name))
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiWidgets.WarningColor, $"A terminal named '{name}' already exists.");
        }

        if (!string.IsNullOrEmpty(_createError))
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiWidgets.ErrorColor, _createError);
        }
    }

    private void DrawConfigure(InWorldTerminalInstance instance)
    {
        var r = instance.Record;
        ImGui.SeparatorText($"Configure — {instance.Name}");

        // Theme: applied live (colors + font reflow within the current texture).
        if (ImGuiWidgets.BeginFormTable("##iw_cfg_theme"))
        {
            ImGuiWidgets.FormRow("Theme");
            string? themeName = r.ThemeName;
            if (DrawThemePicker("##iw_cfg_theme_combo", ref themeName) && themeName != null
                && _manager.Catalog?.Find(themeName) is { } def)
            {
                instance.ApplyTheme(def);
            }

            ImGuiWidgets.FormRow("Columns");
            ImGui.DragInt("##iw_cfg_cols", ref _resizeCols, 0.5f, 8, 400, "%d");

            ImGuiWidgets.FormRow("Rows");
            ImGui.DragInt("##iw_cfg_rows", ref _resizeRows, 0.5f, 4, 200, "%d");

            ImGuiWidgets.EndFormTable();
        }

        bool sizeChanged = _resizeCols != r.Cols || _resizeRows != r.Rows;
        if (!sizeChanged)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Apply size (restarts shell) ##iw_resize") && sizeChanged)
        {
            var newRecord = r.Clone();
            newRecord.Cols = Math.Clamp(_resizeCols, 8, 400);
            newRecord.Rows = Math.Clamp(_resizeRows, 4, 200);
            var created = _manager.Recreate(instance, newRecord);
            if (created != null)
            {
                BeginConfigure(created);
            }
        }

        if (!sizeChanged)
        {
            ImGui.EndDisabled();
        }

        // Opacity: live, per-pixel transparency baked into the off-screen texture and
        // composited by the quad over the 3D scene. Reuses the theme's three opacities
        // (Background drives the see-through; Foreground/Cell dim text/cell backgrounds).
        // Session-only — not persisted unless captured into a named theme via "Theme…".
        ImGui.SeparatorText("Opacity");
        if (ImGuiWidgets.BeginFormTable("##iw_cfg_opacity"))
        {
            float bg = instance.BackgroundOpacity;
            float fg = instance.ForegroundOpacity;
            float cell = instance.CellBackgroundOpacity;
            bool changed = false;

            ImGuiWidgets.FormRow("Background");
            int bgPct = (int)MathF.Round(bg * 100f);
            if (ImGui.SliderInt("##iw_cfg_op_bg", ref bgPct, 0, 100, "%d%%"))
            {
                bg = Math.Clamp(bgPct, 0, 100) / 100f;
                changed = true;
            }

            ImGuiWidgets.FormRow("Foreground");
            int fgPct = (int)MathF.Round(fg * 100f);
            if (ImGui.SliderInt("##iw_cfg_op_fg", ref fgPct, 0, 100, "%d%%"))
            {
                fg = Math.Clamp(fgPct, 0, 100) / 100f;
                changed = true;
            }

            ImGuiWidgets.FormRow("Cell background");
            int cellPct = (int)MathF.Round(cell * 100f);
            if (ImGui.SliderInt("##iw_cfg_op_cell", ref cellPct, 0, 100, "%d%%"))
            {
                cell = Math.Clamp(cellPct, 0, 100) / 100f;
                changed = true;
            }

            if (changed)
            {
                instance.SetOpacities(bg, fg, cell);
            }

            ImGuiWidgets.EndFormTable();
        }

        // Placement: live (the quad reads the record each frame).
        ImGui.SeparatorText("Placement");
        if (r.IsBillboard)
        {
            DrawBillboardForm(r);
        }
        else
        {
            DrawPartForm(r);
        }

        ImGui.Spacing();
        if (ImGui.Button(" Done ##iw_cfg_done", new float2(-1f, 0f)))
        {
            _configuring = null;
        }
    }

    private void BeginConfigure(InWorldTerminalInstance instance)
    {
        _configuring = instance;
        _resizeCols = instance.Record.Cols;
        _resizeRows = instance.Record.Rows;
    }

    private void CreateFromDraft(string name)
    {
        // Attach an optional startup command to the chosen shell (or the default shell when
        // none was picked). Clone so the per-frame shell-list instance is never mutated.
        var launch = _draftShell?.Options;
        string startupCommand = _startupCommandInput.ToString().Trim();
        if (!string.IsNullOrEmpty(startupCommand))
        {
            launch = (launch ?? ProcessLaunchOptions.CreateDefault()).Clone();
            launch.StartupCommand = startupCommand;
        }

        var record = new InWorldTerminalRecord
        {
            Name = name,
            Cols = Math.Clamp(_draftCols, 8, 400),
            Rows = Math.Clamp(_draftRows, 4, 200),
            Mode = _draftMode,
            TargetVehicleId = _draftVehicleId,
            TargetPartId = _draftPartId,
            TargetSubPartId = _draftSubPartId,
            Launch = launch,
            ThemeName = _draftThemeName,
        };

        if (_manager.Create(record) is null)
        {
            _createError = "Failed to create (see log).";
            return;
        }

        _nameInput.Clear();
        _startupCommandInput.Clear();
        _draftShell = null;
        _draftThemeName = null;
        _draftVehicleId = "";
        _draftPartId = "";
        _draftSubPartId = "";
        _createError = null;
    }

    private void DrawPartForm(InWorldTerminalRecord s)
    {
        if (ImGuiWidgets.BeginFormTable("##iw_part_form"))
        {
            DrawVehiclePartPicker(s.TargetVehicleId, s.TargetPartId, s.TargetSubPartId,
                v => s.TargetVehicleId = v, p => s.TargetPartId = p, sp => s.TargetSubPartId = sp);
            DragRow("Offset X (m)", "##px", s.PartOffsetX, 0.05f, -200f, 200f, v => s.PartOffsetX = v);
            DragRow("Offset Y (m)", "##py", s.PartOffsetY, 0.05f, -200f, 200f, v => s.PartOffsetY = v);
            DragRow("Offset Z (m)", "##pz", s.PartOffsetZ, 0.05f, -200f, 200f, v => s.PartOffsetZ = v);
            DragRow("Rotation X (deg)", "##prx", s.PartRotationX, 0.5f, -180f, 180f, v => s.PartRotationX = v);
            DragRow("Rotation Y (deg)", "##pry", s.PartRotationY, 0.5f, -180f, 180f, v => s.PartRotationY = v);
            DragRow("Rotation Z (deg)", "##prz", s.PartRotationZ, 0.5f, -180f, 180f, v => s.PartRotationZ = v);
            DragRow("Width (m)", "##pw", s.PartWidthMeters, 0.02f, 0.05f, 100f, v => s.PartWidthMeters = v);
            DragRow("Height (m)", "##ph", s.PartHeightMeters, 0.02f, 0.05f, 100f, v => s.PartHeightMeters = v);
            ImGuiWidgets.EndFormTable();
        }
    }

    private void DrawBillboardForm(InWorldTerminalRecord s)
    {
        if (ImGuiWidgets.BeginFormTable("##iw_bb_form"))
        {
            DragRow("Distance (m)", "##bd", s.BillboardDistance, 0.05f, 0.2f, 200f, v => s.BillboardDistance = v);
            DragRow("Screen Offset X (m)", "##bx", s.BillboardOffsetX, 0.02f, -100f, 100f, v => s.BillboardOffsetX = v);
            DragRow("Screen Offset Y (m)", "##by", s.BillboardOffsetY, 0.02f, -100f, 100f, v => s.BillboardOffsetY = v);
            DragRow("Width (m)", "##bw", s.BillboardWidthMeters, 0.02f, 0.05f, 100f, v => s.BillboardWidthMeters = v);
            DragRow("Height (m)", "##bh", s.BillboardHeightMeters, 0.02f, 0.05f, 100f, v => s.BillboardHeightMeters = v);
            ImGuiWidgets.EndFormTable();
        }

        bool alwaysOnTop = s.BillboardAlwaysOnTop;
        if (ImGui.Checkbox("Always on top (ignore depth)", ref alwaysOnTop))
        {
            s.BillboardAlwaysOnTop = alwaysOnTop;
        }
    }

    // Tiered Vehicle → Part → Sub-part selection (all filtered combos). Assumes it
    // runs inside a BeginFormTable. Changing the vehicle resets part + sub-part;
    // changing the part resets the sub-part. The Part combo lists only top-level parts;
    // selecting a part is enough to anchor to it, and its sub-parts populate the
    // separate Sub-part combo — picking "(none)" there de-selects (anchors to the part).
    // Empty vehicle id = the controlled vehicle (the anchor follows the player); empty
    // part id = that vehicle's first part.
    private void DrawVehiclePartPicker(
        string vehicleId, string partId, string subPartId,
        Action<string> setVehicle, Action<string> setPart, Action<string> setSubPart)
    {
        var vehicles = VehicleLookup.GetAll();

        var vItems = new List<(string Key, string Label)> { ("", "(controlled vehicle)") };
        foreach (var v in vehicles)
        {
            vItems.Add((v.Id, v.Id));
        }

        ImGuiWidgets.FormRow("Vehicle");
        string vPreview = string.IsNullOrEmpty(vehicleId) ? "(controlled vehicle)" : vehicleId;
        if (ImGuiWidgets.FilterCombo("##iw_vehicle", vPreview, _vehicleFilter, vItems, out string? pickedV) && pickedV != null)
        {
            setVehicle(pickedV);
            setPart(string.Empty);
            setSubPart(string.Empty);
        }

        var resolved = VehicleLookup.Resolve(vehicleId);

        // Part: top-level parts only (sub-parts go in their own combo below).
        var pItems = new List<(string Key, string Label)> { ("", "(auto: first part)") };
        if (resolved != null)
        {
            foreach (Part p in resolved.Parts.Parts)
            {
                pItems.Add((p.Id, PartLabel(p)));
            }
        }

        ImGuiWidgets.FormRow("Part");
        string pPreview = string.IsNullOrEmpty(partId) ? "(auto: first part)" : partId;
        if (ImGuiWidgets.FilterCombo("##iw_part", pPreview, _partFilter, pItems, out string? pickedP) && pickedP != null)
        {
            setPart(pickedP);
            setSubPart(string.Empty);
        }

        // Sub-part: children of the selected top-level part. "(none)" de-selects
        // (anchors to the part itself); disabled until a specific part is picked.
        Part? selectedPart = FindTopLevelPart(resolved, partId);
        var spItems = new List<(string Key, string Label)> { ("", "(none: the part itself)") };
        if (selectedPart != null)
        {
            foreach (Part sub in selectedPart.SubParts)
            {
                spItems.Add((sub.Id, PartLabel(sub)));
            }
        }

        ImGuiWidgets.FormRow("Sub-part");
        bool noPart = selectedPart == null;
        if (noPart)
        {
            ImGui.BeginDisabled();
        }

        string spPreview = string.IsNullOrEmpty(subPartId) ? "(none: the part itself)" : subPartId;
        if (ImGuiWidgets.FilterCombo("##iw_subpart", spPreview, _subPartFilter, spItems, out string? pickedSP) && pickedSP != null)
        {
            setSubPart(pickedSP);
        }

        if (noPart)
        {
            ImGui.EndDisabled();
        }
    }

    private static Part? FindTopLevelPart(Vehicle? vehicle, string partId)
    {
        if (vehicle == null || string.IsNullOrEmpty(partId))
        {
            return null;
        }

        foreach (Part p in vehicle.Parts.Parts)
        {
            if (p.Id == partId)
            {
                return p;
            }
        }

        return null;
    }

    // Returns true (with the picked name) when a theme is chosen this frame. Draws a
    // filtered combo over the catalog; an empty preview means "(default)".
    private bool DrawThemePicker(string id, ref string? selected)
    {
        var items = new List<(string Key, string Label)>();
        if (_manager.Catalog is { } catalog)
        {
            foreach (var theme in catalog.BuiltInThemes)
            {
                items.Add((theme.Name, theme.Name));
            }

            foreach (var theme in catalog.UserThemes)
            {
                items.Add((theme.Name, $"{theme.Name}  (saved)"));
            }
        }

        if (ImGuiWidgets.FilterCombo(id, selected ?? "(default)", _themeFilter, items, out string? picked) && picked != null)
        {
            selected = picked;
            return true;
        }

        return false;
    }

    private List<(string Label, ProcessLaunchOptions Options)> BuildShellList()
    {
        var list = new List<(string, ProcessLaunchOptions)>
        {
            ("Default Shell", ProcessLaunchOptions.CreateDefault()),
        };

        var config = _manager.Config;
        var snapshot = ShellMenuCache.Current;
        if (snapshot != null)
        {
            foreach (var (label, type) in snapshot.Entries)
            {
                switch (type)
                {
                    case ShellType.Wsl:
                        foreach (var d in snapshot.WslDistributions)
                        {
                            list.Add(($"WSL: {d.DisplayName}", ProcessLaunchOptions.CreateWsl(d.Name)));
                        }

                        break;
                    case ShellType.Auto:
                        foreach (var u in snapshot.UnixShells)
                        {
                            list.Add((u.DisplayName, ProcessLaunchOptions.CreateCustom(u.Path)));
                        }

                        break;
                    case ShellType.PowerShell:
                        list.Add((label, ProcessLaunchOptions.CreatePowerShell()));
                        break;
                    case ShellType.PowerShellCore:
                        list.Add((label, ProcessLaunchOptions.CreatePowerShellCore()));
                        break;
                    case ShellType.Cmd:
                        list.Add((label, ProcessLaunchOptions.CreateCmd()));
                        break;
                    case ShellType.CustomGame:
                        if (config != null)
                        {
                            list.Add((label, config.CreateGameShellLaunchOptions()));
                        }

                        break;
                }
            }
        }
        else if (config != null)
        {
            list.Add(("Game Console", config.CreateGameShellLaunchOptions()));
        }

        foreach (var (id, metadata) in CustomShellRegistry.Instance.GetAvailableShells())
        {
            if (id == nameof(GameConsoleShell))
            {
                continue;
            }

            list.Add((metadata.Name, ProcessLaunchOptions.CreateCustomGame(id)));
        }

        return list;
    }

    private bool InstanceIsLive(InWorldTerminalInstance instance)
    {
        var instances = _manager.Instances;
        for (int i = 0; i < instances.Count; i++)
        {
            if (ReferenceEquals(instances[i], instance))
            {
                return true;
            }
        }

        return false;
    }

    private static string PartLabel(Part p)
        => string.IsNullOrEmpty(p.DisplayName) ? p.Id : $"{p.DisplayName} ({p.Id})";

    // Drag row: assumes it runs inside a BeginFormTable; emits a FormRow then a DragFloat.
    private static void DragRow(string label, string id, float value, float speed, float min, float max, Action<float> set)
    {
        ImGuiWidgets.FormRow(label);
        float v = value;
        if (ImGui.DragFloat(id, ref v, speed, min, max, "%.2f"))
        {
            set(v);
        }
    }
}
