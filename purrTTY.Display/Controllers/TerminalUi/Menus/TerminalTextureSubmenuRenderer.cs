using Brutal.ImGuiApi;
using purrTTY.Display.Configuration;

namespace purrTTY.Display.Controllers.TerminalUi.Menus;

internal sealed class TerminalTextureSubmenuRenderer
{
    private readonly ThemeConfiguration _themeConfig;
    private readonly Action _persistThemeConfiguration;

    public TerminalTextureSubmenuRenderer(ThemeConfiguration themeConfig, Action persistThemeConfiguration)
    {
        _themeConfig = themeConfig ?? throw new ArgumentNullException(nameof(themeConfig));
        _persistThemeConfiguration = persistThemeConfiguration ?? throw new ArgumentNullException(nameof(persistThemeConfiguration));
    }

    public void RenderContent()
    {
        bool showPreview = _themeConfig.ShowTerminalTexturePreview;
        if (ImGui.Checkbox("Show texture preview", ref showPreview))
        {
            _themeConfig.ShowTerminalTexturePreview = showPreview;
            _persistThemeConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Allocate the experimental terminal render target and show it in a debug preview window.");
        }

        bool showWorldQuad = _themeConfig.ShowTerminalTextureWorldQuad;
        if (ImGui.Checkbox("Show world quad", ref showWorldQuad))
        {
            _themeConfig.ShowTerminalTextureWorldQuad = showWorldQuad;
            _persistThemeConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Submit the experimental terminal texture as a camera-facing world-space quad.");
        }
    }
}