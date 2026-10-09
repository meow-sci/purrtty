using System.Text;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace ImGuiPlayground;

internal enum MockupScene { Camera, Parts, Tuning, Demo }

// Original, local-only UI exercises. The host owns the context; this instance owns fake state
// and writable UTF-8 buffers (never frame-scoped ImString pointers).
internal sealed class WidgetGallery
{
    private static readonly float4 Accent = new(0.36f, 0.78f, 0.92f, 1);
    private static readonly float4 Success = new(0.48f, 0.85f, 0.62f, 1);
    private static readonly float4 Muted = new(0.60f, 0.65f, 0.72f, 1);
    private readonly Keyframe[] _keyframes = [new("Launch pad", 3), new("Coastal pass", 5), new("Orbital reveal", 4)];
    private readonly Part[] _parts = [new("Capsule", "Crew", 1.8f, false), new("Solar wing", "Power", 0.3f, false),
        new("Landing leg", "Structure", 0.6f, true), new("Fuel tank", "Propulsion", 2.4f, false),
        new("Antenna", "Comms", 0.1f, true), new("Engine bell", "Propulsion", 1.2f, false)];
    private readonly byte[] _filter = Buffer("", 128);
    private readonly byte[] _notes = Buffer("Local shot notes. Try typing 100% here.\nNothing is saved or sent to the game.", 512);
    private readonly float[] _curve = new float[48];
    private readonly float[] _weights = new float[12];
    private MockupScene _scene;
    private bool _selectInitialTab = true;
    private bool _showDemo;
    private bool _demoVisibleLastFrame;
    private int _selectedKeyframe;
    private int _easing = 1;
    private int _cameraMode;
    private bool _playing;
    private bool _loop = true;
    private float _elapsed;
    private int _selectedPart;
    private bool _warningsOnly;
    private bool _requestReload;
    private bool _requestColorPicker;
    private int _reloadCount;
    private string _log = "Ready: 6 local fixtures, 2 simulated warnings.";
    private float _blend = 0.25f;
    private float _rate = 1.2f;
    private float _speed = 2.5f;
    private int _samples = 48;
    private bool _normalize = true;
    private float4 _color = new(0.36f, 0.78f, 0.92f, 1);

    internal WidgetGallery(MockupScene scene = MockupScene.Camera) => _scene = scene;
    internal bool IsPlaying => _playing;
    internal bool LoopEnabled => _loop;
    internal float2 PlayButtonCenter { get; private set; }
    internal float2 LoopCheckboxCenter { get; private set; }
    internal bool SawPartTree { get; private set; }
    internal bool SawReloadModal { get; private set; }
    internal bool SawColorPicker { get; private set; }
    internal void RequestReload() => _requestReload = true;
    internal void RequestColorPicker() => _requestColorPicker = true;
    internal void RequestDemo() => _showDemo = true;

    internal void Draw()
    {
        float2 viewport = ImGui.GetIO().DisplaySize;
        if (_scene == MockupScene.Demo)
        {
            bool open = true;
            DrawDemo(ref open, viewport);
            if (!open) _scene = MockupScene.Camera;
            return;
        }

        ImGui.SetNextWindowPos(new float2(8, 8));
        ImGui.SetNextWindowSize(viewport - new float2(16, 16));
        bool visible = ImGui.Begin("Widget mockups | BRUTAL ImGui"u8,
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.MenuBar);
        try
        {
            if (visible)
            {
                DrawMenu();
                Label("LOCAL WORKBENCH", Accent);
                ImGui.SameLine();
                ImGui.Checkbox("Show ImGui demo"u8, ref _showDemo);
                Label("Mock data only / no game connection / no persistence", Muted);
                if (ImGui.BeginTabBar("Mockup pages"u8))
                {
                    try
                    {
                        DrawTab("Camera sequencer", MockupScene.Camera, DrawCamera);
                        DrawTab("Parts workshop", MockupScene.Parts, DrawParts);
                        DrawTab("Tuning panel", MockupScene.Tuning, DrawTuning);
                    }
                    finally { ImGui.EndTabBar(); }
                }
                _selectInitialTab = false;
            }
        }
        finally { ImGui.End(); }
        if (_showDemo) DrawDemo(ref _showDemo, viewport);
        else _demoVisibleLastFrame = false;
    }

    private void DrawDemo(ref bool open, float2 viewport)
    {
        ImGui.ShowDemoWindow(ref open);
        if (!_demoVisibleLastFrame || ImGui.GetFrameCount() == 1)
        {
            // The native demo overrides SetNextWindow* with its own FirstUseEver defaults.
            // Correct the created window once per opening/context; later frames honor user moves.
            ImGui.SetWindowPos("Dear ImGui Demo"u8, new float2(8, 8));
            ImGui.SetWindowSize("Dear ImGui Demo"u8, viewport - new float2(16, 16));
        }
        _demoVisibleLastFrame = open;
    }

