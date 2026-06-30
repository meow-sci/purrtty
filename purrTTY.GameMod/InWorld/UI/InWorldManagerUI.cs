using Brutal.ImGuiApi;
using KSA;
using purrTTY.Core.Terminal;
using purrTTY.CustomShells;
using purrTTY.Display.Ghostty;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.GameMod.UI;
using float2 = Brutal.Numerics.float2;
using float4 = Brutal.Numerics.float4;

namespace purrTTY.GameMod.InWorld.UI;

/// <summary>
///     The in-world terminal manager: a modal that lists the live in-world terminals
///     (with per-instance focus / placement / close) and a create form for new ones —
///     name, shell, fixed grid size, anchor mode + placement, and theme. Replaces the
///     single-instance launch popup. Renders in the <b>main</b> ImGui context; edits
///     to an instance's placement are live (the quad reads its record every frame).
/// </summary>
public sealed class InWorldManagerUI
{
    private const string PopupId = "In-World Terminals##purrtty_inworld_manager";

    private readonly InWorldTerminalManager _manager;

    private readonly ImInputString _nameInput = new(64);
    private readonly ImInputString _shellFilter = new(64);
    private readonly ImInputString _themeFilter = new(64);
    private readonly ImInputString _partFilter = new(64);

    private bool _openRequested;
    private int _draftCols = 100;
    private int _draftRows = 30;
    private string _draftMode = InWorldTerminalRecord.ModePart;
    private (string Label, ProcessLaunchOptions Options)? _draftShell;
    private string? _draftThemeName;
    private string? _createError;

    // Which instance's placement form is open inline (null = show the create form).
    private InWorldTerminalInstance? _configuring;

    public InWorldManagerUI(InWorldTerminalManager manager) => _manager = manager;

    /// <summary>Requests the manager dialog open on the next <see cref="Draw"/> (from the menu).</summary>
    public void RequestOpen()
    {
        _openRequested = true;
        _configuring = null;
        _createError = null;
    }

    /// <summary>Renders the dialog. Call every frame from the main context.</summary>
    public void Draw()
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        ImGui.SetNextWindowSize(new float2(560f, 0f), ImGuiCond.Appearing);
        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        DrawInstanceList();
        ImGui.Separator();

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

