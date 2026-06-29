using Brutal.ImGuiApi;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using PurrTTY.Terminal.Rendering;
using float2 = Brutal.Numerics.float2;
using float3 = Brutal.Numerics.float3;
using float4 = Brutal.Numerics.float4;

namespace purrTTY.GameMod.UI;

/// <summary>
///     The per-terminal theme manager dialog: pick a target terminal (any registered
///     2D window — and, once they register, in-world instances — defaulting to the
///     focused one) and edit its complete appearance bundle: palette, font family/
///     size, the three opacities, and the advanced cursor/border/lock/hot-zone
///     settings. Also renames the target and saves/loads/deletes named themes.
///     Replaces the old scattered Theme/Font/Focus menus and the Window-menu opacity
///     sliders; all edits apply to the <b>selected</b> target (not just the focused
///     one) and persist as the new-window defaults via the controller.
/// </summary>
public sealed class ThemeDialog
{
    private const string PopupId = "purrTTY Theme##purrtty_theme_dialog";

    private static readonly float4 WarnColor = new(1f, 0.8f, 0.3f, 1f);
    private static readonly float4 ErrorColor = new(1f, 0.4f, 0.4f, 1f);

    private static readonly (string Label, HotZonePlacement Placement)[] HotZonePlacements =
    {
        ("Top Left", HotZonePlacement.TopLeft),
        ("Top Center", HotZonePlacement.TopCenter),
        ("Top Right", HotZonePlacement.TopRight),
        ("Middle Left", HotZonePlacement.MiddleLeft),
        ("Middle Right", HotZonePlacement.MiddleRight),
        ("Bottom Left", HotZonePlacement.BottomLeft),
        ("Bottom Center", HotZonePlacement.BottomCenter),
        ("Bottom Right", HotZonePlacement.BottomRight),
    };

    private static readonly (string Label, CursorShape Shape)[] CursorStyles =
    {
        ("Block", CursorShape.Block),
        ("Bar", CursorShape.Bar),
        ("Underline", CursorShape.Underline),
    };

    private readonly GhosttyTerminalController _controller;

    private readonly ImInputString _targetFilter = new(64);
    private readonly ImInputString _paletteFilter = new(64);
    private readonly ImInputString _fontFilter = new(64);
    private readonly ImInputString _deleteFilter = new(64);
    private readonly ImInputString _renameInput = new(64);
    private readonly ImInputString _saveNameInput = new(96);

    private bool _openRequested;
    private string? _selectedName; // the chosen target's Name; re-resolved each frame
    private bool _showAdvanced;
    private string? _renameError;
    private string? _saveError;
    private bool _hotZoneColorDirty;

    public ThemeDialog(GhosttyTerminalController controller) => _controller = controller;

    /// <summary>Requests the dialog open on the next <see cref="Render"/>, targeting the focused terminal.</summary>
    public void RequestOpen()
    {
        _openRequested = true;
        // The dialog modal itself takes ImGui focus, so registry.Focused is null
        // while it is open; seed from the controller's focused-or-last-or-first
        // window so the default target is the terminal the user was last using.
        _selectedName = _controller.FocusTarget?.Name ?? TerminalTargetRegistry.Focused?.Name;
        _renameError = null;
        _saveError = null;
        _renameInput.Clear();
        _saveNameInput.Clear();
    }