    private void DrawMenu()
    {
        if (!ImGui.BeginMenuBar()) return;
        try
        {
            if (ImGui.BeginMenu("Gallery"u8))
            {
                try
                {
                    if (ImGui.MenuItem("Rewind camera"u8)) { _elapsed = 0; _playing = false; }
                    if (ImGui.MenuItem("Reload parts fixtures..."u8)) { _scene = MockupScene.Parts; _selectInitialTab = true; RequestReload(); }
                    if (ImGui.MenuItem("Reset tuning"u8)) ResetTuning();
                    ImGui.Separator();
                    ImGui.MenuItem("Show ImGui demo"u8, default, ref _showDemo);
                }
                finally { ImGui.EndMenu(); }
            }
            if (ImGui.BeginMenu("Help"u8))
            {
                try { ImGui.Text("Scroll each page; right-click a part for actions."u8); }
                finally { ImGui.EndMenu(); }
            }
        }
        finally { ImGui.EndMenuBar(); }
    }

    private void DrawTab(string title, MockupScene scene, Action draw)
    {
        var flags = _selectInitialTab && _scene == scene ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        if (!ImGui.BeginTabItem(title, flags)) return;
        try
        {
            // The previously active tab can still submit once while SetSelected takes effect.
            if (!_selectInitialTab) _scene = scene;
            bool visible = ImGui.BeginChild(title, new float2(0, 0));
            try { if (visible) draw(); }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.EndTabItem(); }
    }