        ImGui.Separator();
        if (ImGui.Button("Close", new float2(-1f, 0f)) || !open)
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawInstanceList()
    {
        ImGui.TextDisabled("In-World Terminals");

        var instances = _manager.Instances;
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

            if (ImGui.Button($"Focus##iw_focus_{i}"))
            {
                _manager.Focus(instance);
            }

            ImGui.SameLine();
            if (ImGui.Button($"Place##iw_place_{i}"))
            {
                _configuring = instance;
            }

            ImGui.SameLine();
            if (ImGui.Button($"Close##iw_close_{i}"))
            {
                _manager.Remove(instance);
                // The list changed under us; resume next frame.
                return;
            }

            ImGui.Spacing();
        }
    }

    private void DrawCreateForm()
    {
        ImGui.TextDisabled("New terminal");

        ImGui.Text("Name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##iw_name", _nameInput, ImGuiInputTextFlags.None);
        _nameInput.EvaluateLength();
        string name = _nameInput.ToString().Trim();

        // Shell picker (flat list keyed by index).
        var shells = BuildShellList();
        ImGui.Text("Shell");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        var shellItems = new List<(string Key, string Label)>(shells.Count);
        for (int i = 0; i < shells.Count; i++)
        {
            shellItems.Add((i.ToString(), shells[i].Label));
        }

        string shellPreview = _draftShell?.Label ?? "Default Shell";
        if (ImGuiWidgets.FilterCombo("##iw_shell", shellPreview, _shellFilter, shellItems, out string? shellKey)
            && shellKey != null && int.TryParse(shellKey, out int shellIdx) && shellIdx < shells.Count)
        {
            _draftShell = shells[shellIdx];
        }

        ImGui.Text("Columns");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.DragInt("##iw_cols", ref _draftCols, 0.5f, 8, 400, "%d");

        ImGui.Text("Rows");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.DragInt("##iw_rows", ref _draftRows, 0.5f, 4, 200, "%d");

        ImGui.Text("Anchor");
        ImGui.SameLine();
        if (ImGui.Button(_draftMode == InWorldTerminalRecord.ModePart ? "[ Part ]" : "Part"))
        {
            _draftMode = InWorldTerminalRecord.ModePart;
        }

        ImGui.SameLine();
        if (ImGui.Button(_draftMode == InWorldTerminalRecord.ModeBillboard ? "[ Billboard ]" : "Billboard"))
        {
            _draftMode = InWorldTerminalRecord.ModeBillboard;
        }

        // Theme picker (catalog; default = the global selected theme).
        var themeItems = new List<(string Key, string Label)>();
        if (_manager.Catalog is { } catalog)
        {
            foreach (var theme in catalog.BuiltInThemes)
            {
                themeItems.Add((theme.Name, theme.Name));
            }

            foreach (var theme in catalog.UserThemes)
            {
                themeItems.Add((theme.Name, $"{theme.Name}  (saved)"));
            }
        }

        ImGui.Text("Theme");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGuiWidgets.FilterCombo("##iw_theme", _draftThemeName ?? "(default)", _themeFilter, themeItems, out string? themeKey)
            && themeKey != null)
        {
            _draftThemeName = themeKey;
        }

        ImGui.Spacing();

        bool canCreate = name.Length > 0 && TerminalTargetRegistry.IsNameAvailable(name);
        if (name.Length > 0 && !TerminalTargetRegistry.IsNameAvailable(name))
        {
            ImGui.TextColored(new float4(1f, 0.8f, 0.3f, 1f), $"A terminal named '{name}' already exists.");
        }

        if (_createError != null)
        {
            ImGui.TextColored(new float4(1f, 0.4f, 0.4f, 1f), _createError);
        }

        if (!canCreate)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Create terminal", new float2(-1f, 0f)) && canCreate)
        {
            CreateFromDraft(name);
        }

        if (!canCreate)
        {
            ImGui.EndDisabled();
        }
    }

    private void CreateFromDraft(string name)
    {
        var record = new InWorldTerminalRecord
        {
            Name = name,
            Cols = Math.Clamp(_draftCols, 8, 400),
            Rows = Math.Clamp(_draftRows, 4, 200),
            Mode = _draftMode,
            Launch = _draftShell?.Options,
            ThemeName = _draftThemeName,
        };

        if (_manager.Create(record) is null)
        {
            _createError = "Failed to create (see log).";
            return;
        }

        // Reset the draft for the next terminal.
        _nameInput.Clear();
        _draftShell = null;
        _draftThemeName = null;
        _createError = null;
    }

    private void DrawConfigure(InWorldTerminalInstance instance)
    {
        var r = instance.Record;
        ImGui.TextDisabled($"Placement — {instance.Name}");
        ImGui.Separator();

        if (r.IsBillboard)
        {
            DrawBillboardForm(r);
        }
        else
        {
            DrawPartForm(r);
        }

        if (ImGui.Button("Done", new float2(-1f, 0f)))
        {
            _configuring = null;
        }
    }

    private void DrawPartForm(InWorldTerminalRecord s)
    {
        DrawPartCombo(s);
        ImGui.Spacing();
        DragRow("Offset X (m)", "##px", s.PartOffsetX, 0.05f, -200f, 200f, v => s.PartOffsetX = v);
        DragRow("Offset Y (m)", "##py", s.PartOffsetY, 0.05f, -200f, 200f, v => s.PartOffsetY = v);
        DragRow("Offset Z (m)", "##pz", s.PartOffsetZ, 0.05f, -200f, 200f, v => s.PartOffsetZ = v);
        ImGui.Spacing();
        DragRow("Rotation X (deg)", "##prx", s.PartRotationX, 0.5f, -180f, 180f, v => s.PartRotationX = v);
        DragRow("Rotation Y (deg)", "##pry", s.PartRotationY, 0.5f, -180f, 180f, v => s.PartRotationY = v);
        DragRow("Rotation Z (deg)", "##prz", s.PartRotationZ, 0.5f, -180f, 180f, v => s.PartRotationZ = v);
        ImGui.Spacing();
        DragRow("Width (m)", "##pw", s.PartWidthMeters, 0.02f, 0.05f, 100f, v => s.PartWidthMeters = v);
        DragRow("Height (m)", "##ph", s.PartHeightMeters, 0.02f, 0.05f, 100f, v => s.PartHeightMeters = v);
    }

    private void DrawBillboardForm(InWorldTerminalRecord s)
    {
        DragRow("Distance (m)", "##bd", s.BillboardDistance, 0.05f, 0.2f, 200f, v => s.BillboardDistance = v);
        ImGui.Spacing();
        DragRow("Screen Offset X (m)", "##bx", s.BillboardOffsetX, 0.02f, -100f, 100f, v => s.BillboardOffsetX = v);
        DragRow("Screen Offset Y (m)", "##by", s.BillboardOffsetY, 0.02f, -100f, 100f, v => s.BillboardOffsetY = v);
        ImGui.Spacing();
        DragRow("Width (m)", "##bw", s.BillboardWidthMeters, 0.02f, 0.05f, 100f, v => s.BillboardWidthMeters = v);
        DragRow("Height (m)", "##bh", s.BillboardHeightMeters, 0.02f, 0.05f, 100f, v => s.BillboardHeightMeters = v);
        ImGui.Spacing();
        bool alwaysOnTop = s.BillboardAlwaysOnTop;
        if (ImGui.Checkbox("Always on top (ignore depth)", ref alwaysOnTop))
        {
            s.BillboardAlwaysOnTop = alwaysOnTop;
        }
    }

    private void DrawPartCombo(InWorldTerminalRecord s)
    {
        var vehicle = Program.ControlledVehicle;
        string current = string.IsNullOrEmpty(s.TargetPartId) ? "(auto: first part)" : s.TargetPartId;

        var items = new List<(string Key, string Label)> { ("", "(auto: first part)") };
        if (vehicle != null)
        {
            foreach (Part p in vehicle.Parts.Parts)
            {
                items.Add((p.Id, PartLabel(p)));
                foreach (Part sub in p.SubParts)
                {
                    items.Add((sub.Id, PartLabel(sub)));
                }
            }
        }

        ImGui.Text("Anchor Part");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGuiWidgets.FilterCombo("##iw_part", current, _partFilter, items, out string? picked) && picked != null)
        {
            s.TargetPartId = picked;
        }

        if (vehicle == null)
        {
            ImGui.TextDisabled("No controlled vessel — anchor resolves when you take control.");
        }
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

        // Custom shells registered by other mods, skipping the built-in game console.
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

    private static void DragRow(string label, string id, float value, float speed, float min, float max, Action<float> set)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        float v = value;
        if (ImGui.DragFloat(id, ref v, speed, min, max, "%.2f"))
        {
            set(v);
        }
    }
}
