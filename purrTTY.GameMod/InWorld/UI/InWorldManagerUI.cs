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
///     The in-world terminal manager: a movable window listing the live in-world
///     terminals in a table (name / size / Focus / Config / Destroy) under a
///     collapsible header, plus a collapsible create form (name, shell, grid size,
///     anchor, theme). "Config" opens a <b>separate</b> per-terminal window (so several
///     can be edited at once) carrying the live theme/opacity edits, a recreate-based
///     grid resize, a rename, and the live placement form. Renders in the <b>main</b>
///     ImGui context; placement, theme, opacity, and rename edits are live, while a
///     size change rebuilds the off-screen texture (restarting the shell). Table-based
///     label/widget layout per the KSA mod conventions.
/// </summary>
public sealed class InWorldManagerUI
{
    private const string PopupId = "In-World Terminals##purrtty_inworld_manager";
    private const float DialogWidth = 600f;

    // Label sets used to size each window's fixed label column so sections share one
    // content-fitted gutter (see ImGuiWidgets.MeasureLabelWidth / BeginFormTableFixed).
    private static readonly string[] CreateLabels =
    {
        "Name", "Shell", "Startup command", "Columns", "Rows", "Theme", "Anchor",
        "Vehicle", "Part", "Sub-part",
    };

    private static readonly string[] ConfigureLabels =
    {
        "Name", "Theme", "Columns", "Rows", "Background", "Foreground", "Cell background",
    };

    private static readonly string[] PartPlacementLabels =
    {
        "Vehicle", "Part", "Sub-part", "Offset X (m)", "Offset Y (m)", "Offset Z (m)",
        "Rotation X (deg)", "Rotation Y (deg)", "Rotation Z (deg)", "Width (m)", "Height (m)",
    };

    private static readonly string[] BillboardPlacementLabels =
    {
        "Distance (m)", "Screen Offset X (m)", "Screen Offset Y (m)", "Width (m)", "Height (m)",
        "Rotation X (deg)", "Rotation Y (deg)", "Rotation Z (deg)",
    };

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

    // One open configure window per terminal (multiple can be edited simultaneously).
    private readonly List<ConfigWindow> _configWindows = new();
    private int _nextConfigId = 1;

    public InWorldManagerUI(InWorldTerminalManager manager) => _manager = manager;

    /// <summary>Shows (and brings to front) the manager window (from the menu).</summary>
    public void RequestOpen()
    {
        _visible = true;
        _requestFocus = true;
        _createError = null;
    }

    /// <summary>
    ///     Draws the manager (a non-modal, movable window) and any open per-terminal
    ///     configure windows every frame from the main context. The configure windows are
    ///     independent of the manager's visibility, so closing the manager leaves them up.
    /// </summary>
    public void Draw()
    {
        DrawManagerWindow();
        DrawConfigWindows();
    }

    private void DrawManagerWindow()
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

        var instances = _manager.Instances;
        if (ImGui.CollapsingHeader($"In-World Terminals ( {instances.Count} )##iw_list_hdr", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawInstanceList();
        }

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("New Terminal##iw_new_hdr", ImGuiTreeNodeFlags.DefaultOpen))
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
        if (instances.Count == 0)
        {
            ImGui.TextDisabled("None yet — create one below.");
            return;
        }