    private void DrawCamera()
    {
        float total = _keyframes.Sum(frame => frame.Duration);
        if (_playing)
        {
            _elapsed += Math.Min(ImGui.GetIO().DeltaTime, 0.1f) * _rate;
            if (_elapsed >= total)
            {
                if (_loop) _elapsed %= total;
                else { _elapsed = total; _playing = false; }
            }
        }
        _elapsed = Math.Clamp(_elapsed, 0, total);
        Label(_playing ? "PLAYING / simulated camera" : "PAUSED / simulated camera", _playing ? Success : Accent);
        ImGui.ProgressBar(_elapsed / total, new float2(-1, 0), $"{_elapsed:F1}s / {total:F1}s");
        ImGui.BeginDisabled(_playing);
        try { if (ImGui.Button("Play"u8)) _playing = true; PlayButtonCenter = ItemCenter(); }
        finally { ImGui.EndDisabled(); }
        ImGui.SameLine();
        ImGui.BeginDisabled(!_playing);
        try { if (ImGui.Button("Pause"u8)) _playing = false; }
        finally { ImGui.EndDisabled(); }
        ImGui.SameLine();
        if (ImGui.Button("Rewind"u8)) { _elapsed = 0; _playing = false; }
        ImGui.SameLine();
        ImGui.Checkbox("Loop"u8, ref _loop);
        LoopCheckboxCenter = ItemCenter();
        ImGui.SetItemTooltip("Loop the local timeline. No camera or game objects are touched."u8);
        ImGui.SetNextItemWidth(-100);
        ImGui.SliderFloat("Scrub (s)"u8, ref _elapsed, 0, total, "%.1f"u8);
        if (ImGui.BeginTable("Sequence editor"u8, 2, ImGuiTableFlags.SizingStretchSame))
        {
            try
            {
                ImGui.TableNextColumn();
                ImGui.SeparatorText("Keyframes"u8);
                if (ImGui.BeginListBox("##Keyframes"u8, new float2(-1, 115)))
                {
                    try
                    {
                        for (int i = 0; i < _keyframes.Length; ++i)
                        {
                            ImGui.PushID(i);
                            try
                            {
                                if (ImGui.Selectable(Read(_keyframes[i].Name), i == _selectedKeyframe)) _selectedKeyframe = i;
                            }
                            finally { ImGui.PopID(); }
                        }
                    }
                    finally { ImGui.EndListBox(); }
                }
                if (ImGui.TreeNodeEx("Shot summary"u8, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    try { ImGui.Text($"3 shots / {total:F1}s total"); ImGui.Text("Preview only, not a live feed."u8); }
                    finally { ImGui.TreePop(); }
                }
                ImGui.TableNextColumn();
                ImGui.SeparatorText("Selected shot"u8);
                Keyframe selected = _keyframes[_selectedKeyframe];
                ImGui.SetNextItemWidth(-95);
                ImGui.InputText("Name"u8, selected.Name);
                ImGui.SetNextItemWidth(-95);
                ImGui.DragFloat("Duration"u8, ref selected.Duration, 0.1f, 0.5f, 20, "%.1f s"u8, ImGuiSliderFlags.AlwaysClamp);
                ImGui.SetNextItemWidth(-95);
                ImGui.Combo("Easing"u8, ref _easing, "Linear\0Smoothstep\0Ease out\0\0"u8);
                ImGui.RadioButton("Orbit"u8, ref _cameraMode, 0);
                ImGui.SameLine();
                ImGui.RadioButton("Tracking"u8, ref _cameraMode, 1);
                ImGui.InputTextMultiline("##Shot notes"u8, _notes, new float2(-1, 72));
            }
            finally { ImGui.EndTable(); }
        }
    }

    private void DrawParts()
    {
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("Filter"u8, _filter);
        ImGui.SameLine();
        ImGui.Checkbox("Warnings only"u8, ref _warningsOnly);
        string filter = Read(_filter);
        if (ImGui.BeginTable("Parts"u8, 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
            new float2(0, 150)))
        {
            try
            {
                ImGui.TableSetupColumn("Part"u8, ImGuiTableColumnFlags.WidthStretch, 2);
                ImGui.TableSetupColumn("Category"u8);
                ImGui.TableSetupColumn("Mass (t)"u8);
                ImGui.TableSetupColumn("Status"u8);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                int shown = 0;
                for (int i = 0; i < _parts.Length; ++i)
                {
                    Part part = _parts[i];
                    if ((!part.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !part.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)) || (_warningsOnly && !part.Warning)) continue;
                    ++shown;
                    ImGui.PushID(i);
                    try
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        if (ImGui.Selectable(part.Name, i == _selectedPart)) _selectedPart = i;
                        ImGui.SetItemTooltip("Select to edit local stock; right-click for actions."u8);
                        if (ImGui.BeginPopupContextItem("Part actions"u8))
                        {
                            try
                            {
                                if (ImGui.MenuItem("Select part"u8)) _selectedPart = i;
                                if (ImGui.MenuItem("Toggle simulated warning"u8)) part.Warning = !part.Warning;
                            }
                            finally { ImGui.EndPopup(); }
                        }
                        ImGui.TableNextColumn(); ImGui.Text(part.Category);
                        ImGui.TableNextColumn(); ImGui.Text($"{part.Mass:F1}");
                        ImGui.TableNextColumn(); Label(part.Warning ? "Warning" : "Ready", part.Warning ? new float4(1, 0.72f, 0.32f, 1) : Success);
                    }
                    finally { ImGui.PopID(); }
                }
                if (shown == 0) { ImGui.TableNextRow(); ImGui.TableNextColumn(); ImGui.Text("No matching fixtures."u8); }
            }
            finally { ImGui.EndTable(); }
        }
        if (ImGui.TreeNodeEx("Selected part details"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            try
            {
                SawPartTree = true;
                Part selected = _parts[_selectedPart];
                ImGui.Text($"{selected.Name} / {selected.Category} / {selected.Mass:F1} t");
                ImGui.SetNextItemWidth(180);
                ImGui.InputInt("Local stock"u8, ref selected.Stock);
                selected.Stock = Math.Clamp(selected.Stock, 0, 999);
            }
            finally { ImGui.TreePop(); }
        }
        if (ImGui.Button("Reload fixtures..."u8)) RequestReload();
        ImGui.SameLine(); Label($"Reloads: {_reloadCount}", Muted);
        if (ImGui.CollapsingHeader("Local activity log"u8, ImGuiTreeNodeFlags.DefaultOpen)) ImGui.Text(_log);
        if (_requestReload) { ImGui.OpenPopup("Reload fixtures?"u8); _requestReload = false; }
        if (ImGui.BeginPopupModal("Reload fixtures?"u8, ImGuiWindowFlags.AlwaysAutoResize))
        {
            try
            {
                SawReloadModal = true;
                ImGui.Text("Reset local stock and warnings?"u8);
                ImGui.Text("This mock action cannot load game parts."u8);
                if (ImGui.Button("Confirm reload"u8))
                {
                    foreach (Part part in _parts) { part.Stock = 1; part.Warning = false; }
                    ++_reloadCount;
                    _log = $"Reload {_reloadCount}: all 6 fixtures ready (100% local).";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"u8)) ImGui.CloseCurrentPopup();
            }
            finally { ImGui.EndPopup(); }
        }
    }

    private void DrawTuning()
    {
        Label("Animation lab / synthetic signals", Accent);
        if (ImGui.Button("Reset tuning"u8)) ResetTuning();
        ImGui.SameLine();
        ImGui.Checkbox("Normalize weights"u8, ref _normalize);
        if (ImGui.BeginTable("Tuning grid"u8, 2, ImGuiTableFlags.SizingStretchSame))
        {
            try
            {
                ImGui.TableNextColumn();
                ImGui.SeparatorText("Resettable fields"u8);
                ImGui.SetNextItemWidth(-110);
                ImGui.DragFloat("Blend"u8, ref _blend, 0.01f, 0, 2, "%.2f s"u8, ImGuiSliderFlags.AlwaysClamp);
                ImGui.SetItemTooltip("Changes the synthetic response curve on the right."u8);
                ImGui.SetNextItemWidth(-110);
                ImGui.SliderFloat("Rate"u8, ref _rate, 0.1f, 4, "%.2f x"u8);
                ImGui.SetNextItemWidth(-110);
                ImGui.InputFloat("Speed"u8, ref _speed, 0.1f, 1, "%.2f"u8);
                _speed = Math.Clamp(_speed, 0, 10);
                ImGui.SetNextItemWidth(-110);
                ImGui.InputInt("Samples"u8, ref _samples, 1, 8);
                _samples = Math.Clamp(_samples, 8, _curve.Length);
                if (ImGui.CollapsingHeader("Preview color"u8, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.ColorEdit4("##Signal color"u8, ref _color);
                    if (ImGui.Button("Open color picker"u8)) RequestColorPicker();
                    if (_requestColorPicker) { ImGui.OpenPopup("Signal picker"u8); _requestColorPicker = false; }
                    if (ImGui.BeginPopup("Signal picker"u8))
                    {
                        try { SawColorPicker = true; ImGui.SetNextItemWidth(260); ImGui.ColorPicker4("##Picker"u8, ref _color); }
                        finally { ImGui.EndPopup(); }
                    }
                    Label("Plots use your preview color.", _color);
                }
                ImGui.TableNextColumn();
                ImGui.SeparatorText("Synthetic preview"u8);
                for (int i = 0; i < _samples; ++i)
                {
                    float t = i / (float)(_samples - 1);
                    _curve[i] = (1 - MathF.Exp(-t / Math.Max(_blend, 0.01f))) * (0.5f + 0.5f * MathF.Sin(t * _rate * MathF.PI));
                }
                float sum = 0;
                for (int i = 0; i < _weights.Length; ++i) { _weights[i] = 0.1f + MathF.Abs(MathF.Sin(i * 0.4f + _speed)); sum += _weights[i]; }
                if (_normalize) for (int i = 0; i < _weights.Length; ++i) _weights[i] /= sum;
                ImGui.PushStyleColor(ImGuiCol.PlotLines, _color);
                ImGui.PushStyleColor(ImGuiCol.PlotHistogram, _color);
                try
                {
                    ImGui.PlotLines("##Response"u8, _curve.AsSpan(0, _samples), overlayText: "Blend response"u8, scaleMin: 0, scaleMax: 1, graphSize: new float2(-1, 90));
                    ImGui.PlotHistogram("##Weights"u8, _weights, overlayText: _normalize ? "Normalized weights" : "Raw weights", scaleMin: 0, scaleMax: _normalize ? 0.2f : 1.2f, graphSize: new float2(-1, 90));
                }
                finally { ImGui.PopStyleColor(2); }
                ImGui.Text($"{_samples} samples / speed {_speed:F2}");
            }
            finally { ImGui.EndTable(); }
        }
    }

    private void ResetTuning()
    {
        _blend = 0.25f; _rate = 1.2f; _speed = 2.5f; _samples = 48; _normalize = true; _color = Accent;
    }

    private static float2 ItemCenter() => (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
    private static void Label(string text, float4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        // The selected BRUTAL ImGui.Text overload calls native TextUnformatted, not printf.
        try { ImGui.Text(text); }
        finally { ImGui.PopStyleColor(); }
    }
    private static byte[] Buffer(string text, int capacity)
    {
        byte[] buffer = new byte[capacity];
        Encoding.UTF8.GetBytes(text, buffer.AsSpan(0, capacity - 1));
        return buffer;
    }
    private static string Read(byte[] buffer) => Encoding.UTF8.GetString(buffer.AsSpan(0, Array.IndexOf(buffer, (byte)0)));
    private sealed class Keyframe(string name, float duration)
    {
        internal readonly byte[] Name = Buffer(name, 128);
        internal float Duration = duration;
    }
    private sealed class Part(string name, string category, float mass, bool warning)
    {
        internal readonly string Name = name;
        internal readonly string Category = category;
        internal readonly float Mass = mass;
        internal bool Warning = warning;
        internal int Stock = 1;
    }
}
