using Brutal.ImGuiApi;
using Brutal.Numerics;
using purrTTY.Display.Theming;

namespace purrTTY.Display.Ghostty;

// The lock-mode focus hot zone: a separate decoration-less window that stays
// mouse-interactive while the terminal itself is click-through (gotcha 22).
public sealed partial class TerminalWindow
{
    /// <summary>Lock-mode focus hot zone size limits (pixels).</summary>
    public const float MinHotZoneSize = 8f;
    public const float MaxHotZoneSize = 5000f;

    private readonly string _hotZoneImguiName;

    private void RenderHotZone(float2 windowPos, float2 windowSize)
    {
        float w = Math.Clamp(Settings.HotZoneWidth, MinHotZoneSize, Math.Max(MinHotZoneSize, windowSize.X));
        float h = Math.Clamp(Settings.HotZoneHeight, MinHotZoneSize, Math.Max(MinHotZoneSize, windowSize.Y));
        var size = new float2(w, h);
        var pos = HotZonePosition(windowPos, windowSize, w, h);

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        // WindowMinSize would otherwise inflate a small zone to 32x32, leaving
        // dead window area that eats game clicks the user expects to pass through.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new float2(1f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.Begin(_hotZoneImguiName,
            ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoScrollWithMouse);
        ImGui.PopStyleVar(3);

        ImGui.SetCursorScreenPos(pos);
        ImGui.InvisibleButton("##hotzone", size);
        bool hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemActivated())
        {
            // Focus on press (not click-release) so the terminal feels instant;
            // the next frame drops click-through and input works normally.
            RequestFocus();
        }

        if (hovered)
        {
            ImGui.SetTooltip("Focus terminal"u8);
        }

        float opacity = hovered ? Settings.HotZoneHoverOpacity : Settings.HotZoneOpacity;
        if (opacity > 0f)
        {
            // Foreground list: the fill must stay visible above the terminal
            // window's background regardless of ImGui window z-order.
            var fill = Settings.HotZoneColor.WithAlpha((byte)Math.Clamp(opacity * 255f, 0f, 255f));
            ImGui.GetForegroundDrawList().AddRectFilled(pos, pos + size, FrameGridRenderer.ToU32(fill), 3f);
        }

        ImGui.End();
    }

    private float2 HotZonePosition(float2 windowPos, float2 windowSize, float w, float h)
    {
        float left = windowPos.X;
        float centerX = windowPos.X + (windowSize.X - w) * 0.5f;
        float right = windowPos.X + windowSize.X - w;
        float top = windowPos.Y;
        float middleY = windowPos.Y + (windowSize.Y - h) * 0.5f;
        float bottom = windowPos.Y + windowSize.Y - h;

        return Settings.HotZonePlacement switch
        {
            HotZonePlacement.TopLeft => new float2(left, top),
            HotZonePlacement.TopCenter => new float2(centerX, top),
            HotZonePlacement.MiddleLeft => new float2(left, middleY),
            HotZonePlacement.MiddleRight => new float2(right, middleY),
            HotZonePlacement.BottomLeft => new float2(left, bottom),
            HotZonePlacement.BottomCenter => new float2(centerX, bottom),
            HotZonePlacement.BottomRight => new float2(right, bottom),
            _ => new float2(right, top),
        };
    }
}