        // Destroying mutates the live instance list; capture and apply after EndTable.
        InWorldTerminalInstance? destroy = null;

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable("##iw_list", 5, flags))
        {
            ImGui.TableSetupColumn("##name", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##size", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("##focus", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("##config", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("##destroy", ImGuiTableColumnFlags.WidthFixed);

            for (int i = 0; i < instances.Count; i++)
            {
                var instance = instances[i];
                var r = instance.Record;
                ImGui.TableNextRow();

                // Name (a trailing "*" marks the focused terminal).
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(instance.HasFocus ? $"{instance.Name} *" : instance.Name);

                // Grid size, e.g. "80 x 24".
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{r.Cols} x {r.Rows}");

                ImGui.TableNextColumn();
                if (ImGui.Button($" Focus ##iw_focus_{i}"))
                {
                    _manager.Focus(instance);
                }

                ImGui.TableNextColumn();
                if (ImGui.Button($" Config ##iw_cfg_{i}"))
                {
                    OpenConfig(instance);
                }

                ImGui.TableNextColumn();
                if (ImGuiWidgets.DestructiveButton($" Destroy ##iw_remove_{i}"))
                {
                    destroy = instance;
                }
            }

            ImGui.EndTable();
        }

        if (destroy != null)
        {
            _manager.Remove(destroy);
        }
    }

    private void DrawCreateForm()
    {
        var shells = BuildShellList();
        var shellItems = new List<(string Key, string Label)>(shells.Count);
        for (int i = 0; i < shells.Count; i++)
        {
            shellItems.Add((i.ToString(), shells[i].Label));
        }

        float labelWidth = ImGuiWidgets.MeasureLabelWidth(CreateLabels);

        if (ImGuiWidgets.BeginFormTableFixed("##iw_create", labelWidth))
        {
            ImGuiWidgets.FormRow("Name");
            ImGui.InputText("##iw_name", _nameInput, ImGuiInputTextFlags.None);
            _nameInput.EvaluateLength();

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

            // Anchor radios live in the widget column to share the section's label gutter.
            ImGuiWidgets.FormRow("Anchor");
            if (ImGui.RadioButton("Part##iw_mode_part", _draftMode == InWorldTerminalRecord.ModePart))
            {
                _draftMode = InWorldTerminalRecord.ModePart;
            }

            ImGui.SameLine(0, 16);
            if (ImGui.RadioButton("Screen##iw_mode_bb", _draftMode == InWorldTerminalRecord.ModeBillboard))
            {
                _draftMode = InWorldTerminalRecord.ModeBillboard;
            }

            // Part mode: pick the anchor vehicle + part at creation time (same table → same gutter).
            if (_draftMode == InWorldTerminalRecord.ModePart)
            {
                DrawVehiclePartPicker(_draftVehicleId, _draftPartId, _draftSubPartId,
                    v => _draftVehicleId = v, p => _draftPartId = p, sp => _draftSubPartId = sp);
            }

            ImGuiWidgets.EndFormTable();
        }

        string name = _nameInput.ToString().Trim();

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

    // ---- Per-terminal configure windows ----

    /// <summary>Opens (or re-focuses) the configure window for an instance.</summary>
    private void OpenConfig(InWorldTerminalInstance instance)
    {
        for (int i = 0; i < _configWindows.Count; i++)
        {
            if (ReferenceEquals(_configWindows[i].Instance, instance))
            {
                _configWindows[i].RequestFocus = true;
                return;
            }
        }

        _configWindows.Add(new ConfigWindow(instance, _nextConfigId++));
    }

    private void DrawConfigWindows()
    {
        // Drop windows the user closed, or whose instance was destroyed/retired.
        for (int i = _configWindows.Count - 1; i >= 0; i--)
        {
            var w = _configWindows[i];
            if (!w.Open || w.Instance.IsFailed || !InstanceIsLive(w.Instance))
            {
                _configWindows.RemoveAt(i);
            }
        }

        for (int i = 0; i < _configWindows.Count; i++)
        {
            DrawConfigWindow(_configWindows[i]);
        }
    }

    private void DrawConfigWindow(ConfigWindow w)
    {
        var instance = w.Instance;
        var r = instance.Record;

        if (w.RequestFocus)
        {
            w.RequestFocus = false;
            ImGui.SetNextWindowFocus();
        }

        // Cascade new windows so several configure windows don't stack exactly.
        float cascade = (w.Id % 6) * 28f;
        ImGui.SetNextWindowPos(new float2(140f + cascade, 120f + cascade), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new float2(420f, 600f), ImGuiCond.FirstUseEver);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(16f, 16f));
        bool open = ImGui.Begin($"Configure — {instance.Name}##iw_cfg_win_{w.Id}", ref w.Open, ImGuiWindowFlags.None);
        ImGui.PopStyleVar();

        if (!open)
        {
            ImGui.End();
            return;
        }

        // One label gutter shared across both sections (covers the widest label of either).
        float labelWidth = Math.Max(
            ImGuiWidgets.MeasureLabelWidth(ConfigureLabels),
            ImGuiWidgets.MeasureLabelWidth(r.IsBillboard ? BillboardPlacementLabels : PartPlacementLabels));

        if (ImGui.CollapsingHeader("Configure##iw_cfg_cfg_hdr", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawConfigureSection(w, labelWidth);
        }

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Placement##iw_cfg_place_hdr", ImGuiTreeNodeFlags.DefaultOpen))
        {
            if (r.IsBillboard)
            {
                DrawBillboardForm(r, labelWidth);
            }
            else
            {
                DrawPartForm(r, labelWidth);
            }
        }

        // Footer: Done | Destroy, half-width each (the destroy is red).
        ImGui.Spacing();
        ImGui.Separator();
        float avail = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float buttonWidth = (avail - gap) / 2f;

        if (ImGui.Button($" Done ##iw_cfg_done_{w.Id}", new float2(buttonWidth, 0f)))
        {
            w.Open = false;
        }

        ImGui.SameLine(0, gap);
        if (ImGuiWidgets.DestructiveButton($" Destroy ##iw_cfg_destroy_{w.Id}", new float2(buttonWidth, 0f)))
        {
            _manager.Remove(instance);
            w.Open = false;
        }

        ImGui.End();
    }

    // Configure section: rename + theme + grid size + the three live opacities, plus the
    // recreate-based "Apply size" action (size is fixed at build time).
    private void DrawConfigureSection(ConfigWindow w, float labelWidth)
    {
        var instance = w.Instance;
        var r = instance.Record;

        if (ImGuiWidgets.BeginFormTableFixed($"##iw_cfg_form_{w.Id}", labelWidth))
        {
            // Name: applied on commit (Enter / focus loss); reverts on collision.
            ImGuiWidgets.FormRow("Name");
            ImGui.InputText($"##iw_cfg_name_{w.Id}", w.NameInput, ImGuiInputTextFlags.None);
            w.NameInput.EvaluateLength();
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                CommitRename(w);
            }

            // Theme: applied live (colors + font reflow within the current texture).
            ImGuiWidgets.FormRow("Theme");
            string? themeName = r.ThemeName;
            if (DrawThemePicker($"##iw_cfg_theme_{w.Id}", ref themeName) && themeName != null
                && _manager.Catalog?.Find(themeName) is { } def)
            {
                instance.ApplyTheme(def);
            }

            // Grid size: a draft; "Apply size" below recreates the texture (restarts the shell).
            ImGuiWidgets.FormRow("Columns");
            ImGui.DragInt($"##iw_cfg_cols_{w.Id}", ref w.ResizeCols, 0.5f, 8, 400, "%d");

            ImGuiWidgets.FormRow("Rows");
            ImGui.DragInt($"##iw_cfg_rows_{w.Id}", ref w.ResizeRows, 0.5f, 4, 200, "%d");

            // Opacity: live, per-pixel transparency baked into the off-screen texture and
            // composited by the quad over the 3D scene. Reuses the theme's three opacities
            // (Background drives the see-through; Foreground/Cell dim text/cell backgrounds).
            // Session-only — not persisted unless captured into a named theme via "Theme…".
            float bg = instance.BackgroundOpacity;
            float fg = instance.ForegroundOpacity;
            float cell = instance.CellBackgroundOpacity;
            bool changed = false;

            ImGuiWidgets.FormRow("Background");
            int bgPct = (int)MathF.Round(bg * 100f);
            if (ImGui.SliderInt($"##iw_cfg_op_bg_{w.Id}", ref bgPct, 0, 100, "%d%%"))
            {
                bg = Math.Clamp(bgPct, 0, 100) / 100f;
                changed = true;
            }

            ImGuiWidgets.FormRow("Foreground");
            int fgPct = (int)MathF.Round(fg * 100f);
            if (ImGui.SliderInt($"##iw_cfg_op_fg_{w.Id}", ref fgPct, 0, 100, "%d%%"))
            {
                fg = Math.Clamp(fgPct, 0, 100) / 100f;
                changed = true;
            }

            ImGuiWidgets.FormRow("Cell background");
            int cellPct = (int)MathF.Round(cell * 100f);
            if (ImGui.SliderInt($"##iw_cfg_op_cell_{w.Id}", ref cellPct, 0, 100, "%d%%"))
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

        bool sizeChanged = w.ResizeCols != r.Cols || w.ResizeRows != r.Rows;
        if (!sizeChanged)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button($" Apply size (restarts shell) ##iw_cfg_resize_{w.Id}") && sizeChanged)
        {
            var newRecord = r.Clone();
            newRecord.Cols = Math.Clamp(w.ResizeCols, 8, 400);
            newRecord.Rows = Math.Clamp(w.ResizeRows, 4, 200);
            var created = _manager.Recreate(instance, newRecord);
            if (created != null)
            {
                w.Rebind(created);
            }
        }

        if (!sizeChanged)
        {
            ImGui.EndDisabled();
        }

        if (w.NameError != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiWidgets.WarningColor, w.NameError);
        }
    }

    // Commits the configure window's edited name to its instance (live rename). Blank or
    // colliding names revert the buffer and surface a warning; a no-op match clears it.
    private static void CommitRename(ConfigWindow w)
    {
        string newName = w.NameInput.ToString().Trim();
        if (newName.Length == 0)
        {
            w.NameError = "Name cannot be blank.";
            w.NameInput.SetValue(w.Instance.Name);
            return;
        }

        if (newName.Equals(w.Instance.Name, StringComparison.Ordinal))
        {
            w.NameError = null;
            return;
        }

        if (!w.Instance.TryRename(newName))
        {
            w.NameError = $"A terminal named '{newName}' already exists.";
            w.NameInput.SetValue(w.Instance.Name);
            return;
        }

        w.NameError = null;
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

    private void DrawPartForm(InWorldTerminalRecord s, float labelWidth)
    {
        if (ImGuiWidgets.BeginFormTableFixed("##iw_part_form", labelWidth))
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

    private void DrawBillboardForm(InWorldTerminalRecord s, float labelWidth)
    {
        if (ImGuiWidgets.BeginFormTableFixed("##iw_bb_form", labelWidth))
        {
            DragRow("Distance (m)", "##bd", s.BillboardDistance, 0.05f, 0.2f, 200f, v => s.BillboardDistance = v);
            DragRow("Screen Offset X (m)", "##bx", s.BillboardOffsetX, 0.02f, -100f, 100f, v => s.BillboardOffsetX = v);
            DragRow("Screen Offset Y (m)", "##by", s.BillboardOffsetY, 0.02f, -100f, 100f, v => s.BillboardOffsetY = v);
            DragRow("Width (m)", "##bw", s.BillboardWidthMeters, 0.02f, 0.05f, 100f, v => s.BillboardWidthMeters = v);
            DragRow("Height (m)", "##bh", s.BillboardHeightMeters, 0.02f, 0.05f, 100f, v => s.BillboardHeightMeters = v);
            DragRow("Rotation X (deg)", "##brx", s.BillboardRotationX, 0.5f, -180f, 180f, v => s.BillboardRotationX = v);
            DragRow("Rotation Y (deg)", "##bry", s.BillboardRotationY, 0.5f, -180f, 180f, v => s.BillboardRotationY = v);
            DragRow("Rotation Z (deg)", "##brz", s.BillboardRotationZ, 0.5f, -180f, 180f, v => s.BillboardRotationZ = v);
            ImGuiWidgets.EndFormTable();
        }

        bool alwaysOnTop = s.BillboardAlwaysOnTop;
        if (ImGui.Checkbox("Always on top (ignore depth)", ref alwaysOnTop))
        {
            s.BillboardAlwaysOnTop = alwaysOnTop;
        }

        // Opt a billboard into ego-space click-to-focus (it is menu-only by default).
        bool clickToFocus = s.BillboardClickToFocus;
        if (ImGui.Checkbox("Click to focus (ray-pick)", ref clickToFocus))
        {
            s.BillboardClickToFocus = clickToFocus;
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

    // One open configure window: its target instance, a stable id (for ImGui window
    // identity + position cascade), and the per-window edit buffers (name + draft grid
    // size). Re-pointed at the rebuilt instance after an "Apply size" recreate.
    private sealed class ConfigWindow
    {
        public ConfigWindow(InWorldTerminalInstance instance, int id)
        {
            Instance = instance;
            Id = id;
            NameInput.SetValue(instance.Name);
            ResizeCols = instance.Record.Cols;
            ResizeRows = instance.Record.Rows;
        }

        public InWorldTerminalInstance Instance { get; private set; }
        public int Id { get; }
        public ImInputString NameInput { get; } = new(64);

        // Fields (not properties): passed by ref to ImGui widgets.
        public int ResizeCols;
        public int ResizeRows;
        public bool Open = true;
        public bool RequestFocus = true;
        public string? NameError;

        // Re-target this window at the rebuilt instance after a grid-size recreate.
        public void Rebind(InWorldTerminalInstance instance)
        {
            Instance = instance;
            ResizeCols = instance.Record.Cols;
            ResizeRows = instance.Record.Rows;
            NameInput.SetValue(instance.Name);
            NameError = null;
        }
    }
}
