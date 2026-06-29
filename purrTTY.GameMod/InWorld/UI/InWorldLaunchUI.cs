using Brutal.ImGuiApi;
using KSA;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.GameMod.UI;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld.UI;

/// <summary>
///     The in-world terminal launch UI: a modal popup to pick the anchor mode
///     (vehicle part or camera billboard), then a mode-tailored config form.
///     Renders in the <b>main</b> ImGui context — the product requirement is that
///     the terminal <em>window</em> is hidden, not that a config dialog is. Edits
///     mutate the live <see cref="InWorldSettings"/>, which the quad reads every
///     frame, so the in-world panel updates instantly while the form is open.
/// </summary>
public sealed class InWorldLaunchUI
{
    private const string PopupId = "In-World Terminal##purrtty_inworld_launch";

    private readonly InWorldTerminalManager _manager;
    private readonly ImInputString _partFilter = new(64);
    private bool _openRequested;
    private Stage _stage = Stage.ModeSelect;

    private enum Stage { ModeSelect, PartForm, BillboardForm }

    public InWorldLaunchUI(InWorldTerminalManager manager) => _manager = manager;

    /// <summary>Requests the launch popup open on the next <see cref="Draw"/> (from the menu).</summary>
    public void RequestOpen()
    {
        _openRequested = true;
        // Jump straight to the form for the already-chosen mode (Reconfigure), else
        // start at the mode picker.
        _stage = _manager.Settings.IsBillboard ? Stage.BillboardForm
               : _manager.IsActive             ? Stage.PartForm
               : Stage.ModeSelect;
    }

    /// <summary>Renders the popup/forms. Call every frame from the main context.</summary>
    public void Draw()
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        ImGui.SetNextWindowSize(new float2(470f, 0f), ImGuiCond.Appearing);
        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        switch (_stage)
        {
            case Stage.PartForm: DrawPartForm(); break;
            case Stage.BillboardForm: DrawBillboardForm(); break;
            default: DrawModeSelect(); break;
        }

        if (!open)
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawModeSelect()
    {
        ImGui.TextWrapped("Choose how the in-world terminal is anchored in 3D space.");
        ImGui.Spacing();

        if (ImGui.Button("Anchor to Vehicle Part", new float2(-1f, 0f)))
        {
            _manager.Settings.Mode = InWorldSettings.ModePart;
            _stage = Stage.PartForm;
            _manager.Enable(); // live preview while editing
        }

        ImGui.TextDisabled("Sits in the world, occludes / is occluded by parts.");
        ImGui.Spacing();

        if (ImGui.Button("Camera Billboard", new float2(-1f, 0f)))
        {
            _manager.Settings.Mode = InWorldSettings.ModeBillboard;
            _stage = Stage.BillboardForm;
            _manager.Enable();
        }

        ImGui.TextDisabled("Pinned in front of the camera as a HUD panel.");
    }

    private void DrawPartForm()
    {
        var s = _manager.Settings;
        ImGui.TextDisabled("Part-anchored — occludes / is occluded by the scene.");
        ImGui.Separator();

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

        DrawApplyRow();
    }

    private void DrawBillboardForm()
    {
        var s = _manager.Settings;
        ImGui.TextDisabled("Camera billboard — a HUD panel pinned to the view.");
        ImGui.Separator();

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

        DrawApplyRow();
    }

    private void DrawApplyRow()
    {
        ImGui.Separator();

        float availW = ImGui.GetContentRegionAvail().X;
        const float gap = 8f;
        float w = (availW - gap * 2f) / 3f;

        if (ImGui.Button("Apply", new float2(w, 0f)))
        {
            _manager.Settings.Save();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine(0f, gap);
        if (ImGui.Button("Back", new float2(w, 0f)))
        {
            _stage = Stage.ModeSelect;
        }

        ImGui.SameLine(0f, gap);
        if (ImGui.Button("Disable", new float2(w, 0f)))
        {
            _manager.Disable();
            ImGui.CloseCurrentPopup();
        }
    }

    private void DrawPartCombo(InWorldSettings s)
    {
        var vehicle = Program.ControlledVehicle;
        string current = string.IsNullOrEmpty(s.TargetPartId) ? "(auto: first part)" : s.TargetPartId;

        // Build the selectable list fresh each frame: the "(auto)" sentinel (empty
        // key) plus every top-level part and its sub-parts. Part ids can repeat
        // across instances of a template; FilterCombo disambiguates the ImGui id by
        // row index, and selecting any row with a given id sets that id (same
        // semantics as before — TargetPartId is a plain string id).
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
        if (ImGuiWidgets.FilterCombo("##inworld_part", current, _partFilter, items, out string? picked) && picked != null)
        {
            s.TargetPartId = picked;
        }

        if (vehicle == null)
        {
            ImGui.TextDisabled("No controlled vessel — anchor resolves when you take control.");
        }
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
