using System.Collections.Generic;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.GameMod.InWorld.Settings;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld.UI;

/// <summary>
///     Unified settings window for the in-world (render-to-texture) terminal.
///     Holds a reference to the manager and settings, mutates settings in-place,
///     and never touches Vulkan / ImGui contexts other than the main one this
///     Render() call is invoked from.
/// </summary>
public sealed class InWorldSettingsWindow
{
    private const string WindowTitle = "purrTTY In-World Terminal##purrtty_inworld_settings";

    private readonly InWorldTerminalManager _manager;
    private readonly InWorldSettings _settings;

    // Combo selection state. -1 means "no selection". Indices reference the
    // arrays we rebuild at the top of each Render() call from the live vehicle.
    private int _vehicleSelectedIdx = -1;
    private int _partSelectedIdx = -1;
    private int _subPartSelectedIdx = -1; // 0 == "(none — select to anchor)" sentinel

    // Per-combo filter buffers. Allocated once and reused so typed text
    // survives across frames while the popup is open.
    private readonly ImInputString _vehicleFilter = new ImInputString(64);
    private readonly ImInputString _partFilter = new ImInputString(64);
    private readonly ImInputString _subPartFilter = new ImInputString(64);

    private bool _isOpen;

    public InWorldSettingsWindow(InWorldTerminalManager manager, InWorldSettings settings)
    {
        _manager = manager;
        _settings = settings;
    }

    public bool IsOpen
    {
        get => _isOpen;
        set => _isOpen = value;
    }

    public void Toggle()
    {
        _isOpen = !_isOpen;
    }