    /// <summary>Draws the dialog. Returns true while the modal is open (so the host suppresses the toggle hotkey).</summary>
    public bool Render()
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(PopupId);
        }

        ImGui.SetNextWindowSize(new float2(580f, 0f), ImGuiCond.Appearing);
        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            return false;
        }

        var target = ResolveTarget();
        if (target == null)
        {
            ImGui.TextDisabled("No terminal is open. Open a terminal, then reopen this dialog.");
        }
        else
        {
            DrawTargetRow(target);

            if (target is TerminalWindow window)
            {
                ImGui.Separator();
                DrawPalette(window);
                ImGui.Separator();
                DrawFont(window);
                ImGui.Separator();
                DrawOpacity(window);
                ImGui.Separator();
                DrawAdvancedToggle(window);
            }
            else
            {
                // In-world instances apply themes (phase 8) but not the 2D-window
                // granular editor; nothing else is registered yet, so this is a guard.
                ImGui.Separator();
                ImGui.TextDisabled("This terminal type can only be themed by applying a saved theme.");
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Close", new float2(-1f, 0f)) || !open)
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
        return true;
    }

    private INamedTerminal? ResolveTarget()
    {
        var selected = _selectedName != null ? TerminalTargetRegistry.Resolve(_selectedName) : null;
        if (selected == null)
        {
            var all = TerminalTargetRegistry.All;
            selected = _controller.FocusTarget ?? TerminalTargetRegistry.Focused ?? (all.Count > 0 ? all[0] : null);
            _selectedName = selected?.Name;
        }

        return selected;
    }

    private void DrawTargetRow(INamedTerminal target)
    {
        var items = new List<(string Key, string Label)>();
        var all = TerminalTargetRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            string star = t.HasFocus ? "  *focused" : string.Empty;
            items.Add((t.Name, $"{t.Name}  ({KindLabel(t.Kind)}){star}"));
        }

        ImGui.Text("Terminal");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGuiWidgets.FilterCombo("##theme_target", $"{target.Name}  ({KindLabel(target.Kind)})",
                _targetFilter, items, out string? picked) && picked != null)
        {
            _selectedName = picked;
        }

        if (ImGui.Button("Use focused terminal", new float2(-1f, 0f)))
        {
            _selectedName = _controller.FocusTarget?.Name ?? TerminalTargetRegistry.Focused?.Name;
        }

        ImGui.Text("Rename to");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##theme_rename", _renameInput, ImGuiInputTextFlags.None);
        _renameInput.EvaluateLength();

        if (ImGui.Button("Rename", new float2(-1f, 0f)))
        {
            string newName = _renameInput.ToString().Trim();
            if (target.TryRename(newName))
            {
                _selectedName = target.Name;
                _renameInput.Clear();
                _renameError = null;
            }
            else
            {
                _renameError = newName.Length == 0 ? "Enter a name." : $"The name '{newName}' is already taken.";
            }
        }

        if (_renameError != null)
        {
            ImGui.TextColored(WarnColor, _renameError);
        }
    }

    private void DrawPalette(TerminalWindow window)
    {
        ImGui.TextDisabled("Theme");

        var items = new List<(string Key, string Label)>();
        foreach (var theme in _controller.Catalog.BuiltInThemes)
        {
            items.Add((theme.Name, theme.Name));
        }

        foreach (var theme in _controller.Catalog.UserThemes)
        {
            items.Add((theme.Name, $"{theme.Name}  (saved)"));
        }

        ImGui.Text("Palette");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGuiWidgets.FilterCombo("##theme_palette", window.Settings.ThemeName, _paletteFilter, items, out string? pickedTheme)
            && pickedTheme != null
            && _controller.Catalog.Find(pickedTheme) is { } def)
        {
            window.ApplyTheme(def);
            _controller.PersistDisplayDefaults(window);
        }

        // Save the selected window's full appearance as a named user theme.
        ImGui.Text("Save as");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##theme_saveas", _saveNameInput, ImGuiInputTextFlags.None);
        _saveNameInput.EvaluateLength();
        string saveName = _saveNameInput.ToString().Trim();

        bool canSave = saveName.Length > 0;
        if (!canSave)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Save current settings as theme", new float2(-1f, 0f)) && canSave)
        {
            try
            {
                var saved = _controller.Catalog.SaveUserTheme(window.SnapshotAsTheme(saveName));
                window.Settings.ThemeName = saved.Name;
                _controller.PersistDisplayDefaults(window);
                _saveNameInput.Clear();
                _saveError = null;
            }
            catch (Exception ex)
            {
                _saveError = $"Save failed: {ex.Message}";
            }
        }

        if (!canSave)
        {
            ImGui.EndDisabled();
        }

        if (canSave && _controller.Catalog.UserThemeExists(saveName))
        {
            ImGui.TextColored(WarnColor, $"A saved theme named '{saveName}' will be overwritten.");
        }

        if (_saveError != null)
        {
            ImGui.TextColored(ErrorColor, _saveError);
        }

        // Delete a saved theme (built-ins cannot be deleted).
        if (_controller.Catalog.UserThemes.Count > 0)
        {
            var delItems = new List<(string Key, string Label)>();
            foreach (var theme in _controller.Catalog.UserThemes)
            {
                delItems.Add((theme.Name, theme.Name));
            }

            ImGui.Text("Delete saved");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(-1f);
            if (ImGuiWidgets.FilterCombo("##theme_delete", "Pick a saved theme to delete...", _deleteFilter, delItems, out string? delName)
                && delName != null)
            {
                try
                {
                    _controller.Catalog.DeleteUserTheme(delName);
                    _saveError = null;
                }
                catch (Exception ex)
                {
                    _saveError = $"Delete failed: {ex.Message}";
                }
            }
        }

        if (ImGui.Button("Refresh themes from disk", new float2(-1f, 0f)))
        {
            _controller.Catalog.Refresh();
        }
    }

    private void DrawFont(TerminalWindow window)
    {
        ImGui.TextDisabled("Font");

        var families = new List<(string Key, string Label)>();
        foreach (var family in PurrTTYFontManager.GetAvailableFontFamilies())
        {
            families.Add((family, family));
        }

        ImGui.Text("Family");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGuiWidgets.FilterCombo("##theme_fontfamily", window.Settings.FontFamily, _fontFilter, families, out string? pickedFont)
            && pickedFont != null)
        {
            window.Settings.FontFamily = pickedFont;
            _controller.PersistDisplayDefaults(window);
        }

        int fontSize = (int)window.Settings.FontSize;
        ImGui.Text("Size");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        // DragInt (not SliderInt) so the value supports both click-drag and
        // double-click-to-type an exact size.
        if (ImGui.DragInt("##theme_fontsize", ref fontSize, 0.25f, 4, 72, "%d px"))
        {
            window.Settings.FontSize = Math.Clamp(fontSize, 4, 72);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _controller.PersistDisplayDefaults(window);
        }
    }

    private void DrawOpacity(TerminalWindow window)
    {
        ImGui.TextDisabled("Opacity");
        OpacitySlider(window, "Foreground", "##theme_fg_opacity",
            static s => s.ForegroundOpacity, static (s, v) => s.ForegroundOpacity = v);
        OpacitySlider(window, "Background", "##theme_bg_opacity",
            static s => s.BackgroundOpacity, static (s, v) => s.BackgroundOpacity = v);
        OpacitySlider(window, "Cell background", "##theme_cellbg_opacity",
            static s => s.CellBackgroundOpacity, static (s, v) => s.CellBackgroundOpacity = v);
    }

    private void DrawAdvancedToggle(TerminalWindow window)
    {
        if (ImGui.Button(_showAdvanced ? "Advanced settings  (hide)" : "Advanced settings  (show)", new float2(-1f, 0f)))
        {
            _showAdvanced = !_showAdvanced;
        }

        if (!_showAdvanced)
        {
            return;
        }

        DrawCursor(window);
        ImGui.Separator();
        DrawBorder(window);
        ImGui.Separator();
        DrawLock(window);
    }

    private void DrawCursor(TerminalWindow window)
    {
        ImGui.TextDisabled("Cursor");

        int styleIndex = window.Settings.CursorStyle switch
        {
            CursorShape.Bar => 1,
            CursorShape.Underline => 2,
            _ => 0,
        };

        ImGui.Text("Style");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##theme_cursor_style", CursorStyles[styleIndex].Label))
        {
            for (int i = 0; i < CursorStyles.Length; i++)
            {
                if (ImGui.Selectable(CursorStyles[i].Label, i == styleIndex))
                {
                    window.SetCursorStyle(CursorStyles[i].Shape, window.Settings.CursorBlink);
                    _controller.PersistDisplayDefaults(window);
                }
            }

            ImGui.EndCombo();
        }

        bool blink = window.Settings.CursorBlink;
        if (ImGui.Checkbox("Blink", ref blink))
        {
            window.SetCursorStyle(window.Settings.CursorStyle, blink);
            _controller.PersistDisplayDefaults(window);
        }
    }

    private void DrawBorder(TerminalWindow window)
    {
        ImGui.TextDisabled("Window border");

        var settings = window.Settings;

        bool onFocus = settings.BorderOnFocus;
        if (ImGui.Checkbox("Show when focused", ref onFocus))
        {
            settings.BorderOnFocus = onFocus;
            _controller.PersistDisplayDefaults(window);
        }

        bool onHover = settings.BorderOnHover;
        if (ImGui.Checkbox("Show when hovered", ref onHover))
        {
            settings.BorderOnHover = onHover;
            _controller.PersistDisplayDefaults(window);
        }

        OpacitySlider(window, "Border opacity", "##theme_border_opacity",
            static s => s.BorderOpacity, static (s, v) => s.BorderOpacity = v);
    }

    private void DrawLock(TerminalWindow window)
    {
        ImGui.TextDisabled("Lock mode");

        var settings = window.Settings;

        bool lockMode = settings.LockMode;
        if (ImGui.Checkbox("Click-through when not focused", ref lockMode))
        {
            settings.LockMode = lockMode;
            _controller.PersistDisplayDefaults(window);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("While locked and unfocused, mouse input passes through to the game.\nRefocus via the hot zone, this dialog's target, or the toggle hotkey."u8);
        }

        bool hotZone = settings.HotZoneEnabled;
        if (ImGui.Checkbox("Focus hot zone", ref hotZone))
        {
            settings.HotZoneEnabled = hotZone;
            _controller.PersistDisplayDefaults(window);
        }

        int placementIndex = 0;
        for (int i = 0; i < HotZonePlacements.Length; i++)
        {
            if (HotZonePlacements[i].Placement == settings.HotZonePlacement)
            {
                placementIndex = i;
                break;
            }
        }

        ImGui.Text("Hot zone position");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("##theme_hotzone_pos", HotZonePlacements[placementIndex].Label))
        {
            for (int i = 0; i < HotZonePlacements.Length; i++)
            {
                if (ImGui.Selectable(HotZonePlacements[i].Label, i == placementIndex))
                {
                    settings.HotZonePlacement = HotZonePlacements[i].Placement;
                    _controller.PersistDisplayDefaults(window);
                }
            }

            ImGui.EndCombo();
        }

        HotZoneSizeSlider(window, "Hot zone width", "##theme_hotzone_w",
            static s => s.HotZoneWidth, static (s, v) => s.HotZoneWidth = v);
        HotZoneSizeSlider(window, "Hot zone height", "##theme_hotzone_h",
            static s => s.HotZoneHeight, static (s, v) => s.HotZoneHeight = v);

        var zoneColor = settings.HotZoneColor;
        var rgb = new float3(zoneColor.R / 255f, zoneColor.G / 255f, zoneColor.B / 255f);
        ImGui.Text("Hot zone color");
        ImGui.SameLine();
        if (ImGui.ColorEdit3("##theme_hotzone_color", ref rgb, ImGuiColorEditFlags.NoInputs))
        {
            settings.HotZoneColor = new RgbaColor(
                (byte)Math.Clamp(rgb.X * 255f, 0f, 255f),
                (byte)Math.Clamp(rgb.Y * 255f, 0f, 255f),
                (byte)Math.Clamp(rgb.Z * 255f, 0f, 255f));
            _hotZoneColorDirty = true;
        }

        // The swatch picker is a popup, so the item never reports deactivated-after-
        // edit; persist once the mouse is released (the open popup keeps this drawing).
        if (_hotZoneColorDirty && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _hotZoneColorDirty = false;
            _controller.PersistDisplayDefaults(window);
        }

        OpacitySlider(window, "Hot zone opacity", "##theme_hotzone_opacity",
            static s => s.HotZoneOpacity, static (s, v) => s.HotZoneOpacity = v);
        OpacitySlider(window, "Hot zone hover opacity", "##theme_hotzone_hover_opacity",
            static s => s.HotZoneHoverOpacity, static (s, v) => s.HotZoneHoverOpacity = v);
    }

    // Slider helpers take static (non-capturing) accessor lambdas over the settings
    // object — the compiler caches them, so drawing allocates nothing per slider.
    private void OpacitySlider(
        TerminalWindow window,
        string label,
        string id,
        Func<TerminalWindowSettings, float> get,
        Action<TerminalWindowSettings, float> set)
    {
        int percent = (int)MathF.Round(get(window.Settings) * 100f);
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt(id, ref percent, 0, 100, "%d%%"))
        {
            set(window.Settings, Math.Clamp(percent, 0, 100) / 100f);
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _controller.PersistDisplayDefaults(window);
        }
    }

    private void HotZoneSizeSlider(
        TerminalWindow window,
        string label,
        string id,
        Func<TerminalWindowSettings, float> get,
        Action<TerminalWindowSettings, float> set)
    {
        int pixels = (int)MathF.Round(get(window.Settings));
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.DragInt(id, ref pixels, 1f, (int)TerminalWindow.MinHotZoneSize, (int)TerminalWindow.MaxHotZoneSize, "%d px"))
        {
            set(window.Settings, Math.Clamp(pixels, (int)TerminalWindow.MinHotZoneSize, (int)TerminalWindow.MaxHotZoneSize));
        }

        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _controller.PersistDisplayDefaults(window);
        }
    }

    private static string KindLabel(TerminalKind kind) => kind == TerminalKind.InWorld ? "in-world" : "window";
}
