using System.Collections.Generic;
using Brutal.ImGuiApi;
using KSA;
using purrTTY.GameMod.InWorld.Display;
using purrTTY.GameMod.InWorld.Settings;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.GameMod.InWorld.UI;

/// <summary>
///     Unified settings window for the in-world (render-to-texture) terminal.
///     Replaces the old per-menu pickers with a single resizable window so the
///     user can browse long part lists without the menu collapsing on every
///     click. Pure UI: holds a reference to the manager and settings, mutates
///     settings in-place, and never touches Vulkan / ImGui contexts other than
///     the main one this Render() call is invoked from.
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
    private int _subPartSelectedIdx = -1; // 0 == "(quad only — no SubPart)" sentinel

    // Per-combo filter buffers. Allocated once and reused so typed text
    // survives across frames while the popup is open.
    private readonly ImInputString _vehicleFilter = new ImInputString(64);
    private readonly ImInputString _partFilter = new ImInputString(64);
    private readonly ImInputString _subPartFilter = new ImInputString(64);
    private readonly ImInputString _modeFilter = new ImInputString(64);

    // Display labels for the override mode combo, indexed by (int)OverrideMode.
    // Kept in source order to match the enum's declaration order so
    // ModeLabels[(int)Mode] gives the right text.
    private static readonly string[] ModeLabels =
    {
        "Per-template (all instances)",
        "Per-instance overlay (one only)",
    };

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

        ImGui.SetNextWindowSize(new float2(520f, 420f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(WindowTitle, ref _isOpen, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        try
        {
            DrawStatusSection();
            DrawTargetSubPartSection();
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

        // Pre-format every line to a plain `string` local. ImGui.Text bound to
        // an interpolated literal would resolve to the String8 overload (via
        // ImString's InterpolatedStringHandler) and fail to compile.
        string enabledLine     = "Enabled:        " + (_settings.Enabled ? "yes" : "no");
        string initLine        = "Initialized:    " + (_manager.IsInitialized ? "yes" : "no");
        string anchoredLine    = "Quad anchored:  " + (_manager.IsQuadAnchored ? "yes" : "no");
        string focusedLine     = "Focused:        " + (_manager.IsFocused ? "yes" : "no");
        string cameraLine      = "Active camera:  " + (hasCamera ? "yes" : "no — F11 will reject");
        string subpartLine     = "SubPart active: " + (_manager.HasSubPartOverride ? "yes" : "no");

        ImGui.Text(enabledLine);
        ImGui.Text(initLine);
        ImGui.Text(anchoredLine);
        ImGui.Text(focusedLine);
        ImGui.Text(cameraLine);
        ImGui.Text(subpartLine);

        // Disable the toggle button only when the user could not possibly
        // succeed (no camera AND not currently enabled — already-on can still
        // be toggled off). The manager itself enforces the same rule, but
        // greying-out gives immediate visual feedback.
        bool toggleDisabled = !hasCamera && !_settings.Enabled;
        if (toggleDisabled) ImGui.BeginDisabled();
        string toggleLabel = _settings.Enabled
            ? "Disable Rendering (F11)"
            : "Toggle Rendering (F11)";
        if (ImGui.Button(toggleLabel))
        {
            _manager.Toggle();
        }
        if (toggleDisabled) ImGui.EndDisabled();

        ImGui.SameLine();

        bool reanchorDisabled = !hasCamera || !_settings.Enabled;
        if (reanchorDisabled) ImGui.BeginDisabled();
        if (ImGui.Button("Re-anchor Quad"))
        {
            _manager.ReanchorQuad();
        }
        if (reanchorDisabled) ImGui.EndDisabled();
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
        // Default the selected root part to whichever owns the current
        // TargetPartName SubPart (or matches it directly). If nothing matches
        // and we have no prior selection, fall back to the first part so the
        // SubPart combo has something meaningful to populate.
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

        // SubPart combo. Index 0 is the "(quad only)" sentinel that clears
        // TargetPartName. Indices 1.. are the SubParts of the selected root
        // part. We build a parallel display list so the combo helper stays
        // generic over <T>.
        var subPartLabels = new List<string>();
        var subPartIds = new List<string>();
        subPartLabels.Add("(quad only — no SubPart)");
        subPartIds.Add("");
        if (_partSelectedIdx >= 0 && _partSelectedIdx < parts.Count)
        {
            Part rootPart = parts[_partSelectedIdx];
            // Honest UI: count instances per template Id across the vessel so
            // the user can see when picking "engine_thrust" will (per-template
            // mode) bleed onto every other engine_thrust on the ship. The
            // suffix is a hint, not a constraint — picking any of the matching
            // instances resolves to the same shared PartModel anyway.
            var templateCounts = CountInstancesPerTemplate(vehicle);

            // Include the root itself as a selectable SubPart (the old picker
            // behaved the same way — root parts can be the override target).
            subPartLabels.Add(BuildSubPartLabel(rootPart, templateCounts) + " (root)");
            subPartIds.Add(rootPart.Id);
            foreach (Part sub in rootPart.SubParts)
            {
                subPartLabels.Add(BuildSubPartLabel(sub, templateCounts));
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
            if (newId != _settings.TargetPartName)
            {
                _settings.TargetPartName = newId;
                _manager.RebindSubPart();
            }
        }

        // Override mode selector. Switching modes tears down and rebuilds the
        // override under the new strategy via RebindSubPart, so the change
        // takes effect on the next frame without needing a manual toggle.
        var modeLabels = ModeLabels;
        int modeIdx = (int)_settings.TargetOverrideMode;
        if (modeIdx < 0 || modeIdx >= modeLabels.Length) modeIdx = 0;

        ImGui.Text("Mode");
        ImGui.SameLine(120f);
        ImGui.SetNextItemWidth(-1);
        if (FilterableCombo.Render(
                "##inworld_mode_combo",
                modeLabels,
                s => s,
                ref modeIdx,
                _modeFilter))
        {
            var newMode = (OverrideMode)modeIdx;
            if (newMode != _settings.TargetOverrideMode)
            {
                _settings.TargetOverrideMode = newMode;
                _manager.RebindSubPart();
            }
        }
    }

    private static Dictionary<string, int> CountInstancesPerTemplate(Vehicle? vehicle)
    {
        var counts = new Dictionary<string, int>();
        if (vehicle == null) return counts;

        foreach (Part p in vehicle.Parts.Parts)
        {
            BumpTemplate(counts, p);
            foreach (Part sub in p.SubParts)
            {
                BumpTemplate(counts, sub);
            }
        }
        return counts;
    }

    private static void BumpTemplate(Dictionary<string, int> counts, Part part)
    {
        // Match the override's "shared PartModel" key: PartModel.Get(template)
        // dedups by Template.Id, so we count by the same Id. A part with no
        // PartModelModule contributes nothing — it can't be overridden anyway.
        Span<PartModelModule> modules = part.Modules.Get<PartModelModule>();
        if (modules.Length == 0) return;
        string templateId = modules[0].PartModel?.Template?.Id ?? "";
        if (string.IsNullOrEmpty(templateId)) return;
        counts[templateId] = counts.TryGetValue(templateId, out int existing) ? existing + 1 : 1;
    }

    private static string BuildSubPartLabel(Part part, Dictionary<string, int> templateCounts)
    {
        Span<PartModelModule> modules = part.Modules.Get<PartModelModule>();
        if (modules.Length == 0) return part.Id;
        string templateId = modules[0].PartModel?.Template?.Id ?? "";
        if (string.IsNullOrEmpty(templateId)) return part.Id;
        if (templateCounts.TryGetValue(templateId, out int count) && count > 1)
        {
            return part.Id + " (" + count + " instances)";
        }
        return part.Id;
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
        // (this happens during the brief window after a part-combo change).
        // Fall back to the sentinel so the user sees the inconsistency.
        _subPartSelectedIdx = 0;
    }

    private void DrawUvSection()
    {
        ImGui.SeparatorText("Texture render rect");

        // The UV sliders drive the offscreen render-rect: the terminal renders
        // pixel-clean (no font scaling) into the sub-rect of the texture defined
        // here, and the rest of the texture stays at the renderpass's clear
        // color (opaque black). Both the quad and the subpart sample the same
        // texture, so smaller UV Size = smaller terminal on BOTH surfaces with
        // the black mat filling the remainder. Offset shifts where in the
        // texture the terminal lands.
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
        ImGui.SeparatorText("Mesh size");

        float w = _settings.QuadWidthMeters;
        float h = _settings.QuadHeightMeters;
        float d = _settings.QuadDistanceMeters;

        DrawSliderRow("Width (m)",    "##qw", ref w, 0.01f, 0.05f, 20.0f, "%.2f");
        DrawSliderRow("Height (m)",   "##qh", ref h, 0.01f, 0.05f, 20.0f, "%.2f");
        DrawSliderRow("Distance (m)", "##qd", ref d, 0.01f, 0.10f, 50.0f, "%.2f");

        _settings.QuadWidthMeters    = w;
        _settings.QuadHeightMeters   = h;
        _settings.QuadDistanceMeters = d;

        if (ImGui.Button("Reset Mesh"))
        {
            _settings.QuadWidthMeters    = 1.6f;
            _settings.QuadHeightMeters   = 1.0f;
            _settings.QuadDistanceMeters = 2.0f;
        }

        ImGui.SameLine();
        ImGui.Text("Distance applies on next Re-anchor; width/height live.");

        // On the quad, Width/Height are a physical size in meters and Distance
        // is ego-space placement. On a subpart, the same Width and Height are
        // reused as a scale factor pre-multiplied onto each appended instance's
        // ModelMatrix, so the textured face grows / shrinks on the part. In
        // PerTemplate mode every instance of the template scales together
        // (they all share the override anyway); in PerInstanceOverlay mode only
        // the one overlay instance scales and the underlying part keeps its
        // original transform. Defaults are 1.6×1.0 (tuned for the quad's 16:10
        // panel); reset to 1.0×1.0 to leave a subpart at its native size.
        if (_manager.HasSubPartOverride)
        {
            if (_settings.TargetOverrideMode == OverrideMode.PerInstanceOverlay)
            {
                ImGui.TextDisabled("SubPart overlay active — Width/Height scale only the overlay instance.");
            }
            else
            {
                ImGui.TextDisabled("SubPart per-template active — Width/Height scale every instance of the template.");
            }
        }
    }

    private static void DrawSliderRow(string label, string sliderId, ref float value, float speed, float min, float max, string format)
    {
        ImGui.Text(label);
        ImGui.SameLine(120f);
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