    /// <summary>
    ///     Renders the window when open. No-op when closed. Call from a main-
    ///     context per-frame UI hook; the close button updates the open flag
    ///     through the <see cref="ImGui.Begin(ImString, ref bool, ImGuiWindowFlags)"/>
    ///     ref overload.
    /// </summary>
    public void Render()
    {
        if (!_isOpen) return;

        ImGui.SetNextWindowSize(new float2(520f, 520f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(WindowTitle, ref _isOpen, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        try
        {
            DrawStatusSection();
            DrawTargetSubPartSection();
            DrawQuadTransformSection();
            DrawUvSection();
            DrawMeshSection();
            DrawGeometryDiagnosticsSection();
        }
        finally
        {
            ImGui.End();
        }
    }

    private void DrawStatusSection()
    {
        ImGui.SeparatorText("Status");

        bool hasCamera = Program.GetMainCamera() != null;
        bool anchorResolves = _manager.CanResolveAnchor();
        string anchorIdDisplay = string.IsNullOrEmpty(_settings.TargetPartName)
            ? "(not selected)"
            : _settings.TargetPartName + (anchorResolves ? "" : " — NOT on controlled vessel");

        // Pre-format every line to a plain `string` local. ImGui.Text bound to
        // an interpolated literal would resolve to the String8 overload (via
        // ImString's InterpolatedStringHandler) and fail to compile.
        string enabledLine     = "Enabled:        " + (_settings.Enabled ? "yes" : "no");
        string initLine        = "Initialized:    " + (_manager.IsInitialized ? "yes" : "no");
        string anchoredLine    = "Quad anchored:  " + (_manager.IsQuadAnchored ? "yes" : "no");
        string focusedLine     = "Focused:        " + (_manager.IsFocused ? "yes" : "no");
        string cameraLine      = "Active camera:  " + (hasCamera ? "yes" : "no — F11 will reject");
        string anchorLine      = "Anchor part:    " + anchorIdDisplay;

        ImGui.Text(enabledLine);
        ImGui.Text(initLine);
        ImGui.Text(anchoredLine);
        ImGui.Text(focusedLine);
        ImGui.Text(cameraLine);
        ImGui.Text(anchorLine);

        // Toggle button: when turning ON, the manager rejects unless a SubPart
        // is selected AND it resolves to a real part on the controlled vessel.
        // Greying out the button gives immediate visual feedback so the user
        // doesn't press F11 and silently get nothing.
        bool toggleDisabled = _settings.Enabled
            ? false                                    // already on — always allow toggle off
            : (!hasCamera || !anchorResolves);
        if (toggleDisabled) ImGui.BeginDisabled();
        string toggleLabel = _settings.Enabled
            ? "Disable Rendering (F11)"
            : "Toggle Rendering (F11)";
        if (ImGui.Button(toggleLabel))
        {
            _manager.Toggle();
        }
        if (toggleDisabled) ImGui.EndDisabled();

        // Hovering a disabled button still fires IsItemHovered, so the user
        // gets a tooltip explaining what to fix.
        if (toggleDisabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (!hasCamera)
            {
                ImGui.SetTooltip("No active camera — load into flight first.");
            }
            else if (string.IsNullOrEmpty(_settings.TargetPartName))
            {
                ImGui.SetTooltip("Pick a Vehicle / Part / SubPart in the section below first.");
            }
            else
            {
                ImGui.SetTooltip(
                    "Selected SubPart is not on the controlled vessel right now.");
            }
        }
    }

    private void DrawTargetSubPartSection()
    {
        ImGui.SeparatorText("Target SubPart");

        Vehicle? vehicle = Program.ControlledVehicle;

        // Vehicle combo: scoped to the controlled vessel. Keeping it as a
        // combo (rather than plain text) preserves layout consistency for the
        // future case where multiple vessels are exposed.
        var vehicles = new List<Vehicle>();
        if (vehicle != null) vehicles.Add(vehicle);
        _vehicleSelectedIdx = vehicles.Count > 0 ? 0 : -1;

        ImGui.Text("Vehicle");
        ImGui.SameLine(120f);
        ImGui.SetNextItemWidth(-1);
        FilterableCombo.Render(
            "##inworld_vehicle_combo",
            vehicles,
            v => v.Id,
            ref _vehicleSelectedIdx,
            _vehicleFilter);

        // Part (root) combo: rebuild list every frame so vessel reloads or
        // part add/remove are reflected without needing a manual refresh.
        var parts = new List<Part>();
        if (vehicle != null)
        {
            foreach (Part p in vehicle.Parts.Parts) parts.Add(p);
        }
        SyncPartSelection(parts);

        ImGui.Text("Part");
        ImGui.SameLine(120f);
        ImGui.SetNextItemWidth(-1);
        FilterableCombo.Render(
            "##inworld_part_combo",
            parts,
            p => p.Id,
            ref _partSelectedIdx,
            _partFilter);

        // SubPart combo. Index 0 is the "(none — select to anchor)" sentinel
        // that clears TargetPartName (and so disables the toggle). Indices 1..
        // are the SubParts of the selected root part, plus the root itself as
        // the second entry (some "root" parts have anchor surfaces too).
        var subPartLabels = new List<string>();
        var subPartIds = new List<string>();
        subPartLabels.Add("(none — select to anchor)");
        subPartIds.Add("");
        if (_partSelectedIdx >= 0 && _partSelectedIdx < parts.Count)
        {
            Part rootPart = parts[_partSelectedIdx];
            subPartLabels.Add(rootPart.Id + " (root)");
            subPartIds.Add(rootPart.Id);
            foreach (Part sub in rootPart.SubParts)
            {
                subPartLabels.Add(sub.Id);
                subPartIds.Add(sub.Id);
            }
        }
        SyncSubPartSelection(subPartIds);

        ImGui.Text("SubPart");
        ImGui.SameLine(120f);
        ImGui.SetNextItemWidth(-1);
        if (FilterableCombo.Render(
                "##inworld_subpart_combo",
                subPartLabels,
                s => s,
                ref _subPartSelectedIdx,
                _subPartFilter))
        {
            string newId = (_subPartSelectedIdx >= 0 && _subPartSelectedIdx < subPartIds.Count)
                ? subPartIds[_subPartSelectedIdx]
                : "";
            _settings.TargetPartName = newId;
        }
    }

    private void DrawQuadTransformSection()
    {
        ImGui.SeparatorText("Quad transform (SubPart-local)");

        // Position and rotation are interpreted in the chosen SubPart's local
        // frame. The quad first rotates about its own center, then translates
        // by the offset, then the whole thing is brought into ego space by the
        // SubPart's pose. So small Y offsets typically push the quad along the
        // SubPart's local +Y axis (often "up from the surface"), and a small
        // Z offset slides it along the SubPart's normal direction.
        float px = _settings.AnchorOffsetX;
        float py = _settings.AnchorOffsetY;
        float pz = _settings.AnchorOffsetZ;
        float rx = _settings.AnchorRotationX;
        float ry = _settings.AnchorRotationY;
        float rz = _settings.AnchorRotationZ;

        DrawSliderRow("Position X (m)", "##qpx", ref px, 0.005f, -50f, 50f, "%.3f");
        DrawSliderRow("Position Y (m)", "##qpy", ref py, 0.005f, -50f, 50f, "%.3f");
        DrawSliderRow("Position Z (m)", "##qpz", ref pz, 0.005f, -50f, 50f, "%.3f");

        DrawSliderRow("Rotation X (deg)", "##qrx", ref rx, 0.5f, -360f, 360f, "%.1f");
        DrawSliderRow("Rotation Y (deg)", "##qry", ref ry, 0.5f, -360f, 360f, "%.1f");
        DrawSliderRow("Rotation Z (deg)", "##qrz", ref rz, 0.5f, -360f, 360f, "%.1f");

        _settings.AnchorOffsetX = px;
        _settings.AnchorOffsetY = py;
        _settings.AnchorOffsetZ = pz;
        _settings.AnchorRotationX = rx;
        _settings.AnchorRotationY = ry;
        _settings.AnchorRotationZ = rz;

        if (ImGui.Button("Reset Transform"))
        {
            _settings.AnchorOffsetX = 0f;
            _settings.AnchorOffsetY = 0f;
            _settings.AnchorOffsetZ = 0f;
            _settings.AnchorRotationX = 0f;
            _settings.AnchorRotationY = 0f;
            _settings.AnchorRotationZ = 0f;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("Edits apply live — no re-anchor needed.");
    }

    private void SyncPartSelection(List<Part> parts)
    {
        if (parts.Count == 0)
        {
            _partSelectedIdx = -1;
            return;
        }

        if (string.IsNullOrEmpty(_settings.TargetPartName))
        {
            if (_partSelectedIdx < 0 || _partSelectedIdx >= parts.Count)
            {
                _partSelectedIdx = 0;
            }
            return;
        }

        // First try: the currently-bound name IS a root part Id.
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Id == _settings.TargetPartName)
            {
                _partSelectedIdx = i;
                return;
            }
        }
        // Second try: the bound name is a SubPart of one of the root parts.
        for (int i = 0; i < parts.Count; i++)
        {
            foreach (Part sub in parts[i].SubParts)
            {
                if (sub.Id == _settings.TargetPartName)
                {
                    _partSelectedIdx = i;
                    return;
                }
            }
        }

        if (_partSelectedIdx < 0 || _partSelectedIdx >= parts.Count)
        {
            _partSelectedIdx = 0;
        }
    }

    private void SyncSubPartSelection(List<string> subPartIds)
    {
        if (subPartIds.Count == 0)
        {
            _subPartSelectedIdx = -1;
            return;
        }

        // Index 0 is always the sentinel; map empty TargetPartName to it.
        if (string.IsNullOrEmpty(_settings.TargetPartName))
        {
            _subPartSelectedIdx = 0;
            return;
        }

        for (int i = 0; i < subPartIds.Count; i++)
        {
            if (subPartIds[i] == _settings.TargetPartName)
            {
                _subPartSelectedIdx = i;
                return;
            }
        }

        // The bound name does not belong to the currently-selected root part
        // (this happens during the brief window after a part-combo change, or
        // after a vessel reload). Fall back to the sentinel so the user sees
        // the inconsistency.
        _subPartSelectedIdx = 0;
    }

    private void DrawUvSection()
    {
        ImGui.SeparatorText("Texture render rect");

        // The UV sliders drive the offscreen render-rect: the terminal renders
        // pixel-clean (no font scaling) into the sub-rect of the texture defined
        // here, and the rest of the texture stays at the renderpass's clear
        // color (opaque black). Smaller UV Size = smaller terminal with the
        // black mat filling the remainder. Offset shifts where in the texture
        // the terminal lands.
        float ou = _settings.UvOffsetU;
        float ov = _settings.UvOffsetV;
        float su = _settings.UvSizeU;
        float sv = _settings.UvSizeV;

        DrawSliderRow("UV Offset U", "##uvOffU", ref ou, 0.005f, 0.0f, 1.0f, "%.3f");
        DrawSliderRow("UV Offset V", "##uvOffV", ref ov, 0.005f, 0.0f, 1.0f, "%.3f");
        DrawSliderRow("UV Size U",   "##uvSizU", ref su, 0.005f, 0.05f, 1.0f, "%.3f");
        DrawSliderRow("UV Size V",   "##uvSizV", ref sv, 0.005f, 0.05f, 1.0f, "%.3f");

        _settings.UvOffsetU = ou;
        _settings.UvOffsetV = ov;
        _settings.UvSizeU   = su;
        _settings.UvSizeV   = sv;

        if (ImGui.Button("Reset UV"))
        {
            _settings.UvOffsetU = 0.0f;
            _settings.UvOffsetV = 0.0f;
            _settings.UvSizeU   = 1.0f;
            _settings.UvSizeV   = 1.0f;
        }
    }

    private void DrawMeshSection()
    {
        ImGui.SeparatorText("Quad size");

        float w = _settings.QuadWidthMeters;
        float h = _settings.QuadHeightMeters;

        DrawSliderRow("Width (m)",  "##qw", ref w, 0.01f, 0.05f, 20.0f, "%.2f");
        DrawSliderRow("Height (m)", "##qh", ref h, 0.01f, 0.05f, 20.0f, "%.2f");

        _settings.QuadWidthMeters  = w;
        _settings.QuadHeightMeters = h;

        if (ImGui.Button("Reset Size"))
        {
            _settings.QuadWidthMeters  = 1.6f;
            _settings.QuadHeightMeters = 1.0f;
        }
    }

    private static void DrawSliderRow(string label, string sliderId, ref float value, float speed, float min, float max, string format)
    {
        ImGui.Text(label);
        ImGui.SameLine(140f);
        ImGui.SetNextItemWidth(-1);
        ImGui.DragFloat(sliderId, ref value, speed, min, max, format);
    }

    private void DrawGeometryDiagnosticsSection()
    {
        if (!ImGui.CollapsingHeader("Geometry diagnostics"))
        {
            return;
        }

        int sampW = (int)(_settings.UvSizeU * _settings.TextureWidth  + 0.5f);
        int sampH = (int)(_settings.UvSizeV * _settings.TextureHeight + 0.5f);
        string textureLine  = "Texture size:    " + _settings.TextureWidth + "×" + _settings.TextureHeight;
        string sampledLine  = "Sampled region:  " + sampW + "×" + sampH + " px"
                            + "  @ uv (" + _settings.UvOffsetU.ToString("0.00")
                            + ", "       + _settings.UvOffsetV.ToString("0.00") + ")";

        ImGui.Text(textureLine);
        ImGui.Text(sampledLine);
    }
}
