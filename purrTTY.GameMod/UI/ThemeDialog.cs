using Brutal.ImGuiApi;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Rendering;
using purrTTY.Display.Theming;
using PurrTTY.Terminal.Rendering;
using float2 = Brutal.Numerics.float2;
using float3 = Brutal.Numerics.float3;

namespace purrTTY.GameMod.UI;

/// <summary>
///     The per-terminal theme manager dialog: pick a target terminal (any registered
///     2D window or in-world instance, defaulting to the focused one) and edit its
///     complete appearance bundle — palette, font family/size, the three opacities,
///     and advanced cursor/border/lock/hot-zone settings — plus rename it and
///     save/load/delete named themes. All edits apply to the <b>selected</b> target
///     and persist as the new-window defaults via the controller. Fixed-width modal
///     with table-based label/widget rows (KSA mod layout conventions).
/// </summary>
public sealed class ThemeDialog
{
    private const string PopupId = "purrTTY Theme##purrtty_theme_dialog";
    private const float DialogWidth = 580f;

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

    private bool _visible;
    private bool _requestFocus;
    private string? _selectedName;
    private bool _showAdvanced;
    private string? _renameError;
    private string? _saveError;
    private bool _hotZoneColorDirty;

    public ThemeDialog(GhosttyTerminalController controller) => _controller = controller;

    /// <summary>Shows (and brings to front) the dialog window, targeting the focused terminal.</summary>
    public void RequestOpen()
    {
        _visible = true;
        _requestFocus = true;
        _selectedName = _controller.FocusTarget?.Name ?? TerminalTargetRegistry.Focused?.Name;
        _renameError = null;
        _saveError = null;
        _renameInput.Clear();
        _saveNameInput.Clear();
    }

    /// <summary>
    ///     Draws the dialog as a non-modal, movable window (the game stays interactive
    ///     behind it). A no-op while hidden.
    /// </summary>
    public void Render()
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

