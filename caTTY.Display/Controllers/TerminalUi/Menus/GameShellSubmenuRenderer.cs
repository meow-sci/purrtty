using System;
using Brutal.ImGuiApi;
using caTTY.Display.Configuration;

namespace caTTY.Display.Controllers.TerminalUi.Menus;

/// <summary>
/// Handles rendering of the Game Shell submenu with prompt configuration options.
/// Allows users to customize the prompt string used by the Game Console Shell.
/// </summary>
internal class GameShellSubmenuRenderer
{
    private readonly ThemeConfiguration _themeConfig;
    private string _promptBuffer;

    public GameShellSubmenuRenderer(ThemeConfiguration themeConfig)
    {
        _themeConfig = themeConfig ?? throw new ArgumentNullException(nameof(themeConfig));
        _promptBuffer = _themeConfig.GameShellPrompt;
    }

    /// <summary>
    /// Renders the Game Shell submenu content with prompt configuration options.
    /// Note: Parent menu handles BeginMenu/EndMenu calls.
    /// </summary>
    public void RenderContent()
    {
        ImGui.Text("Game Shell Settings");
        ImGui.Separator();

        ImGui.Text($"Current Prompt: {_themeConfig.GameShellPrompt}");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("The prompt string shown in the Game Console Shell");
        }

        ImGui.Spacing();

        // Preset prompt options
        ImGui.Text("Quick Presets:");

        string[] presets = new[] { "ksa> ", "game> ", "$ ", "> ", "# " };

        foreach (var preset in presets)
        {
            if (ImGui.Button($"{preset}##preset_{preset}"))
            {
                _promptBuffer = preset;
                _themeConfig.GameShellPrompt = preset;
                _themeConfig.Save();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Set prompt to: {preset}");
            }

            ImGui.SameLine();
        }

        ImGui.NewLine();

        ImGui.Spacing();
        ImGui.TextColored(new Brutal.Numerics.float4(0.7f, 0.7f, 0.7f, 1.0f), "Note: Changes apply to new Game Console Shell sessions");
        ImGui.TextColored(new Brutal.Numerics.float4(0.7f, 0.7f, 0.7f, 1.0f), "Tip: Manually edit theme-config.json for custom prompts");
    }
}