        ImGui.SetNextWindowSize(new float2(DialogWidth, 640f), ImGuiCond.FirstUseEver);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(16f, 16f));
        bool visible = ImGui.Begin(PopupId, ref _visible, ImGuiWindowFlags.None);
        ImGui.PopStyleVar();

        if (!visible)
        {
            ImGui.End();
            return;
        }

        var target = ResolveTarget();
        if (target == null)
        {
            ImGui.TextDisabled("No terminal is open. Open a terminal, then reopen this dialog.");
        }
        else
        {
            DrawTargetSection(target);

            if (target is TerminalWindow window)
            {
                DrawPaletteSection(window);
                DrawFontSection(window);
                DrawOpacitySection(window);
                DrawAdvancedSection(window);
            }
            else
            {
                ImGui.SeparatorText("Theme");
                DrawPaletteApply(target);
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button(" Close ##theme_close", new float2(-1f, 0f)))
        {
            _visible = false;
        }

        ImGui.End();
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

    private void DrawTargetSection(INamedTerminal target)
    {
        ImGui.SeparatorText("Terminal");

        var items = new List<(string Key, string Label)>();
        var all = TerminalTargetRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var t = all[i];
            string star = t.HasFocus ? "  *focused" : string.Empty;
            items.Add((t.Name, $"{t.Name}  ({KindLabel(t.Kind)}){star}"));
        }

        if (ImGuiWidgets.BeginFormTable("##theme_target"))
        {
            ImGuiWidgets.FormRow("Target");
            if (ImGuiWidgets.FilterCombo("##theme_target_combo", $"{target.Name}  ({KindLabel(target.Kind)})",
                    _targetFilter, items, out string? picked) && picked != null)
            {
                _selectedName = picked;
            }

            ImGuiWidgets.FormRow("Rename to");
            ImGui.InputText("##theme_rename", _renameInput, ImGuiInputTextFlags.None);
            _renameInput.EvaluateLength();

            ImGuiWidgets.EndFormTable();
        }

        if (ImGui.Button(" Use focused terminal ##theme_usefocus"))
        {
            _selectedName = _controller.FocusTarget?.Name ?? TerminalTargetRegistry.Focused?.Name;
        }

        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Rename ##theme_renamebtn"))
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

        if (!string.IsNullOrEmpty(_renameError))
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiWidgets.WarningColor, _renameError);
        }
    }

    private void DrawPaletteSection(TerminalWindow window)
    {
        ImGui.SeparatorText("Theme");

        var items = BuildThemeItems();
        string saveName = _saveNameInput.ToString().Trim();

        if (ImGuiWidgets.BeginFormTable("##theme_palette"))
        {
            ImGuiWidgets.FormRow("Palette");
            if (ImGuiWidgets.FilterCombo("##theme_palette_combo", window.Settings.ThemeName, _paletteFilter, items, out string? pickedTheme)
                && pickedTheme != null
                && _controller.Catalog.Find(pickedTheme) is { } def)
            {
                window.ApplyTheme(def);
                _controller.PersistDisplayDefaults(window);
            }

            ImGuiWidgets.FormRow("Save as");
            ImGui.InputText("##theme_saveas", _saveNameInput, ImGuiInputTextFlags.None);
            _saveNameInput.EvaluateLength();

            if (_controller.Catalog.UserThemes.Count > 0)
            {
                var delItems = new List<(string Key, string Label)>();
                foreach (var theme in _controller.Catalog.UserThemes)
                {
                    delItems.Add((theme.Name, theme.Name));
                }

                ImGuiWidgets.FormRow("Delete saved");
                if (ImGuiWidgets.FilterCombo("##theme_delete", "Pick a saved theme...", _deleteFilter, delItems, out string? delName)
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

            ImGuiWidgets.EndFormTable();
        }

        bool canSave = saveName.Length > 0;
        if (!canSave)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button(" Save current settings as theme ##theme_save") && canSave)
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

        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Refresh ##theme_refresh"))
        {
            _controller.Catalog.Refresh();
        }

        if (canSave && _controller.Catalog.UserThemeExists(saveName))
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiWidgets.WarningColor, $"A saved theme named '{saveName}' will be overwritten.");
        }

        if (!string.IsNullOrEmpty(_saveError))
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiWidgets.ErrorColor, _saveError);
        }
    }

    private void DrawPaletteApply(INamedTerminal target)
    {
        var items = BuildThemeItems();
        if (ImGuiWidgets.BeginFormTable("##theme_apply"))
        {
            ImGuiWidgets.FormRow("Apply theme");
            if (ImGuiWidgets.FilterCombo("##theme_apply_combo", "Pick a saved theme to apply...", _paletteFilter, items, out string? picked)
                && picked != null
                && _controller.Catalog.Find(picked) is { } def)
            {
                target.ApplyTheme(def);
            }

            ImGuiWidgets.EndFormTable();
        }
    }

    private List<(string Key, string Label)> BuildThemeItems()
    {
        var items = new List<(string Key, string Label)>();
        foreach (var theme in _controller.Catalog.BuiltInThemes)
        {
            items.Add((theme.Name, theme.Name));
        }

        foreach (var theme in _controller.Catalog.UserThemes)
        {
            items.Add((theme.Name, $"{theme.Name}  (saved)"));
        }

        return items;
    }

    private void DrawFontSection(TerminalWindow window)
    {
        ImGui.SeparatorText("Font");

        var families = new List<(string Key, string Label)>();
        foreach (var family in PurrTTYFontManager.GetAvailableFontFamilies())
        {
            families.Add((family, family));
        }

        if (ImGuiWidgets.BeginFormTable("##theme_font"))
        {
            ImGuiWidgets.FormRow("Family");
            if (ImGuiWidgets.FilterCombo("##theme_fontfamily", window.Settings.FontFamily, _fontFilter, families, out string? pickedFont)
                && pickedFont != null)
            {
                window.Settings.FontFamily = pickedFont;
                _controller.PersistDisplayDefaults(window);
            }

            ImGuiWidgets.FormRow("Size");
            int fontSize = (int)window.Settings.FontSize;
            if (ImGui.DragInt("##theme_fontsize", ref fontSize, 0.25f, 4, 72, "%d px"))
            {
                window.Settings.FontSize = Math.Clamp(fontSize, 4, 72);
            }

            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                _controller.PersistDisplayDefaults(window);
            }

            ImGuiWidgets.EndFormTable();
        }
    }

    private void DrawOpacitySection(TerminalWindow window)
    {
        ImGui.SeparatorText("Opacity");

        if (ImGuiWidgets.BeginFormTable("##theme_opacity"))
        {
            OpacitySlider(window, "Foreground", "##theme_fg_opacity",
                static s => s.ForegroundOpacity, static (s, v) => s.ForegroundOpacity = v);
            OpacitySlider(window, "Background", "##theme_bg_opacity",
                static s => s.BackgroundOpacity, static (s, v) => s.BackgroundOpacity = v);
            OpacitySlider(window, "Cell background", "##theme_cellbg_opacity",
                static s => s.CellBackgroundOpacity, static (s, v) => s.CellBackgroundOpacity = v);
            ImGuiWidgets.EndFormTable();
        }
    }

    private void DrawAdvancedSection(TerminalWindow window)
    {
        ImGui.Spacing();
        if (ImGui.Button(_showAdvanced ? " Hide advanced settings ##theme_adv" : " Advanced settings ##theme_adv", new float2(-1f, 0f)))
        {
            _showAdvanced = !_showAdvanced;
        }

        if (!_showAdvanced)
        {
            return;
        }

        DrawCursor(window);
        DrawBorder(window);
        DrawLock(window);
    }

    private void DrawCursor(TerminalWindow window)
    {
        ImGui.SeparatorText("Cursor");

        int styleIndex = window.Settings.CursorStyle switch
        {
            CursorShape.Bar => 1,
            CursorShape.Underline => 2,
            _ => 0,
        };

        if (ImGuiWidgets.BeginFormTable("##theme_cursor"))
        {
            ImGuiWidgets.FormRow("Style");
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

            ImGuiWidgets.EndFormTable();
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
        ImGui.SeparatorText("Window border");

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

        if (ImGuiWidgets.BeginFormTable("##theme_border"))
        {
            OpacitySlider(window, "Border opacity", "##theme_border_opacity",
                static s => s.BorderOpacity, static (s, v) => s.BorderOpacity = v);
            ImGuiWidgets.EndFormTable();
        }
    }

    private void DrawLock(TerminalWindow window)
    {
        ImGui.SeparatorText("Lock mode");

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

        if (ImGuiWidgets.BeginFormTable("##theme_lock"))
        {
            ImGuiWidgets.FormRow("Hot zone position");
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

            ImGuiWidgets.FormRow("Hot zone color");
            var zoneColor = settings.HotZoneColor;
            var rgb = new float3(zoneColor.R / 255f, zoneColor.G / 255f, zoneColor.B / 255f);
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

            ImGuiWidgets.EndFormTable();
        }
    }

    // Slider helpers assume they run inside a BeginFormTable; they emit a FormRow then
    // the slider. Static (non-capturing) accessor lambdas — cached by the compiler.
    private void OpacitySlider(
        TerminalWindow window,
        string label,
        string id,
        Func<TerminalWindowSettings, float> get,
        Action<TerminalWindowSettings, float> set)
    {
        ImGuiWidgets.FormRow(label);
        int percent = (int)MathF.Round(get(window.Settings) * 100f);
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
        ImGuiWidgets.FormRow(label);
        int pixels = (int)MathF.Round(get(window.Settings));
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
